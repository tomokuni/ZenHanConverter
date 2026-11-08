<#
.SYNOPSIS
    対象のソリューションの `packages.lock.json` を、現在の `Directory.Packages.props` に合わせて作り直す。

.DESCRIPTION
    `packages.lock.json` は復元結果を固定する（規則は `.github/ビルド設定規約.md` の「パッケージの版と復元」）。

    **版を変えたときの更新は CI が行う**（`.github/workflows/locks.yml` が main 向け Pull Request で
    このスクリプトを実行し、差分があれば自動コミットする）。 このスクリプトは、その CI と、
    手元で先に直したいとき（fork の Pull Request など）が使う。

    更新しないと CI の `-p:RestoreLockedMode=true` が `NU1004` で失敗する。

    ソリューションの一覧は `.github/release-config.json` が単一所有する（`solutionFile` は 1 本、
    `solutions` は複数。 本文へ列挙しない）。

.EXAMPLE
    pwsh -NoProfile -File .github/scripts/updateLocks.ps1
#>
[CmdletBinding()]
param(
    # 既定は release-config.json の solutions（無ければ solutionFile）
    [string[]]$Solution = @()
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repoRoot

. (Join-Path $PSScriptRoot '..\verify\VerifyCommon.ps1')

if (-not $Solution -or $Solution.Count -eq 0) { $Solution = @(Get-SolutionList (Get-RepoConfig $repoRoot)) }
if ($Solution.Count -eq 0) { throw '.github/release-config.json にソリューション（solutionFile / solutions）がありません' }

foreach ($sln in $Solution) {
    Write-Host "lock を更新しています: $sln"
    # --force-evaluate: 復元結果が最新でも評価し直す（lock の要求範囲だけが変わった場合に必要）
    & dotnet restore $sln --force-evaluate
    if ($LASTEXITCODE -ne 0) { throw "lock の更新に失敗しました（$sln、終了コード $LASTEXITCODE）" }
}

# lock と Directory.Packages.props の食い違いを、CI と同じ条件（ロック済みモード）で確かめる
foreach ($sln in $Solution) {
    & dotnet restore $sln -p:RestoreLockedMode=true
    if ($LASTEXITCODE -ne 0) { throw "ロック済みモードの復元に失敗しました（$sln、終了コード $LASTEXITCODE）" }
}

$changed = @(git status --porcelain -- '*/packages.lock.json' 'packages.lock.json')
Write-Host "lock を更新しました（変更 $($changed.Count) 件）。"
$changed | ForEach-Object { "  $_" }