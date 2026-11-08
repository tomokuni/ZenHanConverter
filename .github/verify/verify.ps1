<#
.SYNOPSIS
リポジトリの検証をすべて実行し、合否を要約する。

.DESCRIPTION
`.github/検証手順.md` が定める検証を順に実行し、各項目の合否と件数を表示する（不合格があれば終了コード 1）。
Copilot・CI・開発者が同じ手順を 1 コマンドで再現できるようにする（手作業の再実装をなくす）。

.PARAMETER Solution
対象のソリューション ファイル（既定: release-config.json の solutionFile）。 CI は同じ値を渡す。

.PARAMETER Configuration
ビルド構成（既定: Debug）。 CI は Release を渡す。

.PARAMETER Fix
自動修正できる項目を修正する（markdownlint --fix / dotnet format）。

.PARAMETER SkipTests
テストの実行を省略する（ビルドだけを素早く確かめるとき）。

.PARAMETER SkipBuild
ビルドを省略し、直前にビルドした出力を使う（文書だけを直したときの往復を短くする）。
テストは `--no-build` で実行するため、**一度もビルドしていないと失敗する**（先にビルドを 1 回実行する）。

.PARAMETER SkipFormat
コード スタイルの検査（`dotnet format`。 既定では省略する）を省略したものとして扱う。

.PARAMETER FullFormat
dotnet format による検査（警告以上のコード スタイル。 規則を足したときの確認用）を実行する（既定では省略し、ビルドと走査で代替する）。

.PARAMETER SkipPackage
NuGet の健全性（既知の脆弱性。 推移的依存を含む）の検査を省略する（ネットワークを使いたくないとき）。

.PARAMETER Coverage
テスト時にカバレッジを取得し、対象ファイルごとの被覆率を集計する（テスト規約の「カバレッジ 100% を目指す」の数値確認用）。

.PARAMETER FullBuild
`--no-incremental` を付けて**全プロジェクトを必ず作り直す**（既定は付けない）。
既定で付けない理由: MSBuild の最新性チェックは`.editorconfig` / `Directory.Build.props` / 参照の変更も入力に含むため
（実測 2026-10-27: `.editorconfig` を変えると再コンパイルされる）、CI の新規チェックアウト（`obj` が空）では
`--no-incremental` と結果が同じで、無駄に 30 秒かかるだけだった（実測: 30 秒 → 7 秒）。
**キャッシュを疑うときだけ**付ける。

.PARAMETER SkipDocs
文書とコードの走査（ビルド・テストと重ねて実行する独立した検査）を省略する。
**CI が走査を別のジョブへ分けたときに使う**（同じ走査を 2 回走らせないため）。 省略した検査は合否に含めない。

.PARAMETER NoRestore
`--no-restore` を付ける（呼び出し側が復元済みのとき。 CI は復元を別ステップで行う）。

