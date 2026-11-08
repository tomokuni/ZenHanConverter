# SBOM（CycloneDX）を作る。
#
# 目的: 配布物（zip）からは「どの依存のどの版を使ったか」が分からないため、
# リリースへ SBOM を添付して追跡できるようにする（規則の所有者は .github/RELEASE.md）。
#
# ツールの版は .config/dotnet-tools.json が単一所有する（`dotnet tool restore` で揃う。 CI とローカルで同じものを使う）。
# 出力は `-Output` のフォルダへ `<Product>.<Version>.sbom.cdx.json` として置く（ファイル名で対象が分かる）。
#
# 使い方:
#   pwsh -NoProfile -File .github/scripts/generateSbom.ps1
#   pwsh -NoProfile -File .github/scripts/generateSbom.ps1 -Output sbom -Version 0.2.0
#   # 複数のソリューションを並列に作るとき（復元は呼び出し側で 1 回だけ行う）
#   pwsh -NoProfile -File .github/scripts/generateSbom.ps1 -Solution <solution>.slnx -SkipToolRestore
#   # 呼び出し側が既に復元しているとき（pack の直後。 解析だけを行う）
#   pwsh -NoProfile -File .github/scripts/generateSbom.ps1 -SkipToolRestore -SkipPackageRestore
#
# ソリューション ファイルと製品名の既定は .github/release-config.json が単一所有する
# （スクリプトの本文へリポジトリ固有の名前を書かない）。

[CmdletBinding()]
param(
    [string]$Solution = '',
    [string]$Output = 'artifacts/sbom',
    [string]$Version = '',
    [string]$Product = '',
    # 呼び出し側が `dotnet tool restore` を済ませている場合に、復元を省く（複数の SBOM を並列に作るとき）。
    [switch]$SkipToolRestore,
    # 呼び出し側が対象の復元を済ませている場合に、ツール自身の復元を省く（`--disable-package-restore`）。
    # **費用の大半は復元**である（実測 2026-11-06: 復元込み 11.8 秒 → 復元なし 1.0 秒）。
    [switch]$SkipPackageRestore
)

$ErrorActionPreference = 'Stop'
$enc = [Text.UTF8Encoding]::new($false)
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repoRoot

. (Join-Path $PSScriptRoot '..\verify\VerifyCommon.ps1')

# ソリューション ファイルと製品名は release-config.json が単一所有する
$config = Get-RepoConfig $repoRoot
if (-not $Solution) { $Solution = @(Get-SolutionList $config)[0] }
if (-not $Product) { $Product = $config.product }

if (-not $Version) {
    # 版は Directory.Build.props が単一所有する（VersionPrefix。 無ければ Version。 引数は CI からの上書き用）
    $props = [IO.File]::ReadAllText((Join-Path $repoRoot 'Directory.Build.props'), $enc)
    $m = [regex]::Match($props, '<VersionPrefix>(?<v>[^<]+)</VersionPrefix>')
    if (-not $m.Success) { $m = [regex]::Match($props, '<Version>(?<v>[^<]+)</Version>') }
    if (-not $m.Success) { throw 'Directory.Build.props に VersionPrefix / Version がありません' }
    $Version = $m.Groups['v'].Value.Trim()
}

if (-not (Test-Path -LiteralPath $Solution)) { throw "ソリューションがありません: $Solution" }
[void](New-Item -ItemType Directory -Path $Output -Force)

# ツールはリポジトリのマニフェスト（.config/dotnet-tools.json）から復元する。
# 並列に呼ぶ場合（CI）は呼び出し側が 1 回だけ復元し、-SkipToolRestore を付ける
# （同じツール フォルダへ同時に復元すると、ファイルの取り合いで失敗しうる）。
if (-not $SkipToolRestore) {
    $restore = Invoke-NativeText 'dotnet' @('tool', 'restore')
    if ($restore.ExitCode -ne 0) {
        Write-Output ($restore.Text + $restore.ErrorText)
        throw "dotnet tool restore に失敗しました（終了コード $($restore.ExitCode)）"
    }
}

$name = "$Product.$Version.sbom.cdx.json"
$work = Join-Path ([IO.Path]::GetTempPath()) ("sbom-" + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $work)

try {
    # ツールは出力ファイル名を固定で受けるため、一時フォルダへ作ってから目的の名前へ移す
    $arguments = @('cyclonedx', $Solution, '-o', $work, '-fn', $name)
    if ($SkipPackageRestore) { $arguments += '--disable-package-restore' }
    $run = Invoke-NativeText 'dotnet' $arguments
    $text = $run.Text + $run.ErrorText
    if ($run.ExitCode -ne 0) {
        Write-Output $text
        throw "SBOM の作成に失敗しました（終了コード $($run.ExitCode)）"
    }

    $source = Join-Path $work $name
    if (-not (Test-Path -LiteralPath $source)) { throw "SBOM が作られませんでした: $source" }

    # 中身が空（components が無い）なら失敗にする（配布物に意味の無い SBOM を添付しない）
    $json = Get-Content -LiteralPath $source -Raw -Encoding UTF8 | ConvertFrom-Json
    $count = @($json.components).Count
    if ($count -eq 0) { throw 'SBOM の components が 0 件です（復元が済んでいない可能性があります）' }

    $destination = Join-Path $Output $name
    Move-Item -LiteralPath $source -Destination $destination -Force
    Write-Output "SBOM を作成しました: $destination（components $count 件 / specVersion $($json.specVersion)）"
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
