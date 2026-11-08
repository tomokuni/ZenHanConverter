<#
.SYNOPSIS
配布物（Native AOT）を作成し、起動と設定の往復を検証する。

.DESCRIPTION
`.github/検証手順.md` §18 が定める「配布物の検証」を実行する。
対象 UI の定義（publish スクリプト・出力フォルダ・配布名）は `.github/release-config.json` が単一所有し、
本スクリプトはそれを読む（UI を追加しても本スクリプトの変更は不要）。

検証の内容:
1. publish（`buildScript/publish_*.bat`）を実行し（`-SkipPublish` で省略）、出力フォルダの exe が配布名と一致することを確認する。
2. 実行ファイルが起動中でないことを確認する（起動中は設定ファイルが競合するため中止する）。
3. ユーザーの設定ファイル（%LOCALAPPDATA% 配下の製品名フォルダー（`release-config.json` の `product`）の settings.json）を退避する。
4. exe を起動し、メイン ウィンドウが現れるのを待って `WM_CLOSE` で正常終了させる（終了コード 0 を確かめる）。
5. 終了後の設定ファイルが空（`{}`）でなく、起動前と同じキーを保っていることを確認する（Native AOT のトリミングで
   設定が全損する不具合の回帰検出。 検証は [`検証手順.md`](../.github/検証手順.md) §18）。

**設定ファイルは必ず元へ戻す**（`try` / `finally`）。 アプリは終了時にウィンドウ配置などを保存するため、
戻さないとユーザーの実設定が書き換わる。

.PARAMETER Ui
対象の UI（既定は `All`）。 `.github/release-config.json` の `name` と一致させる。

.PARAMETER SkipPublish
publish を省略し、既存の出力フォルダを検証する（CI は publish 済みの成果物を検証する）。

.PARAMETER ForcePublish
publish の入力が前回と同一でも publish する（入力が同一のときは既定で省略する。 基準は
[`.github/検証手順.md`](../.github/検証手順.md) §18）。

.PARAMETER WindowTimeoutSeconds
メイン ウィンドウが現れるまでの待ち時間（既定 60 秒）。

.PARAMETER SettleSeconds
メイン ウィンドウが現れてから `WM_CLOSE` を送るまでの待ち時間（既定 2 秒）。
ウィンドウ ハンドルは XAML の初期化が終わる前に作られるため、直後に閉じると初期化の途中で破棄され、
アクセス違反（`0xC0000005`）で終了することがある（実測 2026-10-26: CI の dry-run で 1 回だけ
起動直後に `0xC0000005` で終了し、同じ実行の再実行では成功した。 ローカルでは 20 回再現しなかった）。

.PARAMETER ExitTimeoutSeconds
`WM_CLOSE` を送ってからプロセスが終了するまでの待ち時間（既定 60 秒）。

.EXAMPLE
pwsh -NoProfile -File buildScript/verifyPublish.ps1

.EXAMPLE
pwsh -NoProfile -File buildScript/verifyPublish.ps1 -Ui MewUI -SkipPublish
#>
[CmdletBinding()]
param(
    [ValidateSet('All', 'AvaloniaUI', 'MewUI', 'WinForms', 'WinUI3')][string]$Ui = 'All',
    [switch]$SkipPublish,
    [switch]$ForcePublish,
    [int]$WindowTimeoutSeconds = 60,
    [int]$SettleSeconds = 2,
    [int]$ExitTimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
$enc = [Text.UTF8Encoding]::new($false)
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repoRoot

# 結果の記録（Add-Result / Add-Info）とネイティブ コマンドの読み取り（Invoke-NativeText）は
# buildScript/VerifyCommon.ps1 が単一所有する（本スクリプトは自前の実装を持たない）。
# 文字化け対策の根拠は .github/検証手順.md §19。
. (Join-Path $PSScriptRoot 'VerifyCommon.ps1')

# メイン ウィンドウへ WM_CLOSE を送るためだけの Win32 呼び出し（強制終了させない）
Add-Type -Namespace SeVerifyPublish -Name Native -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
public static extern bool PostMessage(System.IntPtr hWnd, uint msg, System.IntPtr wParam, System.IntPtr lParam);
'@

# アプリが保存する設定ファイルの一覧は release-config.json の settingsFiles が単一所有する
# （ファイル名はリポジトリごとに違い、複数持つこともある）。 場所は %LOCALAPPDATA% 配下の製品フォルダー。
$verifyConfig = Get-RepoConfig $repoRoot
$settingsRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) $verifyConfig.product
$settingsPaths = @($verifyConfig.settingsFiles | ForEach-Object { Join-Path $settingsRoot $_ })
if ($settingsPaths.Count -eq 0) { throw '.github/release-config.json に settingsFiles がありません' }