.EXAMPLE
pwsh -NoProfile -File .github/verify/verify.ps1
.EXAMPLE
pwsh -NoProfile -File .github/verify/verify.ps1 -Fix
.EXAMPLE
pwsh -NoProfile -File .github/verify/verify.ps1 -Configuration Release -Solution <solution>.slnx -NoRestore
#>
[CmdletBinding()]
param(
    # 既定は release-config.json の solutionFile（リポジトリごとに違うため、本文には書かない）
    [string]$Solution = '',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [switch]$Fix,
    [switch]$SkipBuild,
    [switch]$SkipTests,
    [switch]$SkipFormat,
    [switch]$FullFormat,
    [switch]$SkipPackage,
    [switch]$Coverage,
    [switch]$SkipDocs,
    [switch]$FullBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$enc = [Text.UTF8Encoding]::new($false)
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repoRoot


. (Join-Path $PSScriptRoot '..\verify\VerifyCommon.ps1')

# 各節の所要時間を記録する（どこに時間を使っているかを出力に残し、最適化を推測でやらないため）。
$scriptClock = [Diagnostics.Stopwatch]::StartNew()
$sectionClock = [Diagnostics.Stopwatch]::StartNew()
$sectionTimings = [System.Collections.Generic.List[string]]::new()
function Add-SectionTiming([string]$Name) {
    $sectionTimings.Add(("{0} {1:N1} 秒" -f $Name, $sectionClock.Elapsed.TotalSeconds))
    $sectionClock.Restart()
}

# 対象のソリューション ファイルは release-config.json が単一所有する
if (-not $Solution) { $Solution = (Get-RepoConfig $repoRoot).solutionFile }

# テストの実行基盤（VSTest / Microsoft.Testing.Platform）は global.json が単一所有する
$globalJsonPath = Join-Path $repoRoot 'global.json'
$globalJsonText = if (Test-Path -LiteralPath $globalJsonPath) { [IO.File]::ReadAllText($globalJsonPath, $enc) } else { '' }

# `dotnet` を実行し、**出力と終了コード**を返す。 出力の文字列だけを見ると、CLI 自体の異常終了
# （引数の誤り・復元の失敗など、警告の形で出ないもの）を「警告 0 件」と読み替えてしまうため、
# 呼び出し側は終了コードも合否へ含める。
# 出力の復号は Invoke-NativeText（VerifyCommon.ps1）が所有する（`& dotnet` は環境によっては
# 日本語の出力を文字化けさせ、テストの件数を読めなくする）。
function Invoke-DotNet([string[]]$Arguments) {
    $result = Invoke-NativeText 'dotnet' $Arguments
    return [pscustomobject]@{ Text = $result.Text + $result.ErrorText; ExitCode = $result.ExitCode }
}

# 文書とコードの走査（5〜10 節）は .github/verify/verifyDocs.ps1 が所有する。
# 走査は 1 ファイルずつ読む独立した作業であり、ビルド・テストと重ねられる（実測: 全体 47 秒 → 34 秒）。
# **先に起動してからビルドへ進む**（起動が遅れると重ならない）。
# 自動修正（-Fix）のときは直列にする（修正と判定の順序が決まっているため。 重ねない）。
function Start-DocsVerification {
    $base = Join-Path ([IO.Path]::GetTempPath()) ("se_verify_docs_" + [Guid]::NewGuid().ToString('N'))
    $arguments = @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'verifyDocs.ps1'))
    if ($Fix) { $arguments += '-Fix' }

    # 実行中の PowerShell 自身を使う（`pwsh` の解決に依存しない）
    $pwsh = (Get-Process -Id $PID).Path
    $process = Start-Process -FilePath $pwsh -ArgumentList $arguments -WorkingDirectory $repoRoot -NoNewWindow -PassThru `
        -RedirectStandardOutput ($base + '.out') -RedirectStandardError ($base + '.err')
    return [pscustomobject]@{ Process = $process; OutFile = $base + '.out'; ErrFile = $base + '.err' }
}

# 走査の子プロセスと一時ファイルの後始末をする（回収済みなら何もしない。 二重に呼んでも安全）。
function Stop-DocsVerification($Background) {
    if ($null -eq $Background) {
        return
    }

    if (-not $Background.Process.HasExited) {
        $Background.Process.Kill()
        $Background.Process.WaitForExit()
    }

    foreach ($file in @($Background.OutFile, $Background.ErrFile)) {
        Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue
    }
}

# 走査の出力をそのまま出し、合否を取り込む。 完走の印（##DOCS-DONE）が無ければ不合格にする
# （走査の失敗を「違反 0 件」と読み替えないため）。
function Receive-DocsVerification($Background) {
    $Background.Process.WaitForExit()
    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($file in @($Background.OutFile, $Background.ErrFile)) {
        if (Test-Path -LiteralPath $file) { $lines.AddRange([IO.File]::ReadAllLines($file, $enc)) }
        Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue
    }

    $completed = $false
    foreach ($line in $lines) {
        if ($line -match '^##DOCS-DONE\s+(\d+)\s+(\d+)$') { $completed = $true; continue }
        Write-Output $line
        $m = [regex]::Match($line, '^(OK|FAIL|INFO)\s+([^\t]+)\t(.*)$')
        if (-not $m.Success) { continue }
        $mark = $m.Groups[1].Value
        $script:Results.Add([pscustomobject]@{
                Name   = $m.Groups[2].Value
                Ok     = ($mark -ne 'FAIL')
                Kind   = if ($mark -eq 'INFO') { 'Info' } else { 'Check' }
                Detail = $m.Groups[3].Value
            })
    }

    if (-not $completed) {
        Add-Result '文書とコードの走査' $false '走査を完了できなかった（.github/verify/verifyDocs.ps1 の起動または実行に失敗した）'
    }
}
# ネットワーク照会や MSBuild の評価は待ち時間が長い（実測: NuGet の照会 35 秒 / dotnet format 20 秒）ため、
# 背景へ投入して他の検査と重ねる。 `Start-Job`（PowerShell をもう 1 つ立ち上げる）より軽く、出力は一時ファイルへ書き出す。
function Start-BackgroundDotNet([string[]]$Arguments) {
    $base = Join-Path ([IO.Path]::GetTempPath()) ("se_verify_" + [Guid]::NewGuid().ToString('N'))
    $process = Start-Process -FilePath 'dotnet' -ArgumentList $Arguments -WorkingDirectory $repoRoot -NoNewWindow -PassThru `
        -RedirectStandardOutput ($base + '.out') -RedirectStandardError ($base + '.err')
    return [pscustomobject]@{ Process = $process; OutFile = $base + '.out'; ErrFile = $base + '.err' }
}

# 背景実行の完了を待ち、出力（標準出力 + 標準エラー）と終了コードを返す（一時ファイルは消す）
function Receive-BackgroundDotNet($Background) {
    $Background.Process.WaitForExit()
    $text = ''
    foreach ($file in @($Background.OutFile, $Background.ErrFile)) {
        if (Test-Path -LiteralPath $file) { $text += [IO.File]::ReadAllText($file, $enc) }
        Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue
    }
    return [pscustomobject]@{ Text = $text; ExitCode = $Background.Process.ExitCode }
}

# 設定に応じた共通引数（復元の有無は呼び出し側が決める）
$restoreArgs = if ($NoRestore) { @('--no-restore') } else { @() }

# 文書とコードの走査をここで起動する（ビルド・テストと重ねるため、結果を出す位置より先に始める）。
# -Fix のときは直列にする（修正と判定の順序が決まっているため）ので、ここでは起動しない。
# -SkipDocs のときは起動しない（CI が走査を別のジョブへ分けたとき。 同じ走査を 2 回走らせない）。
$docsBackground = if ($Fix -or $SkipDocs) { $null } else { Start-DocsVerification }

