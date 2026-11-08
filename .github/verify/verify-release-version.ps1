# 進捗の表示は Write-Host を使う（標準出力は JSON として ConvertFrom-Json に渡すため、
# Write-Output を混ぜると JSON の解析が壊れる）。
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification = '標準出力を JSON として ConvertFrom-Json に渡すため、人の目に出す進捗は Write-Host を使う')]
<#
.SYNOPSIS
リリースしようとしているバージョンの妥当性を検証し、リリース情報を返す。
.DESCRIPTION
次の順で検証する。1 つでも満たさない場合は例外で失敗する。
  1. バージョンの形式（semver。規則は version.ps1 に委譲する）
  2. 実行ブランチが許可されていること
     - 許可するブランチは release-config.json の releaseBranches（例: ["main", "release/**"]）
     - release/<major>.<minor> ブランチでは、入力バージョンの系列がブランチ名と一致すること
  3. 単調性（**同じ系列**のタグの最大より大きいこと）
     - 系列内でのみ比較するため、旧系列へのリリース（バックポート）も指定できる
     - **dry-run では違反を警告にして続行する**（版を上げていないだけの状態でも、
       それ以外の条件が通ることを確認できるようにするため。 公開では失敗させる）
  4. タグ v<version> が未作成であること（同値のリリースを防ぐ）
     - 3 と同じく **dry-run では警告にして続行する**

現在のバージョン状況の確認は show-current-versions.ps1 が行う（本スクリプトは検証のみ）。
既存タグの比較はバージョンの規則を単一所有する version.ps1 に委譲する
（git tag --sort は semver の優先順位に従わないため、最大値の算出はスクリプト側で行う）。
.PARAMETER Version
リリースするバージョン（例: 1.2.3 / 1.2.3-rc.1）。
.PARAMETER Branch
実行ブランチ名（例: main / release/1.2）。
.PARAMETER RepoRoot
リポジトリのルート。既定は本スクリプトの 2 つ上の階層。
.OUTPUTS
System.String
リリース情報を表す JSON（version, tag, series, prerelease, isBackport, updateVersionFile, markLatest, notesStartTag）。
.EXAMPLE
$info = & ./.github/verify/verify-release-version.ps1 -Version 1.2.0 -Branch main | ConvertFrom-Json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$Branch,
    # dry-run は dev でも実行できる（公開は main と系列ブランチだけ）。 既定は公開（安全側）。
    [ValidateSet('dry-run', 'release')]
    [string]$Mode = 'release',

    [string]$RepoRoot
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\scripts\version.ps1')

if (-not $RepoRoot) {
    # 本スクリプトは <リポジトリルート>/.github/scripts に置かれている
    $RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
}

# 形式の検証、タグ名と系列の算出はバージョンの規則（version.ps1）に従う
$null = ConvertTo-SemanticVersion -Version $Version
$tag = Get-TagName -Version $Version
$series = Get-VersionSeries -Version $Version

# ─── 許可ブランチの読み込み（release-config.json。未指定は main のみ）───
# dry-run（配布物を作れることの確認）は dev でも実行できる。 **公開（release）は main と系列ブランチだけ**。
# これにより、開発から dry-run までの確認を dev で終えられる（公開は main でのみ）。
$configPath = Join-Path $RepoRoot '.github/release-config.json'

if (-not (Test-Path -LiteralPath $configPath)) {
    throw "設定ファイルが見つかりません: $configPath"
}

$config = Get-Content $configPath -Raw | ConvertFrom-Json
$releaseBranches = @('main')
$dryRunBranches = @('main')

if ($config.PSObject.Properties['releaseBranches'] -and $config.releaseBranches) {
    $releaseBranches = @($config.releaseBranches)
}
if ($config.PSObject.Properties['dryRunBranches'] -and $config.dryRunBranches) {
    $dryRunBranches = @($config.dryRunBranches)
}
else {
    $dryRunBranches = $releaseBranches
}

$isDryRun = $Mode -eq 'dry-run'
$allowedBranches = if ($isDryRun) { $dryRunBranches } else { $releaseBranches }
$modeLabel = if ($isDryRun) { 'dry-run' } else { '公開' }

