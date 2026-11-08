<#
.SYNOPSIS
    ソリューションの `packages.lock.json` を、現在の `Directory.Packages.props` に合わせて作り直す。

.DESCRIPTION
    `packages.lock.json` は復元結果を固定する（規則は `.github/ビルド設定規約.md` §4）。

    **版を変えたときの更新は CI が行う**（`.github/workflows/locks.yml` が main 向け Pull Request で
    このスクリプトを実行し、差分があれば自動コミットする）。 このスクリプトは、その CI と、
    手元で先に直したいとき（fork の Pull Request など）が使う。

    更新しないと CI の `-p:RestoreLockedMode=true` が `NU1004` で失敗する。

    対象のソリューションは `.github/release-config.json` の `solutionFile` が単一所有する（本文へ列挙しない）。

.EXAMPLE
    pwsh -NoProfile -File buildScript/updateLocks.ps1
#>
[CmdletBinding()]
param(
    # 既定は release-config.json の solutionFile
    [string]$Solution = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repoRoot

. (Join-Path $PSScriptRoot 'VerifyCommon.ps1')

if (-not $Solution) { $Solution = (Get-RepoConfig $repoRoot).solutionFile }
if (-not $Solution) { throw '.github/release-config.json の solutionFile が空です' }

Write-Host "lock を更新しています: $Solution"
# --force-evaluate: 復元結果が最新でも評価し直す（lock の要求範囲だけが変わった場合に必要）
& dotnet restore $Solution --force-evaluate
if ($LASTEXITCODE -ne 0) { throw "lock の更新に失敗しました（$Solution、終了コード $LASTEXITCODE）" }

# lock と Directory.Packages.props の食い違いを、CI と同じ条件（ロック済みモード）で確かめる
& dotnet restore $Solution -p:RestoreLockedMode=true
if ($LASTEXITCODE -ne 0) { throw "ロック済みモードの復元に失敗しました（$Solution、終了コード $LASTEXITCODE）" }

$changed = @(git status --porcelain -- '*/packages.lock.json' 'packages.lock.json')
Write-Host "lock を更新しました（変更 $($changed.Count) 件）。"
$changed | ForEach-Object { "  $_" }