# 途中で例外が出ても走査の子プロセスと一時ファイルを残さない（`break` で例外はそのまま伝える）。
trap {
    Stop-DocsVerification $docsBackground
    break
}

# ============================================================
# 1. ビルド（0 エラー / 0 警告。 IDE0005・書式・アナライザー・ドキュメントコメントを同時に検証する）
# ============================================================
if (-not $SkipBuild) {
    Write-Output '== 1. ビルド =='
    # 警告のエラー化は Directory.Build.props の TreatWarningsAsErrors が単一所有する（-warnaserror は付けない）。
    # これで素の `dotnet build` でも規約違反が終了コードに出る（スクリプトの引数に依存しない）。
    # 既定は増分ビルド（最新性チェックは .editorconfig / Directory.Build.props / 参照も入力に含む）。
    # 全プロジェクトを作り直すのは -FullBuild のときだけ（規則は .github/検証手順.md §3）。
    $buildArgs = @('build', $Solution, '-c', $Configuration, '-tl:off', '-v:q')
    if ($FullBuild) { $buildArgs += '--no-incremental' }
    $buildArgs += $restoreArgs
    $build = Invoke-DotNet $buildArgs
    $buildText = $build.Text
    $buildWarnings = ([regex]::Matches($buildText, 'warning ')).Count
    $buildErrors = ([regex]::Matches($buildText, ': error ')).Count
    Add-Result 'ビルド' ($buildWarnings -eq 0 -and $buildErrors -eq 0 -and $build.ExitCode -eq 0) "警告 $buildWarnings / エラー $buildErrors / 終了コード $($build.ExitCode)"
    if ($buildWarnings -gt 0 -or $buildErrors -gt 0) {
        $buildText -split "`r?`n" | Where-Object { $_ -match 'warning |: error ' } | Select-Object -First 20 | ForEach-Object { "      $($_.Trim())" }
    }
}
else {
    # 省略した検査は合否に含めない（-SkipBuild は文書だけを直したときの往復を短くする用途）
    Add-Info 'ビルド' '省略（-SkipBuild。 直前にビルドした出力を使う）'
}
Add-SectionTiming 'ビルド'


# ============================================================
# 2. ビルド設定（単一所有と配布物へ XML を出さないこと）
# ============================================================
Write-Output '== 2. ビルド設定 =='
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
$propsText = if (Test-Path -LiteralPath $propsPath) { [IO.File]::ReadAllText($propsPath, $enc) } else { '' }
$missingProps = [System.Collections.Generic.List[string]]::new()
# 必須のビルド設定はリポジトリごとに違う（規則文書と解析の厳しさを持つリポジトリだけが要求する）ため、
# release-config.json の requiredBuildProps が単一所有する。
foreach ($p in (Get-RepoConfig $repoRoot).requiredBuildProps) {
    if ($propsText -notmatch "<$p>") { $missingProps.Add($p) }
}
# 各 .csproj が重複して指定していないか（単一所有の確認）
$duplicateProps = @(Get-ChildItem -Path (Join-Path $repoRoot 'src') -Recurse -File -Filter '*.csproj' |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
        Where-Object { [IO.File]::ReadAllText($_.FullName) -match '<EnforceCodeStyleInBuild>|<GenerateDocumentationFile>|PublishDocumentationFiles|<AnalysisLevel>|<AnalysisMode>|<TreatWarningsAsErrors>' })
Add-Result 'ビルド設定' ($missingProps.Count -eq 0 -and $duplicateProps.Count -eq 0) "不足 $($missingProps.Count) / 重複 $($duplicateProps.Count)"
$missingProps | ForEach-Object { "      Directory.Build.props に無い: $_" }
$duplicateProps | ForEach-Object { "      重複指定: $($_.FullName.Substring($repoRoot.Length + 1))" }