<#
.SYNOPSIS
実行ブランチが許可ブランチに一致するかを判定する。
.DESCRIPTION
許可ブランチの指定は、完全一致（例: main）とワイルドカード（例: release/**）の両方を受け付ける。
ワイルドカードは PowerShell の -like（* と ?）で判定する。
#>
function Test-BranchAllowed {
    param(
        [string]$Name,
        [string[]]$Patterns
    )

    foreach ($pattern in $Patterns) {
        if ($pattern -eq $Name) {
            return $true
        }

        # ワイルドカードを含む指定（例: release/**）は -like で判定する
        if ($pattern -match '[*?]' -and $Name -like $pattern) {
            return $true
        }
    }

    return $false
}

# ─── 実行ブランチの検証 ───
if (-not (Test-BranchAllowed -Name $Branch -Patterns $allowedBranches)) {
    throw "モード $modeLabel は次のブランチからのみ実行できます: $($allowedBranches -join ', ')（現在: $Branch）"
}

# release/ 配下のブランチは release/<major>.<minor> の形式に限定し、入力バージョンの系列をブランチ名に合わせる
# （releaseBranches のワイルドカード（release/**）で、系列と無関係なブランチからのリリースを防ぐ）
if ($Branch -like 'release/*') {
    if ($Branch -notmatch '^release/(?<branchSeries>\d+\.\d+)$') {
        throw "release/ 配下のブランチ名は <major>.<minor> にしてください（現在: $Branch。例: release/1.0）"
    }

    $expectedSeries = $Matches['branchSeries']

    if ($series -ne $expectedSeries) {
        throw "ブランチ $Branch では系列 $expectedSeries のバージョンのみリリースできます（入力: $Version）"
    }
}

# ─── 既存タグの収集（v で始まる semver 形式のタグのみを対象にする）───
# 形式の判定はバージョンの規則（version.ps1 の Get-ReleasedVersions）に委譲する
$released = @(Get-ReleasedVersions -RepoRoot $RepoRoot)

# ─── 単調性の検証（同じ系列の最大より大きいこと）───
# 系列をまたぐ比較はしない（v2.0.0 があっても 1.0.1 をバックポートできるようにする）
# 同じ版は再リリースできないが、**dry-run では止めない**（版を上げていないだけの状態でも、
# それ以外の条件が通ることを確認できるようにする。 公開（release）では失敗させる）。
$seriesVersions = @($released | Where-Object { (Get-VersionSeries -Version $_) -eq $series })
$maxSeries = Get-MaxVersion -Versions $seriesVersions
$alreadyReleased = $false

if ($maxSeries -and -not (Test-VersionGreater -Version $Version -Than $maxSeries)) {
    $message = "入力 $Version は系列 $series の最大 $maxSeries 以下です。より大きいバージョンを指定してください。"

    if (-not $isDryRun) {
        throw $message
    }

    Write-Host "::warning::$message（dry-run のため続行します。 公開では失敗します）"
    $alreadyReleased = $true
}

# ─── タグの未作成確認 ───
if (git tag -l $tag) {
    $message = "タグ $tag は既に存在します。"

    if (-not $isDryRun) {
        throw $message
    }

    Write-Host "::warning::$message（dry-run のため続行します。 公開では失敗します）"
    $alreadyReleased = $true
}

# ─── 全タグの最大との比較（バックポートかの判定）───
# 入力が全タグの最大以下なら、旧系列へのリリース（バックポート）とみなす
$maxAll = Get-MaxVersion -Versions $released
$isBackport = $false

if ($maxAll -and -not (Test-VersionGreater -Version $Version -Than $maxAll)) {
    $isBackport = $true
}

# ─── バージョンファイルの書き換えと Latest の扱い ───
# main からのバックポートではバージョンファイルを書き換えない（main のバージョンを旧系列へ戻さないため）
$updateVersionFile = -not (($Branch -eq 'main') -and $isBackport)

# Latest にするのは「main からの通常のリリース」のみ
# （旧系列は、バックポートでも系列ブランチからのリリースでも Latest にしない）
$markLatest = ($Branch -eq 'main') -and (-not $isBackport)

# リリースノートは「同じ系列」の前回リリースからの差分で生成する
$notesStartTag = if ($maxSeries) { Get-TagName -Version $maxSeries } else { $null }

$maxSeriesLabel = if ($maxSeries) { $maxSeries } else { 'なし' }
$maxAllLabel = if ($maxAll) { $maxAll } else { 'なし' }
$kindLabel = if ($isBackport) { 'バックポート' } else { '通常のリリース' }
$versionFileLabel = if ($updateVersionFile) { '書き換える' } else { '書き換えない（main のバージョンを維持）' }

Write-Host "検証OK: version=$Version tag=$tag branch=$Branch"
Write-Host "  系列: $series / 系列の最大: $maxSeriesLabel / 全タグの最大: $maxAllLabel"
Write-Host "  種別: $kindLabel / バージョンファイル: $versionFileLabel"
if ($alreadyReleased) { Write-Host '  注意: この版は既に公開済みです（dry-run のため続行。 公開はできません）' }

# 呼び出し側が取り出せるよう、結果のみを JSON で出力する
Write-Output (@{
        version           = $Version
        tag               = $tag
        series            = $series
        prerelease        = (Test-PrereleaseVersion -Version $Version)
        isBackport        = $isBackport
        updateVersionFile = $updateVersionFile
        markLatest        = $markLatest
        notesStartTag     = $notesStartTag
        # dry-run で「同じ版」の警告だけを出して続行したか（公開すると失敗する状態か）
        alreadyReleased   = $alreadyReleased
    } | ConvertTo-Json -Compress)
