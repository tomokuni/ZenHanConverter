<#
.SYNOPSIS
情報レベルのコード スタイル（`verify.ps1` の合否に含めない改善提案）を検出し、`-Fix` で自動修正する。

.DESCRIPTION
`verify.ps1`（およびビルド）の合否は「**警告以上 0 件**」であり、規則は `.editorconfig` が単一所有する。
本スクリプトはその**下の情報レベル（提案）**だけを対象にし、**採用するかどうかを判断するための一覧**を出す
（合否ゲートではない。 `.github/検証手順.md` §4「情報レベルの提案」が定める）。

- 既定は**検出のみ**（`--verify-no-changes`）。 ファイルは変更しない。
- **`-Fix` を付けると自動修正する**（`dotnet format` の修正を適用する）。
  - **修正後に必ずビルドと `verify.ps1` を実行する**（自動修正が新しい違反を生むことがある。 実測:
    情報レベルの修正（`IDE0019` のパターン マッチへの書き換え）が未使用の変数（`IDE0059`）を生み、
    ビルドがエラーになった）。
  - アナライザーが直せない項目（`CA1305` 等）は手で直す。
- 対象は種類（`-Kind`）で分ける。 既定は `Style`（`IDExxxx`。 `dotnet format style`）。
  `Analyzer` は `CAxxxx` / `MAxxxx`（`dotnet format analyzers`）で、**採用していない規則の提案が大量に出る**
  （実測: 881 件）ため、規則の採用を判断するときに使う。

.PARAMETER Solution
対象のソリューション ファイル（既定: release-config.json の solutionFile）。

.PARAMETER Fix
自動修正する（既定は検出のみ）。

.PARAMETER Kind
対象の種類（`Style` = `IDExxxx` / `Analyzer` = `CAxxxx`・`MAxxxx` / `All`。 既定は `Style`）。

.PARAMETER MaxDetail
規則ごとに表示する例の最大件数（既定 5。 `0` で例を表示しない）。

.PARAMETER NoRestore
`--no-restore` を付ける（呼び出し側が復元済みのとき）。

.EXAMPLE
pwsh -NoProfile -File buildScript/verifyInfoStyle.ps1
.EXAMPLE
pwsh -NoProfile -File buildScript/verifyInfoStyle.ps1 -Fix
.EXAMPLE
pwsh -NoProfile -File buildScript/verifyInfoStyle.ps1 -Kind Analyzer -MaxDetail 0
#>
[CmdletBinding()]
param(
    [string]$Solution = '',
    [switch]$Fix,
    [ValidateSet('Style', 'Analyzer', 'All')][string]$Kind = 'Style',
    [int]$MaxDetail = 5,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$enc = [Text.UTF8Encoding]::new($false)
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repoRoot

# `dotnet format` の出力もネイティブ コマンドの出力である（復号は Invoke-NativeText が単一所有する。
# 文字化けの根拠は .github/検証手順.md §19）。
. (Join-Path $PSScriptRoot 'VerifyCommon.ps1')

# 対象のソリューション ファイルは release-config.json が単一所有する
if (-not $Solution) { $Solution = (Get-RepoConfig $repoRoot).solutionFile }

$restoreArgs = if ($NoRestore) { @('--no-restore') } else { @() }
# 診断行（`<ファイル>(<行>,<列>): <レベル> <規則>: <メッセージ> [<プロジェクト>]`）を分解する
$rxDiagnostic = [regex]'(?m)^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\): (?<level>error|warning|info) (?<rule>[A-Z]+\d+): (?<message>.+?) \[(?<project>.+)\]\s*$'

$kinds = switch ($Kind) {
    'Style' { @('style') }
    'Analyzer' { @('analyzers') }
    'All' { @('style', 'analyzers') }
}

$total = 0
foreach ($target in $kinds) {
    Write-Output "== $target（情報レベル） =="
    $args = @('format', $target, $Solution, '--no-restore') + $restoreArgs
    if (-not $Fix) { $args += @('--verify-no-changes', '--severity', 'info') }
    # 出力の復号は Invoke-NativeText が行う（`& dotnet` は環境によっては日本語を文字化けさせる）
    $formatRun = Invoke-NativeText 'dotnet' $args
    $text = $formatRun.Text + $formatRun.ErrorText
    $exit = $formatRun.ExitCode

    $diagnostics = @()
    foreach ($m in $rxDiagnostic.Matches($text)) {
        $diagnostics += [pscustomobject]@{
            File    = $m.Groups['file'].Value.Replace($repoRoot + '\', '').Replace('\', '/')
            Line    = [int]$m.Groups['line'].Value
            Level   = $m.Groups['level'].Value
            Rule    = $m.Groups['rule'].Value
            Message = $m.Groups['message'].Value
        }
    }

    if ($Fix) {
        Write-Output ("      修正を適用した（終了コード {0}）" -f $exit)
        Write-Output '      次: dotnet build と buildScript/verify.ps1 で 0 警告・全件合格を確かめる'
        continue
    }

    $total += $diagnostics.Count
    # 規則ごとにまとめる（採用するかどうかを規則単位で判断するため）
    foreach ($group in ($diagnostics | Group-Object Rule | Sort-Object Count -Descending)) {
        $level = ($group.Group | Select-Object -First 1).Level
        Write-Output ("      {0,-12} {1,-6} {2,4} 件  {3}" -f $group.Name, $level, $group.Count, ($group.Group | Select-Object -First 1).Message)
        if ($MaxDetail -gt 0) {
            $group.Group | Select-Object -First $MaxDetail | ForEach-Object { "          {0}:{1}" -f $_.File, $_.Line }
        }
    }
    Write-Output ("      合計 {0} 件" -f $diagnostics.Count)
}

if ($Fix) { exit 0 }

Write-Output ''
Write-Output '===== 情報レベルの要約 ====='
Write-Output ("情報レベルの改善提案 {0} 件（合否ゲートではない。 採用の判断は .github/検証手順.md §4「情報レベルの提案」）" -f $total)
if ($total -eq 0) { exit 0 }
# 提案が残っている場合は終了コード 1（呼び出し側が「検出あり」を判定できるようにする）
exit 1