# 2a) パッケージの版の単一所有（Central Package Management）
#     版は Directory.Packages.props が持ち、各 .csproj では指定しない（規則は .github/ビルド設定規約.md §4）。
$packagesPropsPath = Join-Path $repoRoot 'Directory.Packages.props'
$packagesPropsText = if (Test-Path -LiteralPath $packagesPropsPath) { [IO.File]::ReadAllText($packagesPropsPath, $enc) } else { '' }
$cpmIssues = [System.Collections.Generic.List[string]]::new()
if ($packagesPropsText -notmatch '<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>') {
    $cpmIssues.Add('Directory.Packages.props に ManagePackageVersionsCentrally=true が無い')
}
# 版の範囲（`[1.*,)`）は使わない（同じタグでも復元結果が変わる）
foreach ($m in [regex]::Matches($packagesPropsText, '<PackageVersion Include="(?<id>[^"]+)" Version="(?<v>[^"]+)"')) {
    if ($m.Groups['v'].Value -match '^[\[*]|\*') { $cpmIssues.Add(("{0}: 版が範囲指定です（固定する）: {1}" -f $m.Groups['id'].Value, $m.Groups['v'].Value)) }
}
foreach ($csproj in (Get-ChildItem -Path (Join-Path $repoRoot 'src') -Recurse -File -Filter '*.csproj' |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' })) {
    $text = [IO.File]::ReadAllText($csproj.FullName, $enc)
    foreach ($m in [regex]::Matches($text, '<PackageReference\s+[^>]*?Include="(?<id>[^"]+)"\s+Version=')) {
        $cpmIssues.Add(("{0}: {1} に Version が書かれている（Directory.Packages.props へ移す）" -f $csproj.FullName.Substring($repoRoot.Length + 1), $m.Groups['id'].Value))
    }
}
Add-Result 'パッケージの版' ($cpmIssues.Count -eq 0) "違反 $($cpmIssues.Count) 件"
$cpmIssues | Select-Object -First 10 | ForEach-Object { "      $_" }

# 2a-2) 復元の固定（packages.lock.json）
#     依存の版を lock で固定する（同じ版で別の中身が入る事故を防ぐ）。 規則は .github/ビルド設定規約.md §4。
$lockMissing = @(Get-ChildItem -Path (Join-Path $repoRoot 'src') -Recurse -File -Filter '*.csproj' |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
        Where-Object { -not (Test-Path -LiteralPath (Join-Path $_.DirectoryName 'packages.lock.json')) })
Add-Result 'ロックファイル' ($lockMissing.Count -eq 0) "未作成 $($lockMissing.Count) 件"
$lockMissing | Select-Object -First 10 | ForEach-Object { "      packages.lock.json が無い: $($_.FullName.Substring($repoRoot.Length + 1))（dotnet restore で作られる）" }

# 2b) アナライザー設定が .cs へ適用されているか
#     `.editorconfig` の `dotnet_diagnostic.*` は「直前のセクション」に属する。 セクションを分け忘れると
#     設定が `.cs` 以外（例: `[*.csproj]`）に属し、**設定が無効のまま「0 警告」になる**（実測で発生した）。
$editorConfigPath = Join-Path $repoRoot '.editorconfig'
$analyzerSectionIssues = [System.Collections.Generic.List[string]]::new()
if (Test-Path -LiteralPath $editorConfigPath) {
    $section = ''
    $line = 0
    foreach ($text in [IO.File]::ReadAllLines($editorConfigPath, $enc)) {
        $line++
        if ($text -match '^\s*\[(?<pattern>.+)\]\s*$') { $section = $Matches['pattern']; continue }
        if ($text -match '^\s*dotnet_diagnostic\.[A-Za-z0-9]+\.[a-z_]+\s*=') {
            # 対象に .cs を含むセクションか。 次のいずれかなら `.cs` へ適用される:
            #   `*.cs`（ファイル パターン） / `src/X/src/**`（ディレクトリ配下） / `*` や `**`（全ファイル）
            # `*.csproj` や `*.cs-broken` のような取り違えは、いずれにも当たらず検出される（実測で確認）。
            $sectionOk = $true
            foreach ($part in $section.Replace('{', '').Replace('}', '').Split(',')) {
                $p = $part.Trim()
                if ($p -eq '*' -or $p -eq '**') { continue }
                if ($p.EndsWith('.cs') -or $p.EndsWith('/**') -or $p.EndsWith('**')) { continue }
                $sectionOk = $false
            }
            if (-not $sectionOk) {
                $analyzerSectionIssues.Add(("{0}:{1}  {2}（セクション: [{3}]）" -f '.editorconfig', $line, $text.Trim(), $section))
            }
        }
    }
}
Add-Result 'アナライザー設定' ($analyzerSectionIssues.Count -eq 0) "無効なセクション $($analyzerSectionIssues.Count) 件"
$analyzerSectionIssues | Select-Object -First 8 | ForEach-Object { "      $_" }

# 2c) ログの有効判定を定義側で行っているか（呼び出し側で IsEnabled を書かない）
#     判定はログ定義（<対象>LogMessage / AppLog）が単一所有する。 呼び出し側に書くと、レベルを変えるたびに
#     全呼び出し元を直すことになり、雑音にもなる（実測: 7 箇所のゲートを定義側へ移した）。
#     --untracked: 新規ファイル（未コミット）も対象にする（無視対象の obj / bin は走査されない）。
$callSiteGates = [System.Collections.Generic.List[string]]::new()
foreach ($hit in @(git -c core.quotepath=false grep --untracked -n -E '\bIsEnabled\s*\(\s*LogLevel\.' -- 'src' 2>$null)) {
    if ($hit -match '\\tests\\' -or $hit -match '/(tests)/') { continue }
    # ログ定義（*LogMessage.cs / AppLog.cs）と、記録用ロガーの実装は対象外
    if ($hit -match '(LogMessage|AppLog)\.cs:') { continue }
    $callSiteGates.Add($hit)
}
Add-Result 'ログの有効判定' ($callSiteGates.Count -eq 0) "呼び出し側の IsEnabled $($callSiteGates.Count) 件"
$callSiteGates | Select-Object -First 8 | ForEach-Object { "      $_" }

# 2d) Dependabot の対象（.csproj を持つフォルダが全て列挙されているか）
#     列挙漏れがあると、後から足したプロジェクトの依存だけが更新されない（気付かないまま古くなる）。
#     規則の所有者は .github/検証手順.md §9。 対象は**リポジトリ内の全プロジェクト**（テスト・サンプルを含む。
#     どれも依存を持つため）。
$dependabotPath = Join-Path $repoRoot '.github/dependabot.yml'
$dependabotMissing = [System.Collections.Generic.List[string]]::new()
if (-not (Test-Path -LiteralPath $dependabotPath)) {
    Add-Result 'Dependabot の対象' $false '.github/dependabot.yml が無い'
}
else {
    $dependabotText = [IO.File]::ReadAllText($dependabotPath, $enc)
    $listedDirectories = @([regex]::Matches($dependabotText, '(?m)^\s*-\s*"(?<path>/[^"]*)"\s*$') |
            ForEach-Object { $_.Groups['path'].Value.TrimEnd('/') })
    # **追跡対象と未追跡（無視されない）の `.csproj` だけを見る**: 無視されたフォルダー（`.idea/` 等）の
    # 生成物を対象にすると、リポジトリの外の事情で不合格になる（実測 2026-10-28）。
    $projectDirectories = @(git ls-files --cached --others --exclude-standard -- '*.csproj' |
            ForEach-Object { '/' + ([IO.Path]::GetDirectoryName($_)).Replace('\', '/') } |
            Sort-Object -Unique)
    foreach ($directory in $projectDirectories) {
        if ($listedDirectories -notcontains $directory) { $dependabotMissing.Add($directory) }
    }
    Add-Result 'Dependabot の対象' ($dependabotMissing.Count -eq 0) "未登録 $($dependabotMissing.Count) 件"
    $dependabotMissing | Select-Object -First 8 | ForEach-Object { "      .github/dependabot.yml の directories に無い: $_" }
}

# 2e) 配布物の名前（`<製品名>.<UI名>.exe` / `<製品名>.<UI名>.zip`）
#     exe と zip の名前が UI ごとに揃っていないと、利用者がどれを展開すべきか分からなくなる
#     （実測 2026-10-28: 1 つの UI だけ `<製品名>.zip` で、他は `<製品名>.<UI名>.zip` だった）。
#     規則の所有者は .github/命名規約.md「アセンブリ名」。
$distIssues = [System.Collections.Generic.List[string]]::new()
$releaseConfig = Get-RepoConfig $repoRoot
$product = $releaseConfig.product
foreach ($ui in @($releaseConfig.uis)) {
    $expectedExe = "$product.$($ui.name).exe"
    if ($ui.exe -ne $expectedExe) { $distIssues.Add(("{0}: exe が {1} です（{2} にする）" -f $ui.name, $ui.exe, $expectedExe)) }
    if ($ui.zip) {
        $expectedAsset = "$product.$($ui.name).zip"
        if ($ui.asset -ne $expectedAsset) { $distIssues.Add(("{0}: asset が {1} です（{2} にする）" -f $ui.name, $ui.asset, $expectedAsset)) }
    }
}
Add-Result '配布物の名前' ($distIssues.Count -eq 0) "不一致 $($distIssues.Count) 件"
$distIssues | ForEach-Object { "      $_" }
# 2f) 実行ファイルのアーキテクチャ
#     方針は .github/release-config.json の `executableArchitecture` が単一所有する（リポジトリごとに違うため、
#     本スクリプトへ書かない）。 規則は .github/ビルド設定規約.md §4。
#       x64    … `PlatformTarget` = x64 か**単数形**の `RuntimeIdentifier` = win-x64 を要求する
#                （AnyCPU の exe は apphost をビルドしたマシンの RID で作られるため、32bit 環境では 32bit になる。
#                 複数形の `RuntimeIdentifiers` は復元対象の RID 一覧であり、ビルド対象を固定しない）。
#       anycpu … アーキテクチャを固定しない（`PlatformTarget` / `Prefer32Bit` / `RuntimeIdentifier` を書かない。
#                 RID が要る処理は publish の `-r` で渡す）。
#     判定は**コメントを除いた XML** で行う（コメントアウトした指定を「指定あり」と誤認しないため）。
$archMode = $releaseConfig.executableArchitecture
if (-not $archMode) { $archMode = 'anycpu' }
$archIssues = [System.Collections.Generic.List[string]]::new()
$archChecked = 0
foreach ($rel in @(git ls-files --cached --others --exclude-standard -- 'src/**/App.*.csproj')) {
    $text = [IO.File]::ReadAllText((Join-Path $repoRoot $rel), $enc)
    # ライブラリとテストは対象外（アーキテクチャを固定しない）
    if ($rel -match '(\\|/)tests(\\|/)|\.Tests\.csproj$') { continue }
    if ($text -notmatch '<OutputType>(WinExe|Exe)</OutputType>') { continue }
    $archChecked += 1
    $xml = [regex]::Replace($text, '(?s)<!--.*?-->', '')
    if ($archMode -eq 'x64') {
        if ($xml -match '<Prefer32Bit>\s*true\s*</Prefer32Bit>' -or $xml -match '<PlatformTarget>\s*x86\s*</PlatformTarget>') {
            $archIssues.Add(("{0}: 32bit の指定がある" -f $rel))
        }
        elseif ($xml -match '<PlatformTarget>\s*x64\s*</PlatformTarget>') { continue }
        elseif ($xml -match '<RuntimeIdentifier(\s[^>]*)?>\s*win-x64\s*</RuntimeIdentifier>') { continue }
        elseif ($xml -match '<RuntimeIdentifiers(\s[^>]*)?>') {
            $archIssues.Add(("{0}: RuntimeIdentifiers（複数形）はビルド対象を固定しない（単数形の RuntimeIdentifier か PlatformTarget を書く）" -f $rel))
        }
        else {
            $archIssues.Add(("{0}: x64 の指定が無い（PlatformTarget か RuntimeIdentifier を書く）" -f $rel))
        }
    }
    else {
        if ($xml -match '<Prefer32Bit>\s*true\s*</Prefer32Bit>' -or $xml -match '<PlatformTarget(\s[^>]*)?>') {
            $archIssues.Add(("{0}: アーキテクチャを固定している（TargetFramework だけで AnyCPU にする）" -f $rel))
        }
        elseif ($xml -match '<RuntimeIdentifiers?(\s[^>]*)?>') {
            $archIssues.Add(("{0}: RuntimeIdentifier を指定している（-r は publish のコマンド ラインで渡す）" -f $rel))
        }
    }
}
Add-Result '実行ファイルのアーキテクチャ' ($archIssues.Count -eq 0) "方針 $archMode / 対象 $archChecked 件 / 違反 $($archIssues.Count) 件"
$archIssues | ForEach-Object { "      $_" }

# ============================================================
# 3. テスト（全件合格。 -Coverage でカバレッジも集計する）
# ============================================================
if (-not $SkipTests) {
    Write-Output '== 3. テスト =='
    # テストの実行基盤は global.json の test.runner が単一所有する。
    # Microsoft.Testing.Platform のときは対象を `--solution` で渡す（位置引数を渡すとテスト アプリへの
    # 引数として扱われ、0 件・終了コード 5 になる。 実測 2026-10-26）。 VSTest は位置引数で受ける。
    $useMtp = $globalJsonText -match '"runner"\s*:\s*"Microsoft\.Testing\.Platform"'
    $testArgs = @('test')
    $testArgs += if ($useMtp) { @('--solution', $Solution) } else { @($Solution) }
    $testArgs += @('-c', $Configuration, '--no-build')
    # `-tl:off` は VSTest の用語（Microsoft.Testing.Platform は受け付けず終了コード 5 になる。 実測 2026-10-26）
    if (-not $useMtp) { $testArgs += '-tl:off' }
    $coverageDir = Join-Path ([IO.Path]::GetTempPath()) ("se_cov_" + [Guid]::NewGuid().ToString('N'))
    if ($Coverage) { $testArgs += @('--collect:XPlat Code Coverage', "--results-directory:$coverageDir") }
    $test = Invoke-DotNet $testArgs
    $testText = $test.Text
    # 件数の見出しはランナーの言語で変わる（日本語の "合格:" と英語の "Passed:"）。
    # **どちらでも数える**（片方だけだと、英語のランナーで「0 件＝不合格」と誤判定する。 実測 2026-10-26）。
    $passed = 0
    # 実行基盤で見出しが変わる（VSTest は「合格 / Passed」、Microsoft.Testing.Platform は「成功 / Succeeded」）。
    # 見出しは小文字にもなる（Microsoft.Testing.Platform の英語出力は `succeeded:`）。 大文字小文字を無視する。
    foreach ($m in [regex]::Matches($testText, '(?:合格|Passed|成功|Succeeded):\s*(\d+)', 'IgnoreCase')) { $passed += [int]$m.Groups[1].Value }
    $failed = 0
    foreach ($m in [regex]::Matches($testText, '(?:失敗|Failed):\s*(\d+)', 'IgnoreCase')) { $failed += [int]$m.Groups[1].Value }
    $projects = [regex]::Matches($testText, '\.dll \(net10\.0[^)]*\)').Count
    Add-Result 'テスト' ($failed -eq 0 -and $passed -gt 0 -and $test.ExitCode -eq 0) "合格 $passed / 失敗 $failed / $projects プロジェクト / 終了コード $($test.ExitCode)"
    Add-SectionTiming 'テスト'
    if ($failed -gt 0) {
        # 失敗したテスト名を出す（合計だけでは原因を特定できない）
        $testText -split "`r?`n" | Where-Object { $_ -match '^\s*(失敗|Failed)\s+\S' } | Select-Object -First 10 | ForEach-Object { "      $($_.Trim())" }
    }

    if ($Coverage) {
        $files = @(Get-ChildItem -Path $coverageDir -Recurse -File -Filter 'coverage.cobertura.xml' -ErrorAction SilentlyContinue)
        if ($files.Count -eq 0) {
            Add-Result 'カバレッジ' $false 'レポートが生成されませんでした'
        }
        else {
            # 同じファイルが複数のテスト プロジェクトから出るため、行ごとに「被覆した実行があれば被覆」として集計する。
            # 生成コード（ソース ジェネレーターの出力・アセンブリ属性）は対象外にする
            $byFile = @{}
            foreach ($f in $files) {
                [xml]$doc = Get-Text $f.FullName
                foreach ($cls in $doc.coverage.packages.package.classes.class) {
                    $name = ([string]$cls.filename).Replace('\', '/')
                    if ($name -notmatch '/src/' -or $name -match '/obj/' -or $name -match '\.g\.cs$' -or $name -match '\.Designer\.cs$' -or $name -match 'AssemblyInfo\.cs$' -or $name -match 'AssemblyAttributes\.cs$') { continue }
                    if (-not $byFile.ContainsKey($name)) { $byFile[$name] = @{} }
                    foreach ($line in $cls.lines.line) {
                        $n = [string]$line.number
                        $hit = [int]$line.hits
                        if (-not $byFile[$name].ContainsKey($n) -or $byFile[$name][$n] -lt $hit) { $byFile[$name][$n] = $hit }
                    }
                }
            }
            $totalLines = 0
            $coveredLines = 0
            $uncovered = [System.Collections.Generic.List[string]]::new()
            foreach ($name in ($byFile.Keys | Sort-Object)) {
                $lines = $byFile[$name]
                $total = $lines.Count
                $covered = @($lines.Values | Where-Object { $_ -gt 0 }).Count
                $totalLines += $total
                $coveredLines += $covered
                if ($covered -lt $total) { $uncovered.Add(("{0}  {1}/{2}" -f $name, $covered, $total)) }
            }
            $rate = if ($totalLines -gt 0) { 100.0 * $coveredLines / $totalLines } else { 0 }
            # カバレッジは参考値（合否に含めない。 目標は テスト規約が定める）
            Add-Info 'カバレッジ' ("行被覆 {0:N2}%（{1}/{2}） 未達 {3} ファイル（生成コードを除く）" -f $rate, $coveredLines, $totalLines, $uncovered.Count)
            $uncovered | Select-Object -First 15 | ForEach-Object { "      $_" }
            if ($uncovered.Count -gt 15) { "      … 他 $($uncovered.Count - 15) ファイル" }
        }
        Remove-Item -Recurse -Force $coverageDir -ErrorAction SilentlyContinue
    }
}

# ============================================================
# 4. NuGet の健全性（既知の脆弱性。 推移的依存も含む）
# ============================================================
if (-not $SkipPackage) {
    Write-Output '== 4. NuGet の健全性 =='
    # まず lock どおりに復元できるかを確かめる（lock と csproj / Directory.Packages.props が食い違えば
    # NU1004 で失敗する）。 監査は --force-evaluate で lock を書き換え得るため、この検査を先に行う。
    # 構成は指定しない（CI の復元と同じ条件にする）。 `packages.lock.json` は 1 つしか持てないため、
    # 構成で解決が変わるプロジェクトがあると、構成を変えるだけで NU1004 になる（実測 2026-10-26）。
    $locked = Invoke-DotNet @('restore', $Solution, '-p:RestoreLockedMode=true')
    $lockedText = $locked.Text + $locked.ErrorText
    $lockedIssues = @($lockedText -split "`r?`n" | Where-Object { $_ -match 'NU1004|error' })
    Add-Result 'ロックどおりの復元' ($locked.ExitCode -eq 0) "終了コード $($locked.ExitCode)"
    if ($locked.ExitCode -ne 0) { $lockedIssues | Select-Object -First 5 | ForEach-Object { "      $($_.Trim())" } }

    # 脆弱性は復元の NuGet 監査で検出する（推移的依存も対象。 実測 2.7 秒。 同じ結果を
    # `dotnet package list --vulnerable --include-transitive` で取ると 35 秒かかる）。
    # `--force-evaluate` は「復元は最新」と判定されたときにも監査を走らせるために付ける
    # （付けないと監査が省略され、0 件と誤判定する）。 監査は検査そのものなので、-NoRestore は適用しない。
    # 直接参照の脆弱性はビルドの復元でも検出される（`NuGetAuditMode` の既定は direct であり、
    # 警告は Directory.Build.props の TreatWarningsAsErrors でエラーになる）。 本節は推移的依存まで広げる。
    $audit = Invoke-DotNet @('restore', $Solution, "-p:Configuration=$Configuration", '-p:NuGetAuditMode=all', '--force-evaluate')
    $auditText = $audit.Text
    $auditHits = @($auditText -split "`r?`n" | Where-Object { $_ -match 'NU19\d\d' })

    # 監査自体が失敗した場合（復元の失敗）に 0 件と誤判定しない（終了コードが 0 以外なら不合格にし、
    # NuGet のエラー（NU1xxx。 脆弱性の NU19xx を除く）の有無も見る）
    $auditFailed = $audit.ExitCode -ne 0 -or $auditText -match 'error NU1(?!9)\d\d'
    Add-Result 'NuGet の健全性' ($auditHits.Count -eq 0 -and -not $auditFailed) "脆弱性 $($auditHits.Count) 件（推移的依存を含む） / 終了コード $($audit.ExitCode)"
    $auditHits | Select-Object -First 10 | ForEach-Object { "      $($_.Trim())" }
    if ($auditFailed) {
        "      監査（復元）が失敗した"
        $auditText -split "`r?`n" | Where-Object { $_ -match 'error NU1' } | Select-Object -First 5 | ForEach-Object { "      $($_.Trim())" }
    }
}
Add-SectionTiming 'NuGet の健全性'


# ============================================================
# 5〜10. 文書とコードの走査（.github/verify/verifyDocs.ps1。 上の Start-DocsVerification で起動済み）
# ============================================================
if ($docsBackground) {
    Receive-DocsVerification $docsBackground
}
elseif ($SkipDocs) {
    # 別のジョブ（.github/verify/verifyDocs.ps1）が実行する。 合否に含めない（二重計上しないため）
    Add-Info '文書とコードの走査' '省略（-SkipDocs。 CI の docs ジョブが .github/verify/verifyDocs.ps1 を実行する）'
}
else {
    # -Fix のときは直列で走らせる（修正と判定の順序を保つ）
    Receive-DocsVerification (Start-DocsVerification)
}
Add-SectionTiming '走査（背景で実行。 ビルド・テストと重なる）'

# ============================================================
# 11. コードスタイル（`dotnet format`。 既定では省略し、ビルドと走査で代替する）
# ============================================================
# **既定で省略する理由**: 同じことを 20 秒かけて確かめていた。 実測で次のように分担できる
# （規則の実効的な強制は「ビルド」と「走査」が担い、`dotnet format` は自動修正の手段として残す）。
#   - IDE / CA の規則（`IDE0044` / `IDE0055` / `CA1805` 等）は**ビルドが報告する**
#     （`EnforceCodeStyleInBuild` + `TreatWarningsAsErrors`。 書式（`IDE0055`）もビルドが出すことを実測で確認した）。
#   - **命名規則（`IDE1006`）だけはビルドが報告しない**（実測: `dotnet format` が 4 種類の違反を出し、
#     ビルドは 0 件だった）。 これは走査（`verifyDocs.ps1` の「フィールドとインターフェースの命名」）が
#     `.editorconfig` の 4 規則をそのまま検査する（同じ 4 件を検出することを実測で確認した）。
# `-FullFormat` を付けると従来どおり `dotnet format` を実行する（規則を足したときの確認に使う）。
if ($SkipFormat) {
    Add-Info 'コードスタイル（dotnet format）' '省略（-SkipFormat）'
}
elseif (-not $Fix -and -not $FullFormat) {
    Add-Info 'コードスタイル（dotnet format）' '省略（ビルドと走査で代替する。 命名規則は走査が検査する。 -FullFormat で実行）'
}
else {
    Write-Output '== 11. コードスタイル（dotnet format） =='
    if ($Fix) {
        # 自動修正してから判定する（修正した内容で合否を出す）。 修正自体の失敗は後段の判定では
        # 見えない（判定は修正後の状態を見るため）ので、ここで終了コードを確かめる。
        # `--severity warn` は style / analyzers だけが受ける（whitespace に渡すと引数の誤りになる）。
        $fixSteps = @(
            @('whitespace'),
            @('style', '--severity', 'warn'),
            @('analyzers', '--severity', 'warn')
        )
        $fixFailures = [System.Collections.Generic.List[string]]::new()
        foreach ($step in $fixSteps) {
            $fix = Invoke-DotNet (@('format') + $step + @($Solution, '--no-restore'))
            if ($fix.ExitCode -ne 0) { $fixFailures.Add(("{0}（終了コード {1}）" -f $step[0], $fix.ExitCode)) }
        }
        Add-Result 'コードスタイルの修正' ($fixFailures.Count -eq 0) $(if ($fixFailures.Count -eq 0) { '3 段（whitespace / style / analyzers）' } else { $fixFailures -join ' / ' })
        # 修正した内容で判定するため、読み込みのキャッシュを捨てる
        Clear-FileCache
    }

    $fmt = Invoke-DotNet @('format', 'style', $Solution, '--verify-no-changes', '--severity', 'warn', '--no-restore')
    $fmtText = $fmt.Text
    $fmtExit = $fmt.ExitCode
    $fmtIssues = ([regex]::Matches($fmtText, ': (error|warning) (IDE|CA)\d+')).Count
    # 合否は「警告以上」の件数で判定する（引数の `--severity warn` と同じ基準。 情報レベルの提案は
    # ビルドが強制する範囲ではなく、改善の必要がない状態でも返るため合否に含めない）。
    # 実行自体の失敗（引数の誤り・復元の失敗等）は 2 以外の終了コードになるため、それだけを不合格にする。
    $fmtRunFailed = $fmtExit -gt 2
    Add-Result 'コードスタイル（dotnet format）' ($fmtIssues -eq 0 -and -not $fmtRunFailed) "警告以上 $fmtIssues 件"
    if ($fmtIssues -gt 0) { $fmtText -split "`r?`n" | Where-Object { $_ -match ': (error|warning) (IDE|CA)\d+' } | Select-Object -First 10 | ForEach-Object { "      $($_.Trim())" } }
    if ($fmtRunFailed) {
        "      実行に失敗した（終了コード $fmtExit）"
        $fmtText -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 } | Select-Object -First 5 | ForEach-Object { "      $($_.Trim())" }
    }
}

# ============================================================
# 要約
# ============================================================
$failed = @($script:Results | Where-Object { $_.Kind -eq 'Check' -and -not $_.Ok })
Write-Output ''
Write-Output '===== 検証の要約 ====='
foreach ($r in $script:Results) {
    $mark = if ($r.Kind -eq 'Info') { 'INFO' } elseif ($r.Ok) { 'OK  ' } else { 'FAIL' }
    Write-Output ("{0}  {1}`t{2}" -f $mark, $r.Name, $r.Detail)
}
# どこに時間を使ったかを残す（走査はビルド・テストと重なるため、内訳の合計は実時間より長くなる）
Add-SectionTiming 'その他'
Write-Output ''
Write-Output ("INFO  所要時間`t実時間 {0:N1} 秒 / 内訳: {1}" -f $scriptClock.Elapsed.TotalSeconds, ($sectionTimings -join ' / '))
Write-Output ''
if ($failed.Count -eq 0) {
    $checks = @($script:Results | Where-Object { $_.Kind -eq 'Check' }).Count
    Write-Output "すべて合格（合否 $checks 項目）"
    exit 0
}
$names = ($failed | ForEach-Object { $_.Name }) -join ', '
Write-Output "不合格 $($failed.Count) 項目: $names"
exit 1