# 設定ファイルの最上位キー（無い場合は $null、空の JSON は空配列）
function Get-SettingKeys {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseSingularNouns', '', Justification = 'コレクションを返す関数のため複数形にする')]
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $text = [IO.File]::ReadAllText($Path, $enc)
    if ([string]::IsNullOrWhiteSpace($text)) { return @() }

    $json = $text | ConvertFrom-Json -AsHashtable
    if ($json -isnot [System.Collections.IDictionary]) { return @() }
    return @($json.Keys | Sort-Object)
}

# exe を起動し、ウィンドウが現れてから正常終了させる
function Invoke-AppRoundTrip([string]$ExePath, [int]$WindowTimeout, [int]$Settle, [int]$ExitTimeout) {
    # 起動と終了の通過点をアプリに書かせ、異常終了の原因を CI のログだけで追えるようにする
    # （アプリ側は環境変数 `ZENHANCONVERTER_STARTUP_LOG` が未設定なら何もしない）
    $startupLog = Join-Path ([IO.Path]::GetTempPath()) ("{0}-startup-{1}.log" -f $verifyConfig.product.ToLowerInvariant(), [guid]::NewGuid().ToString('N'))
    $env:ZENHANCONVERTER_STARTUP_LOG = $startupLog

    $process = Start-Process -FilePath $ExePath -PassThru

    $handle = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds($WindowTimeout)
    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) { break }
        $process.Refresh()
        $handle = $process.MainWindowHandle
        if ($handle -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 250
    }

    if ($handle -eq [IntPtr]::Zero) {
        if (-not $process.HasExited) { $process.Kill() }
        return [pscustomobject]@{ Closed = $false; Reason = "メイン ウィンドウが $WindowTimeout 秒以内に現れませんでした" }
    }

    # ウィンドウ ハンドルは XAML の初期化が終わる前に作られる。 直後に閉じると初期化の途中で
    # 破棄され、アクセス違反（0xC0000005）で終了することがある（実測 2026-10-26: CI の dry-run で
    # 1 回だけ起動直後に 0xC0000005 で終了し、再実行では成功した。 ローカルでは 20 回再現しなかった）。
    # 立ち上がりきるのを待ってから閉じる（待つだけで、判定は弱めない）。
    Start-Sleep -Seconds $Settle
    if ($process.HasExited) {
        return [pscustomobject]@{ Closed = $false; Reason = "ウィンドウが現れた後に終了しました（終了コード $($process.ExitCode)）" }
    }

    [void][SeVerifyPublish.Native]::PostMessage($handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)   # WM_CLOSE
    if (-not $process.WaitForExit($ExitTimeout * 1000)) {
        $process.Kill()
        return [pscustomobject]@{ Closed = $false; Reason = "WM_CLOSE 後 $ExitTimeout 秒以内に終了しませんでした" }
    }

    # 終了できただけでは合格にしない（ウィンドウが出ても異常終了していれば不具合である）
    if ($process.ExitCode -ne 0) {
        $detail = Get-CrashDiagnostics ([IO.Path]::GetFileName($ExePath)) + Get-StartupTrace $startupLog
        return [pscustomobject]@{ Closed = $false; Reason = "終了コード $($process.ExitCode) で終了しました（異常終了）$detail" }
    }

    # 成功時もトレースと環境変数を片付ける（失敗時は上の分岐で片付く）
    [void](Get-StartupTrace $startupLog)
    return [pscustomobject]@{ Closed = $true; Reason = '' }
}

# アプリが書き出した起動トレースを、不合格の理由へ添える（ファイルは読み終わったら消す）
function Get-StartupTrace([string]$Path) {
    try {
        if (-not (Test-Path -LiteralPath $Path)) { return '（起動トレースなし）' }
        $lines = @(Get-Content -LiteralPath $Path -Encoding utf8)
        Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
        if ($lines.Count -eq 0) { return '（起動トレースは空）' }
        return ('（起動トレース: ' + (($lines | Select-Object -Last 8) -join ' | ') + '）')
    }
    catch {
        return ''
    }
    finally {
        # 成否に関わらず環境変数は残さない（後続の UI の検証へ持ち越さない）
        Remove-Item Env:\ZENHANCONVERTER_STARTUP_LOG -ErrorAction SilentlyContinue
    }
}

# 異常終了の原因を CI のログだけで分かるようにする（アプリケーション ログの障害報告から、
# 障害が起きたモジュールと例外コードを拾う）。 取得できなくても検証は止めない（診断は補助情報）。
function Get-CrashDiagnostics([string]$ExeName) {
    if (-not $IsWindows) { return '' }
    try {
        $events = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = (Get-Date).AddMinutes(-3) } -ErrorAction Stop |
                Where-Object { $_.Message -and $_.Message.Contains($ExeName, [StringComparison]::OrdinalIgnoreCase) } |
                Select-Object -First 2)
        if ($events.Count -eq 0) { return '' }
        $lines = foreach ($e in $events) {
            # 「障害が発生しているモジュール名」「例外コード」の行だけを抜く（ログを肥大化させない）
            foreach ($line in ($e.Message -split "`r?`n")) {
                if ($line -match 'モジュール名|例外コード|Faulting module|Exception code') { " / $($line.Trim())" }
            }
        }
        return ("（診断: " + (($lines -join '') -replace '^ / ', '') + "）")
    }
    catch {
        return ''
    }
}

# リリース設定（UI の定義）を読む
$config = [IO.File]::ReadAllText((Join-Path $repoRoot '.github/release-config.json'), $enc) | ConvertFrom-Json
$targets = @($config.uis | Where-Object { $Ui -eq 'All' -or $_.name -eq $Ui })
if ($targets.Count -eq 0) {
    Write-Output "FAIL  対象 UI が見つかりません: $Ui"
    exit 1
}

# publish の入力の指紋（追跡・未追跡のソース、ビルド設定、SDK のバージョン、publish スクリプト）。
# 前回と同一で出力フォルダが残っていれば publish を省略する（検証手順 §18。 実測: 直前のビルドで
# AOT の無効化が起きると同じ入力でも 35 秒/UI かかるのに対し、検証そのものは 1.4 秒）。
# 復元結果（各プロジェクトの `project.assets.json`）は毎回書き換わるため指紋に含めない（含めると常に「変化」になる）。
function Get-PublishFingerprint([string]$ScriptPath) {
    $sb = [Text.StringBuilder]::new()
    $paths = [System.Collections.Generic.List[string]]::new()
    # `buildScript` 全体は含めない（本スクリプトや verify.ps1 の編集で publish が無効化されてしまう）。
    # publish スクリプトの内容は下で個別に指紋へ入れる。
    # 一覧は Get-GitList（VerifyCommon.ps1）が取る（未追跡も含める。 結果は 1 回だけ読んで使い回される）
    foreach ($rel in (Get-GitList @('-c', 'core.quotepath=false', 'ls-files', '--', 'src', 'Directory.Build.props', 'Directory.Build.targets', 'global.json', '.github/release-config.json'))) { $paths.Add($rel) }
    # 未追跡（コミット前）のソースも入力である
    foreach ($rel in (Get-GitList @('-c', 'core.quotepath=false', 'ls-files', '--others', '--exclude-standard', '--', 'src'))) { $paths.Add($rel) }
    foreach ($rel in ($paths | Sort-Object -Unique)) {
        $full = Join-Path $repoRoot $rel
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }
        $info = [IO.FileInfo]::new($full)
        [void]$sb.Append($rel).Append('|').Append($info.Length).Append('|').Append($info.LastWriteTimeUtc.Ticks).Append("`n")
    }
    # リポジトリの場所（別の複製と指紋を共有しない）・SDK のバージョン・publish スクリプト自身
    [void]$sb.Append('root:').Append($repoRoot).Append("`n")
    [void]$sb.Append('sdk:').Append($script:SdkVersion).Append("`n")
    $scriptFull = Join-Path $repoRoot ($ScriptPath -replace '/', '\')
    if (Test-Path -LiteralPath $scriptFull -PathType Leaf) {
        $scriptInfo = [IO.FileInfo]::new($scriptFull)
        [void]$sb.Append('script:').Append($ScriptPath).Append('|').Append($scriptInfo.Length).Append('|').Append($scriptInfo.LastWriteTimeUtc.Ticks).Append("`n")
    }
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($sb.ToString())))
}

# SDK のバージョンは指紋の入力である（出力の復号は Invoke-NativeText が行う）
$script:SdkVersion = (Invoke-NativeText 'dotnet' @('--version')).Text.Trim()
$fingerprintDir = Join-Path ([IO.Path]::GetTempPath()) ("{0}-verify-publish" -f $verifyConfig.product.ToLowerInvariant())

# 退避した設定ファイル（パス -> 退避先）。 finally で必ず戻す
$settingsBackups = @{}
# 退避先は**ユーザーのフォルダーの外**（一時フォルダー）へ置き、名前に GUID を付ける。
# 設定ファイルの隣の固定名（`settings.json.bak`）へ `-Force` で上書きすると、利用者が残していた
# 復旧用のバックアップを消してしまう（実測 2026-10-28: レビューで指摘）。
$settingsBackupDir = Join-Path ([IO.Path]::GetTempPath()) ("{0}-verify-settings-{1}" -f $verifyConfig.product.ToLowerInvariant(), [guid]::NewGuid().ToString('N'))

try {
    foreach ($target in $targets) {
        Write-Output "== $($target.name) =="
        $outputDir = Join-Path $repoRoot (($target.output) -replace '/', '\')

        # 1. publish（-SkipPublish のときは既存の出力を使う。 入力が同一なら省略する）
        if (-not $SkipPublish) {
            $scriptPath = ($target.script) -replace '/', '\'
            $fingerprint = Get-PublishFingerprint $target.script
            $fingerprintPath = Join-Path $fingerprintDir "$($target.name).txt"
            $previous = if (Test-Path -LiteralPath $fingerprintPath) { [IO.File]::ReadAllText($fingerprintPath, $enc).Trim() } else { '' }
            if (-not $ForcePublish -and $previous -eq $fingerprint -and (Test-Path -LiteralPath $outputDir -PathType Container)) {
                Add-Info "$($target.name) publish" '省略（入力が前回と同一。 -ForcePublish で強制）'
            }
            else {
                Write-Output "      publish: $scriptPath"
                # バッチ（.bat）は `cmd.exe` 経由で起動する。 出力の復号は Invoke-NativeText が行う。
                $publish = Invoke-NativeText $env:ComSpec @('/c', (Join-Path $repoRoot $scriptPath))
                $publishText = $publish.Text + $publish.ErrorText
                if ($publish.ExitCode -ne 0) {
                    Add-Result "$($target.name) publish" $false "終了コード $($publish.ExitCode)"
                    $publishText -split "`r?`n" | Where-Object { $_ -match 'error|ERROR' } | Select-Object -First 5 | ForEach-Object { "      $($_.Trim())" }
                    continue
                }
                New-Item -ItemType Directory -Path $fingerprintDir -Force | Out-Null
                [IO.File]::WriteAllText($fingerprintPath, $fingerprint, $enc)
            }
        }

        if (-not (Test-Path -LiteralPath $outputDir -PathType Container)) {
            Add-Result "$($target.name) 配布物" $false "出力フォルダがありません: $($target.output)"
            continue
        }

        # exe が 1 つだけあり、名前が配布設定（release-config.json の exe）と一致することを確認する
        $expectedExeName = $target.exe
        if ([string]::IsNullOrEmpty($expectedExeName)) {
            Add-Result "$($target.name) 配布物" $false '.github/release-config.json に exe がありません'
            continue
        }

        $exes = @(Get-ChildItem -LiteralPath $outputDir -File -Filter '*.exe')
        Add-Result "$($target.name) 配布物" ($exes.Count -eq 1 -and $exes[0].Name -eq $expectedExeName) "$($target.output) の exe $($exes.Count) 個 / 期待 $expectedExeName"
        if ($exes.Count -ne 1) {
            $exes | ForEach-Object { "      $($_.Name)" }
            continue
        }

        $exe = $exes[0]

        # 構成とサイズを実測して出力する（文書には書かない。 コードの変更で変わるため）
        $distFiles = @(Get-ChildItem -LiteralPath $outputDir -File)
        $distBytes = 0.0
        foreach ($distFile in $distFiles) { $distBytes += $distFile.Length }
        $distDlls = @($distFiles | Where-Object { $_.Extension -eq '.dll' })
        Write-Output ("      構成: exe 1 個（{0:N1} MB）+ ネイティブ DLL {1} 個 / 合計 {2:N1} MB / {3} ファイル" -f ($exe.Length / 1MB), $distDlls.Count, ($distBytes / 1MB), $distFiles.Count)

        # 2. 起動中のプロセスがあると設定ファイルが競合する（検証結果が不定になる）
        $running = @(Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($exe.Name)) -ErrorAction SilentlyContinue)
        if ($running.Count -gt 0) {
            Add-Result "$($target.name) 起動と終了" $false "$($exe.Name) が起動中です。終了してから実行してください"
            continue
        }

        # 3. 設定ファイルを退避する（複数のことがある）
        $settingsBackups = @{}
        if ($settingsPaths.Count -gt 0 -and -not (Test-Path -LiteralPath $settingsBackupDir)) {
            [void](New-Item -ItemType Directory -Path $settingsBackupDir -Force)
        }
        foreach ($settingsPath in $settingsPaths) {
            if (Test-Path -LiteralPath $settingsPath) {
                # 同じ名前の設定ファイルが複数あっても衝突しないよう、連番を付ける
                $backup = Join-Path $settingsBackupDir ("{0}-{1}" -f $settingsBackups.Count, [IO.Path]::GetFileName($settingsPath))
                Copy-Item -LiteralPath $settingsPath -Destination $backup -Force
                $settingsBackups[$settingsPath] = $backup
            }
        }
        $beforeCount = 0
        $beforeKeys = @{}
        foreach ($settingsPath in $settingsPaths) {
            $keys = Get-SettingKeys $settingsPath
            # `@($null)` は 1 要素の配列になるため、$null は空配列として扱う
            $beforeKeys[$settingsPath] = if ($null -eq $keys) { @() } else { @($keys) }
            if ($null -ne $keys) { $beforeCount += $keys.Count }
        }

        # 4. 起動と終了（正常終了させる。 強制終了では終了時の保存が走らない）
        $roundTrip = Invoke-AppRoundTrip -ExePath $exe.FullName -WindowTimeout $WindowTimeoutSeconds -Settle $SettleSeconds -ExitTimeout $ExitTimeoutSeconds
        Add-Result "$($target.name) 起動と終了" $roundTrip.Closed $(if ($roundTrip.Closed) { $exe.Name } else { $roundTrip.Reason })

        # 5. 設定の往復（空になっていないこと・キーが保たれていること）
        # 判定は「設定が失われていないこと」。 UI によっては起動時に全ファイルを書かないため、
        # **各ファイルが空でないこと**ではなく「起動前にあったキーが残っていること」と
        # 「1 つ以上の設定が書かれていること」で判定する（実測 2026-10-26: WinUI 3 版は
        # 終了時に AppSetting.json だけを書く）。
        $afterCount = 0
        $keysKept = $true
        foreach ($settingsPath in $settingsPaths) {
            $rawAfter = Get-SettingKeys $settingsPath
            $after = if ($null -eq $rawAfter) { @() } else { @($rawAfter) }
            $afterCount += $after.Count
            foreach ($key in $beforeKeys[$settingsPath]) {
                if ($after -notcontains $key) { $keysKept = $false }
            }
        }
        if ($afterCount -eq 0) { $keysKept = $false }
        Add-Result "$($target.name) 設定の往復" $keysKept "起動前 $beforeCount キー / 終了後 $afterCount キー"
        if (-not $keysKept) {
            Write-Output '      設定ファイルが空になったか、キーが失われています（Native AOT のトリミングで設定が全損する不具合の疑い）'
        }

        # 6. 設定ファイルを元へ戻す（次の UI の検証もユーザーの実設定から始める）
        #    **戻した退避ファイルは削除してから辞書を空にする**（空にしてからでは finally が
        #    退避先を見つけられず、一時フォルダーにコピーが残る。 実測 2026-10-28: レビューで指摘）。
        foreach ($settingsPath in $settingsPaths) {
            if ($settingsBackups.ContainsKey($settingsPath)) {
                Copy-Item -LiteralPath $settingsBackups[$settingsPath] -Destination $settingsPath -Force
                Remove-Item -LiteralPath $settingsBackups[$settingsPath] -Force
            }
        }
        $settingsBackups = @{}
    }
}
finally {
    # 途中で失敗してもユーザーの設定ファイルを必ず戻す
    foreach ($key in $settingsBackups.Keys) {
        if (Test-Path -LiteralPath $settingsBackups[$key]) {
            Copy-Item -LiteralPath $settingsBackups[$key] -Destination $key -Force
            Remove-Item -LiteralPath $settingsBackups[$key] -Force
        }
    }
    # 退避用の一時フォルダーは残さない（空でない場合だけ消す）
    if (Test-Path -LiteralPath $settingsBackupDir) {
        Remove-Item -LiteralPath $settingsBackupDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Output ''
Write-Output '===== 配布物の検証の要約 ====='
foreach ($result in $script:Results) {
    $mark = if ($result.Kind -eq 'Info') { 'INFO' } elseif ($result.Ok) { 'OK  ' } else { 'FAIL' }
    Write-Output "$mark  $($result.Name)`t$($result.Detail)"
}

$failed = @($script:Results | Where-Object { $_.Kind -eq 'Check' -and -not $_.Ok })
if ($failed.Count -gt 0) {
    Write-Output ''
    Write-Output "不合格 $($failed.Count) 項目: $(($failed | ForEach-Object { $_.Name }) -join ', ')"
    exit 1
}

Write-Output ''
Write-Output "すべて合格（合否 $(@($script:Results | Where-Object { $_.Kind -eq 'Check' }).Count) 項目）"
