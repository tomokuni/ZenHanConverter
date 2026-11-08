<#
.SYNOPSIS
文書とコードの走査（引用の実在・体裁・命名・抑止の一覧・改行コード・フォルダなど）を実行し、合否を出す。

.DESCRIPTION
ビルドとテストを必要としない検査だけを持つ。 所有者は .github/検証手順.md であり、入口は
buildScript/verify.ps1 である（単独で実行せず、入口から起動される）。 入口は本スクリプトを
背景で起動し、ビルド・テストと重ねる（走査は 1 ファイルずつ読む独立した作業のため）。
`-Fix` のときは入口が直列で起動する（修正と判定の順序が決まっているため）。

.PARAMETER Fix
自動修正できる項目を修正する（markdownlint --fix）。

.EXAMPLE
pwsh -NoProfile -File buildScript/verifyDocs.ps1
#>
[CmdletBinding()]
param(
    [switch]$Fix
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repoRoot
$enc = [Text.UTF8Encoding]::new($false)
. (Join-Path $PSScriptRoot 'VerifyCommon.ps1')
# 製品名と配置はリポジトリごとに違うため、release-config.json から取る。
# `layout.roots` は「リポジトリ ルート直下の、プロジェクト フォルダを束ねるフォルダ」
# （アプリは `src`、複数ソリューションに分かれたライブラリは `EsUtil*`）。
$repoConfig = Get-RepoConfig $repoRoot
$productName = $repoConfig.product
$layout = $repoConfig.layout
if (-not $layout -or -not $layout.roots) { throw '.github/release-config.json の layout.roots がありません' }
$rootPatterns = @($layout.roots)
$rootDirs = @(Get-ChildItem -Path $repoRoot -Directory | Where-Object {
        $n = $_.Name
        @($rootPatterns | Where-Object { $n -like $_ }).Count -gt 0
    })
$rootPaths = @($rootDirs | ForEach-Object { $_.FullName })
$rootNames = @($rootDirs | ForEach-Object { $_.Name })
# 文書のパスと見出しの照合に使う正規表現（glob の `*` だけを「フォルダ名の任意の並び」として扱う）
$rootRx = '(?:' + (($rootPatterns | ForEach-Object { [regex]::Escape($_) -replace '\\\*', '[^/]*' }) -join '|') + ')'
# プロジェクトの見出し（H1）に使う名前。 `name` はフォルダ名、`path` は起点からのパスを `.` で繋いだ名前空間
$projectTitleStyle = if ($layout.projectTitleStyle) { $layout.projectTitleStyle } else { 'name' }
function Get-ProjectTitle([string]$root, [string]$project) {
    if ($projectTitleStyle -eq 'path') { return "$root.$project" }
    return $project
}
# 可搬な層（リポジトリの外でも使う層）のプロジェクト名の接頭辞。
# 可搬な層を持つリポジトリだけが設定し、その層に固有の検査（カテゴリ フォルダの単数形）を有効にする
$portablePrefixes = @(if ($layout.portablePrefixes) { $layout.portablePrefixes })
function Test-PortableProject([string]$name) {
    return @($portablePrefixes | Where-Object { $name.StartsWith($_) }).Count -gt 0
}
# 文書に書いてはいけない外部プロジェクト名（リポジトリごとに違うため release-config.json が持つ）
$externalDocTokens = @(if ($layout.externalDocTokens) { $layout.externalDocTokens })
$solutionLabels = @(if ($layout.solutionLabels) { $layout.solutionLabels })

# 以降のドキュメント検査で使う文書一覧（リポジトリ直下の文書も対象にする）
$docs = @(
    Get-ChildItem -Path $repoRoot -File -Filter '*.md'
    Get-ChildItem -Path (@($rootPaths) + @(Join-Path $repoRoot '.github')) -Recurse -File -Filter '*.md'
) | Where-Object { $_.FullName -notmatch '[\\/](obj|bin|archive)[\\/]' }

# パスの照合（8a）はビルドスクリプトのコメントも対象にする（実測: 削除済みの検証スクリプトを
# 指す参照が buildScript/*.bat のコメントに残っていた）
$scriptDocs = @(Get-ChildItem -Path (Join-Path $repoRoot 'buildScript') -Recurse -File -Include '*.bat', '*.ps1')

# ソースの一覧（複数の検査が同じ列挙を繰り返さないよう 1 回だけ作る。
# `-Include` は obj/bin まで全ファイルを走査してから絞るため遅い。 `-Filter` を使う）
$allCs = @(Get-ChildItem -Path $rootPaths -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' })
$allCsproj = @(Get-ChildItem -Path $rootPaths -Recurse -File -Filter '*.csproj' |
    Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' })

# リポジトリ外の参照先（例: 隣に置いた EsUtil リポジトリ）のソースも読む。
# 文書が外部の型を引用することはあるため、「型名の接頭辞が実在するか」の判定に含める
# （判定の目的は「存在しない名前空間を書いていないか」であり、参照先の型は実在する）。
$externalSrcRoots = [System.Collections.Generic.HashSet[string]]::new()
foreach ($csproj in $allCsproj) {
    $dir = Split-Path -Parent $csproj.FullName
    foreach ($m in [regex]::Matches([IO.File]::ReadAllText($csproj.FullName, $enc), '<ProjectReference\s+Include="(?<p>[^"]+)"')) {
        $full = [IO.Path]::GetFullPath((Join-Path $dir $m.Groups['p'].Value))
        if ($full.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) { continue }
        # 参照先のプロジェクト ファイルは `<参照先リポジトリ>/src/<プロジェクト>/src/…` に置かれる前提で
        # 参照先リポジトリの `src/` を型の供給元にする（推移的に参照するプロジェクトの型も含める）
        $srcRoot = Split-Path -Parent (Split-Path -Parent $full)
        if (Test-Path -LiteralPath $srcRoot) { [void]$externalSrcRoots.Add($srcRoot) }
    }
}

$externalCs = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
foreach ($root in $externalSrcRoots) {
    foreach ($f in (Get-ChildItem -Path $root -Recurse -File -Filter '*.cs' |
            Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' })) {
        $externalCs.Add($f)
    }
}

# パッケージ参照の型（`PackageReference` で参照している同一組織のパッケージ）は、
# パッケージに同封された XML ドキュメントから型名と名前空間を読む（ソースは手元に無い）。
# 読まないと、`ICaptureService.CaptureVirtualScreen` のような**実在する型**を「実在しない」と誤判定する
# （実測 2026-10-26: ProjectReference を PackageReference へ変えたときに 2 件の誤検出が出た）。
$externalPackageTypes = @{}
$lockFiles = @(Get-ChildItem -Path $repoRoot -Recurse -File -Filter 'packages.lock.json' |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' })
if ($lockFiles.Count -gt 0) {
    $locals = Invoke-NativeText 'dotnet' @('nuget', 'locals', 'global-packages', '--list')
    $globalPackages = (($locals.Text + $locals.ErrorText) -split "`r?`n" |
            Where-Object { $_ -match 'global-packages:' } |
            Select-Object -First 1) -replace '^global-packages:\s*', ''
}

foreach ($lock in $lockFiles) {
    $json = Get-Content -LiteralPath $lock.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($tfm in $json.dependencies.PSObject.Properties) {
        foreach ($package in $tfm.Value.PSObject.Properties) {
            $id = $package.Name
            if ($id -notmatch '^EsUtil\.') { continue }
            $version = $package.Value.resolved
            if (-not $version -or -not $globalPackages) { continue }
            $dir = Join-Path $globalPackages ("{0}\{1}" -f $id.ToLowerInvariant(), $version)
            $xml = Get-ChildItem -Path $dir -Recurse -File -Filter '*.xml' -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not $xml) { continue }
            $text = Get-Content -LiteralPath $xml.FullName -Raw -Encoding UTF8
            foreach ($m in [regex]::Matches($text, 'name="T:(?<t>[\w.`]+)"')) {
                $full = ($m.Groups['t'].Value -split '`')[0]
                $segments = $full.Split('.')
                $typeName = $segments[-1]
                $nsSegments = $segments[0..($segments.Count - 2)]
                if (-not $externalPackageTypes.ContainsKey($typeName)) {
                    $externalPackageTypes[$typeName] = [System.Collections.Generic.HashSet[string]]::new()
                }
                for ($k = 0; $k -lt $nsSegments.Count; $k++) {
                    [void]$externalPackageTypes[$typeName].Add(($nsSegments[$k..($nsSegments.Count - 1)] -join '.'))
                }
            }
        }
    }
}

# ============================================================
# 5. ドキュメント（markdownlint）
# ============================================================
Write-Output '== 5. ドキュメント（markdownlint） =='
# ツールの版はここが単一所有する（`npx --yes markdownlint-cli2` は毎回「最新」を解決するため、
# 版が上がると同じ文書の合否が変わる。 CI のキャッシュも版に追随させる）。
# 版を上げるときは、この行を直してから `-Fix` で全文書を整形し直す。
$mdVersion = 'markdownlint-cli2@0.24.0'
$mdArgs = @('--yes', $mdVersion, '--config', 'buildScript/markdownlint.json')
if ($Fix) { $mdArgs += '--fix' }
$mdArgs += @(@($rootPatterns | ForEach-Object { "$_/**/*.md" }) + @('.github/**/*.md', '*.md', '!**/obj/**', '!**/bin/**'))
# npx は Windows では `cmd.exe` 経由で起動する（`.cmd` は直接起動できない）。 Linux / macOS では
# 直接起動する（CI の docs ジョブは ubuntu で走る）。 出力の復号は Invoke-NativeText が行う。
$mdRun = if ($env:ComSpec -and (Test-Path -LiteralPath $env:ComSpec)) {
    Invoke-NativeText $env:ComSpec (@('/c', 'npx') + $mdArgs)
}
else {
    Invoke-NativeText 'npx' $mdArgs
}
$mdText = $mdRun.Text + $mdRun.ErrorText
# 自動修正した場合は、以降の検査が古い内容を読まないようキャッシュを捨てる
if ($Fix) { Clear-FileCache }
$mdIssues = 0
if ($mdText -match 'Summary:\s*(\d+) issues? in (\d+) files?') { $mdIssues = [int]$Matches[1] }
Add-Result 'markdownlint' ($mdIssues -eq 0) "$mdIssues 件"
if ($mdIssues -gt 0) { $mdText -split "`r?`n" | Where-Object { $_ -match 'error|warning' } | Select-Object -First 10 | ForEach-Object { "      $($_.Trim())" } }

# ============================================================
# 6. 相対リンク（存在しないファイルへの参照）
# ============================================================
Write-Output '== 6. 相対リンク =='
$brokenLinks = [System.Collections.Generic.List[string]]::new()
# リポジトリ外（隣接リポジトリ）への参照のうち、そのリポジトリが無い環境で飛ばした件数
$externalLinks = 0
$repoParent = Split-Path $repoRoot -Parent
$rxLink = [regex]'\]\((?<t>[^)\s]+)\)'
# **インライン コードは「同じ長さのバックティックで囲まれた区間」**とする（Markdown の仕様）。
# コード スパンは行をまたげるため、行単位に区切らずに対応させる。 **終端はバックティック列の長さが
# 開始と一致する場合だけ**認識する（囲みより長い列を内部に含む正当なコード スパンを除去できるようにするため）。
$rxCode = [regex]'(?s)(?<!`)(`+)(?!`)(?:(?!(?<!`)\1(?!`)).)*?(?<!`)\1(?!`)'

# **フェンス付きコード ブロック（``` / ~~~）はリンク検査の対象外**にする（中身は例示である）。
# 先に除去しないと、フェンスのバックティックが本文のバックティックと対になり、対応がずれる。
# 終了フェンスは**開始と同じ文字で、開始以上の長さ**の列だけを認識する（Markdown の仕様）。
# 状態遷移で 1 行ずつ見る（正規表現では列の長さの比較ができないため）。
# 改行は CRLF / LF のどちらでも扱えるよう、行の分割と結合で吸収する。
function Remove-FencedCode([string]$Text) {
    $lines = $Text -split "`r?`n"
    $kept = [System.Collections.Generic.List[string]]::new()
    $fenceChar = [char]0
    $fenceLength = 0
    # 開始フェンスは情報文字列（```` ```powershell ```` など）を持てる
    $rxOpen = [regex]'^[ \t]*(?<run>`{3,}|~{3,})'
    # 終了フェンスは**列の後ろが空白だけ**の行に限る（情報文字列を持つ行は本文である）
    $rxClose = [regex]'^[ \t]*(?<run>`{3,}|~{3,})[ \t]*$'
    foreach ($line in $lines) {
        if ($fenceChar -eq [char]0) {
            $m = $rxOpen.Match($line)
            if ($m.Success) {
                $fenceChar = $m.Groups['run'].Value[0]
                $fenceLength = $m.Groups['run'].Value.Length
                $kept.Add('')
                continue
            }
            $kept.Add($line)
            continue
        }
        # フェンスの中（リンク検査の対象外）
        $kept.Add('')
        $m = $rxClose.Match($line)
        if ($m.Success -and $m.Groups['run'].Value[0] -eq $fenceChar -and $m.Groups['run'].Value.Length -ge $fenceLength) {
            $fenceChar = [char]0
            $fenceLength = 0
        }
    }
    return ($kept -join "`n")
}

foreach ($f in $docs) {
    # コード片の中のリンク風表記は文書の例示なので対象外にする（置換先にバックティックを使わない）
    $text = Remove-FencedCode (Get-Text $f.FullName)
    $text = $rxCode.Replace($text, ' ')
    foreach ($m in $rxLink.Matches($text)) {
        $target = $m.Groups['t'].Value
        if ($target -match '^(https?:|mailto:|\w+:|#)') { continue }
        $path = [Uri]::UnescapeDataString($target.Split('#')[0])
        if ($path.Length -eq 0) { continue }
        # ファイルへの参照だけを対象にする（`.` も `/` も含まない語は例示・用語）
        if ($path -notmatch '[./]') { continue }
        $full = [IO.Path]::GetFullPath((Join-Path $f.DirectoryName $path))
        if (Test-Path -LiteralPath $full) { continue }
        # 隣接リポジトリ（`../../EsUtil/...`）への参照は、そのリポジトリを取得していない環境
        # （CI の単一リポジトリ チェックアウト）では解決できない。 **隣接リポジトリ自体が無い場合は
        # 対象外**にし、ある場合はリンク切れとして報告する（ローカルでは従来どおり検証する。 実測 2026-10-26）。
        if (-not $full.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
            $sibling = Join-Path $repoParent ([IO.Path]::GetRelativePath($repoParent, $full).Split([IO.Path]::DirectorySeparatorChar)[0])
            if (-not (Test-Path -LiteralPath $sibling)) { $externalLinks++; continue }
        }
        $brokenLinks.Add("$($f.FullName.Substring($repoRoot.Length + 1)) -> $target")
    }
}
Add-Result '相対リンク' ($brokenLinks.Count -eq 0) "$($brokenLinks.Count) 件"
if ($externalLinks -gt 0) { Add-Info '相対リンク（隣接リポジトリ）' "対象外 $externalLinks 件（隣接リポジトリを取得していないため検証しない）" }
$brokenLinks | Select-Object -First 10 | ForEach-Object { "      $_" }

# ============================================================
# 7. 名前空間（フォルダへの追従）
# ============================================================
# 検査はビルドが担う（IDE0130: 名前空間がフォルダー構造と一致しない）。
# 実測で IDE0130 が期待どおりに検出することを確かめてあり、自作の照合は持たない。
Write-Output '== 7. 名前空間（ビルドの IDE0130 が強制する） =='
Add-Info '名前空間' 'ビルドの IDE0130 が強制する（不一致はビルドが失敗する）'

# ============================================================
# 8. 文書が引用する名前空間・ファイル（実在の確認）
# ============================================================
Write-Output '== 8. 文書の引用（実在の確認） =='
$tracked = @{}
$trackedNames = @{}
foreach ($f in (Get-GitList @('ls-files'))) {
    $tracked[$f] = $true
    $trackedNames[[IO.Path]::GetFileName($f)] = $true
}
$projectNames = @(Get-ChildItem -Path $rootPaths -Directory | Select-Object -ExpandProperty Name)
# プロジェクト内のフォルダ（部分的な構成図のルートとして許可する）
$intraFolders = @('src', 'tests', 'sample', 'docs')

# 8a) 文書の裸の相対パス（プロジェクト名から始まっていないもの）
$rxQuotedPath = [regex]'(?<![\w./\\-])((?:[A-Za-z0-9_.\-]+[\\/])+[A-Za-z0-9_.\-]+\.(?:csproj|axaml|props|slnx|json|yaml|yml|ps1|bat|cs))(?![\w])'
$loosePaths = [System.Collections.Generic.List[string]]::new()
foreach ($f in ($docs + $scriptDocs)) {
    $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    $docDir = ([IO.Path]::GetDirectoryName($rel) + '/').Replace('\', '/')
    $lines = Get-Lines $f.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        # 環境変数を含む行のパスはリポジトリ外（%LOCALAPPDATA%\~ 等）を指すため対象外にする
        if ($lines[$i] -match '%[A-Za-z_]+%|\$env:') { continue }
        $line = $rxLink.Replace($lines[$i], ']()')
        foreach ($m in $rxQuotedPath.Matches($line)) {
            $tok = $m.Groups[1].Value
            if ($tok -match '^https?') { continue }
            if ($tok.StartsWith('..')) { continue }
            $norm = $tok.Replace('\', '/')
            # 実在の判定は追跡ファイルと作業ツリーの両方で行う（未追跡の新規ファイルを「解決不能」としない）
            if ($tracked.ContainsKey($norm) -or $tracked.ContainsKey($docDir + $norm)) { continue }
            if (Test-Path -LiteralPath (Join-Path $repoRoot $norm)) { continue }
            if (Test-Path -LiteralPath (Join-Path $f.DirectoryName $norm)) { continue }
            $cands = @()
            foreach ($root in $rootNames) {
                foreach ($p in $projectNames) {
                    foreach ($pre in @('', 'src/', 'tests/', 'sample/')) {
                        if ($tracked.ContainsKey("$root/$p/$pre$norm")) { $cands += "$root/$p/$pre$norm" }
                    }
                }
            }
            if ($cands.Count -eq 0) { $loosePaths.Add(("解決不能 {0}:{1}  {2}" -f $rel, ($i + 1), $tok)) }
            elseif ($cands.Count -eq 1) { $loosePaths.Add(("要統一 {0}:{1}  {2} -> {3}" -f $rel, ($i + 1), $tok, $cands[0])) }
        }
    }
}

# 8b) プロジェクト構成図のルートとファイル名
$treeIssues = [System.Collections.Generic.List[string]]::new()
$rxTreeFile = [regex]'(?<![\w./\\-])([A-Za-z0-9_.\-]+\.(?:csproj|axaml|cs))(?![\w])'
foreach ($f in $docs) {
    $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    $lines = Get-Lines $f.FullName
    $inTree = $false
    $treeRootSeen = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line.StartsWith('```')) {
            if ($inTree) { $inTree = $false; } elseif ($line.StartsWith('```text')) { $inTree = $true; $treeRootSeen = $false }
            continue
        }
        if (-not $inTree) { continue }
        # ツリーのルートになり得るのはブロックの先頭行だけ（以降の行はプロジェクト内の相対）
        if (-not $treeRootSeen) {
            if ($line.Trim().Length -eq 0) { continue }
            $treeRootSeen = $true
            if ($line -match "^(?:$rootRx/)?([A-Za-z0-9_.\-]+)/\s*$") {
                $label = $Matches[1]
                # ルートはプロジェクト フォルダ名、起点のフォルダ名、ソリューション名、またはプロジェクト内のフォルダ（部分的な構成図）
                if ($projectNames -notcontains $label -and $intraFolders -notcontains $label -and $rootNames -notcontains $label -and $solutionLabels -notcontains $label) {
                    $treeIssues.Add(("ルート名 {0}:{1}  {2}" -f $rel, ($i + 1), $label))
                }
            }
        }
        foreach ($m in $rxTreeFile.Matches($line)) {
            $n = $m.Groups[1].Value
            if (-not $trackedNames.ContainsKey($n)) { $treeIssues.Add(("ファイル名 {0}:{1}  {2}" -f $rel, ($i + 1), $n)) }
        }
    }
}

# 8c) 陳腐化した識別子（改名・削除の残骸）
#     全追跡ファイルを対象にする（文書だけでなくビルドスクリプト・設定ファイルのコメントも含める）。
#     **パターンはリポジトリごとに違う**ため、スクリプトへ書かず `.github/verify-stale-patterns.txt` が持つ
#     （1 行 1 パターンの正規表現。 `#` で始まる行と空行は無視する。 対象を追加して育てる）。
$stalePatternsPath = Join-Path $repoRoot '.github/verify-stale-patterns.txt'
if (-not (Test-Path -LiteralPath $stalePatternsPath)) { throw "陳腐化パターンの一覧がありません: $stalePatternsPath" }
$stalePatterns = @([IO.File]::ReadAllLines($stalePatternsPath, $enc) |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_ -and -not $_.StartsWith('#') })
# パターンが 1 つも無いのは正当な状態（既知の残骸が無いリポジトリ）。 その場合は 8c を省略する。
$skipStale = $stalePatterns.Count -eq 0
$stale = [System.Collections.Generic.List[string]]::new()
# 走査は 1 回にする（パターンごとに `git grep` を起動すると、パターン数だけリポジトリ全体を走査する。
# 実測: 8 パターンで 8 回 → 1 回にまとめた）。 当たったパターンは一致部分から特定する。
$rxStale = if ($skipStale) { $null } else { [regex]('(?<p>' + ($stalePatterns -join '|') + ')') }
$staleHits = if ($skipStale) { @() } else { Get-GitList @('-c', 'core.quotepath=false', 'grep', '--untracked', '-n', '-E', '--', ($stalePatterns -join '|')) }
foreach ($hit in $staleHits) {
    if ($hit -match 'archive/') { continue }
    # パターンの一覧そのものは対象外（自分の定義に一致してしまう）
    if ($hit -match 'verify-stale-patterns\.txt') { continue }
    # 外部プロジェクト名の一覧（release-config.json の layout.externalDocTokens）も対象外（同じ理由）
    if ($hit -match '\.github/release-config\.json') { continue }
    # バイナリ（バンドル・パッケージ・画像等）は対象外にする（`--untracked` は作業ツリーの
    # 非テキスト ファイルも走査するため、圧縮データの中身がたまたま一致することがある）
    if ($hit -match '\.(bundle|nupkg|snupkg|zip|dll|exe|pdb|png|jpg|jpeg|gif|ico|pdf|snk|pfx|ttf|woff|woff2)(:\d+)?:') { continue }
    # 検証手順と本スクリプトは、この検査の説明としてこれらの名前を引用するため対象外にする
    if ($hit -match '検証手順\.md') { continue }
    # 本検査の実装（パターンそのものを書く）は対象外にする
    if ($hit -match 'buildScript/(verify|verifyDocs|VerifyCommon)\.ps1') { continue }
    $matched = $rxStale.Match($hit)
    $label = if ($matched.Success) { $matched.Groups['p'].Value } else { $stalePatterns -join '|' }
    $stale.Add("$label : $hit")
}

# 8d) 節参照の破損（文書.md の直後に「§」が無い数字が続く形。 一括置換で § が消えた残骸）
$rxSectionRef = [regex]'\]\([^)]*\.md\)\s*\d'
foreach ($f in $docs) {
    $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    $lines = Get-Lines $f.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($rxSectionRef.IsMatch($lines[$i])) { $stale.Add(("節参照 {0}:{1}  {2}" -f $rel, ($i + 1), $lines[$i].Trim())) }
    }
}

$quoteIssues = $loosePaths.Count + $treeIssues.Count + $stale.Count
Add-Result '文書の引用' ($quoteIssues -eq 0) "パス $($loosePaths.Count) / 構成図 $($treeIssues.Count) / 陳腐化 $($stale.Count)"
$loosePaths | Select-Object -First 8 | ForEach-Object { "      $_" }
$treeIssues | Select-Object -First 8 | ForEach-Object { "      $_" }
$stale | Select-Object -First 8 | ForEach-Object { "      $_" }

# 8d) 文書へ陳腐化する統計（テスト件数など）を書いていないか
#     「（41 件）」「テストは **577 件**」のような実測値はコードを変えるたびに古くなる。
#     .github/ドキュメント規約.md §10 が「書かない」と定めており、実測は verify.ps1 が出力する。
#     検査を説明する本手順だけは例として引用するため対象外にする（8c と同じ扱い）。
$rxStatistic = [regex]'（[1-9]\d* 件）|テストは \*\*[1-9]\d* 件'
$statistic = [System.Collections.Generic.List[string]]::new()
foreach ($f in $docs) {
    $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    if ($rel -match '検証手順\.md$') { continue }
    $lines = Get-Lines $f.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($rxStatistic.IsMatch($lines[$i])) { $statistic.Add(("{0}:{1}  {2}" -f $rel, ($i + 1), $lines[$i].Trim())) }
    }
}
Add-Result '文書の統計値' ($statistic.Count -eq 0) "陳腐化する件数の記載 $($statistic.Count) 件"
$statistic | Select-Object -First 10 | ForEach-Object { "      $_" }

# 8e) 文書が引用する型名の接頭辞（実在しない名前空間を書かない）
#     文書は型を「ルート名前空間の型 = 接頭辞なし」「下位名前空間の型 = 名前空間の末尾セグメントを接頭辞」で書く
#     （`Logging.MarqueeLogMessage`）。 フォルダ名を名前空間として書いた `Drawing.MarqueeGeometry` のような
#     陳腐化は、名前の実在検査（8a / 8b）では見つからない（型名は実在するため。 実測: 4 文書 24 箇所が該当した）。
#     - 判定は「点で修飾された名前の**末尾が実在の型名**」のものだけを対象にする（`MarqueeNotice.Kind` 等は対象外）。
#     - 修飾子が「名前空間の接尾辞（セグメント単位）」または「外側の型名（入れ子型）」なら適合とする。
#     - 修飾子が小文字始まり（`e.Rect` のようなローカル変数・引数のメンバー参照）は対象外にする。
#     - 外部フレームワーク（`System.` / `Aprillz.` 等）はリポジトリの型名と同名の型を持つため対象外にする。
$rxTypeDecl = [regex]'(?m)^\s*(?:(?:public|internal|private|protected|static|sealed|abstract|partial|readonly|ref|file|unsafe|new|required)\s+)*(?:class|struct|interface|enum|delegate|record(?:\s+(?:class|struct))?)\s+([A-Za-z_]\w*)'
$typeQualifiers = @{}
foreach ($f in (@($allCs) + @($externalCs))) {
    $text = Get-Text $f.FullName
    $nsMatch = [regex]::Match($text, '(?m)^namespace\s+([\w.]+)\s*;')
    $suffixes = @()
    if ($nsMatch.Success) {
        $segments = $nsMatch.Groups[1].Value.Split('.')
        for ($k = 0; $k -lt $segments.Count; $k++) { $suffixes += ($segments[$k..($segments.Count - 1)] -join '.') }
    }
    foreach ($tm in $rxTypeDecl.Matches($text)) {
        $name = $tm.Groups[1].Value
        if (-not $typeQualifiers.ContainsKey($name)) { $typeQualifiers[$name] = [System.Collections.Generic.HashSet[string]]::new() }
        foreach ($s in $suffixes) { [void]$typeQualifiers[$name].Add($s) }
    }
}
# パッケージ参照の型（XML ドキュメントから読んだもの）を足す
foreach ($name in $externalPackageTypes.Keys) {
    if (-not $typeQualifiers.ContainsKey($name)) { $typeQualifiers[$name] = [System.Collections.Generic.HashSet[string]]::new() }
    foreach ($s in $externalPackageTypes[$name]) { [void]$typeQualifiers[$name].Add($s) }
}
$rxTypeRef = [regex]'(?<![\w.])((?:[A-Za-z_]\w*\.)+[A-Za-z_]\w*)(?![\w./\\-])'
$rxExternalRoot = [regex]'^(System|Microsoft|Avalonia|SkiaSharp|Serilog|Xunit|Newtonsoft|Windows|Moq|NSubstitute|Aprillz|ShimSkiaSharp|HarfBuzz|CommunityToolkit)\b'
# **参照先の型を 1 つも解決できないときは判定しない**（INFO）。 文書は隣のリポジトリ（EsUtil）の型を
# 引用するため、そのソースもパッケージの XML ドキュメントも無い環境では「実在しない」と誤判定する
# （実測 2026-10-27: 文書だけの変更で復元を省略した CI が 2 件を誤検出した）。
$externalTypesAvailable = ($externalCs.Count -gt 0) -or ($externalPackageTypes.Count -gt 0)
$typeRefIssues = [System.Collections.Generic.List[string]]::new()
foreach ($f in $(if ($externalTypesAvailable) { $docs } else { @() })) {
    $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    # 本検査の説明（不合格の例を引用する）は対象外にする（8c と同じ扱い）
    if ($rel -match '検証手順\.md$') { continue }
    $lines = Get-Lines $f.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        foreach ($m in $rxTypeRef.Matches($lines[$i])) {
            $token = $m.Groups[1].Value
            $segments = $token.Split('.')
            $last = $segments[-1]
            if (-not $typeQualifiers.ContainsKey($last)) { continue }
            if ($rxExternalRoot.IsMatch($token)) { continue }
            $isVariable = $false
            for ($k = 0; $k -lt $segments.Count - 1; $k++) { if ($segments[$k] -cmatch '^[a-z]') { $isVariable = $true } }
            if ($isVariable) { continue }
            $qualifier = ($segments[0..($segments.Count - 2)] -join '.')
            $ok = $typeQualifiers[$last].Contains($qualifier)
            if (-not $ok -and $typeQualifiers.ContainsKey($segments[-2])) { $ok = $true }
            if (-not $ok) {
                # 修飾子の途中に型名がある場合（`X.Y.Type.Member` の型部分）は判定しない
                $middleIsType = $false
                if ($segments.Count -ge 3) {
                    for ($k = 0; $k -lt $segments.Count - 2; $k++) { if ($typeQualifiers.ContainsKey($segments[$k])) { $middleIsType = $true } }
                }
                if (-not $middleIsType) { $typeRefIssues.Add(("{0}:{1}  {2}" -f $rel, ($i + 1), $token)) }
            }
        }
    }
}
if ($externalTypesAvailable) {
    Add-Result '文書の型参照' ($typeRefIssues.Count -eq 0) "$($typeRefIssues.Count) 件"
}
else {
    Add-Info '文書の型参照' '省略（参照先（EsUtil）の型を解決できない。 ソースもパッケージの XML ドキュメントも無い）'
}
$typeRefIssues | Select-Object -First 10 | ForEach-Object { "      $_" }

# 8f) 文書の体裁（規約 §6・§7）
#     - `docs/SPEC_*.md` は見出しへ番号を付け、冒頭へ `## 目次` を置く（README は番号のみ。 目次は任意）。
#     - 番号は階層を表す（`##` = `<連番>` + ピリオド / `###` = `<親>.<連番>` / `####` = `<親>.<連番>.<連番>`）。
#     - 目次は `##` 以下の見出しだけを、**番号付きの文言**と一致するアンカーで列挙する
#       （見出しを足したのに目次を直していない状態は、markdownlint の MD051 では見つからない）。
#     - 目次見出しは `## 目次 <!-- omit from toc -->` と書く（エディタの目次整形拡張が
#       目次自身を目次へ載せないためのマーカー。 マーカー無しも受け付ける）。
#     - `SPEC_System.md` へ「単一所有」「移行の経緯」等の独立した章を設けない（規約 §6）。
function Get-MdHeadings([string]$Path) {
    $lines = [IO.File]::ReadAllLines($Path, $enc)
    $result = [System.Collections.Generic.List[object]]::new()
    $inFence = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i].StartsWith('```')) { $inFence = -not $inFence; continue }
        if ($inFence) { continue }
        if ($lines[$i] -match '^(#{1,6}) (.+)$') {
            $result.Add([pscustomobject]@{ Line = $i + 1; Level = $Matches[1].Length; Text = $Matches[2] })
        }
    }
    return ,$result
}

function Get-MdSlug([string]$Text) {
    $s = [regex]::Replace($Text, '[^\p{L}\p{M}\p{Nd}\p{Pc}\- ]', '')
    return ($s.ToLowerInvariant() -replace '\s', '-')
}

$rxHeadingNumber = [regex]'^(\d+(?:\.\d+)*)(\.?)\s+(.+)$'
$rxTocHeading = [regex]'^目次( <!-- omit from toc -->)?$'
$rxForbiddenChapter = [regex]'移行|経緯|変更履歴|最適化|単一所有|以前は|旧仕様|旧名'
$rxTocItem = [regex]'^\s*- '
$styleIssues = [System.Collections.Generic.List[string]]::new()
foreach ($f in $docs) {
    $name = [IO.Path]::GetFileName($f.FullName)
    $isSpec = $f.FullName -match '[\\/]docs[\\/]SPEC_[^\\/]+\.md$'
    if (-not $isSpec -and $name -ne 'README.md') { continue }
    $isSystem = $f.FullName -match '[\\/]docs[\\/]SPEC_System\.md$'
    $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    $headings = Get-MdHeadings $f.FullName
    if (@($headings | Where-Object { $_.Level -eq 1 }).Count -ne 1) { $styleIssues.Add(("{0}  H1 が 1 件でない" -f $rel)); continue }

    $expectedToc = [System.Collections.Generic.List[string]]::new()
    $counters = @{}
    foreach ($h in $headings) {
        if ($h.Level -eq 1) { continue }
        if ($h.Level -eq 2 -and $rxTocHeading.IsMatch($h.Text)) { continue }
        if ($isSystem -and $rxForbiddenChapter.IsMatch($h.Text)) {
            $styleIssues.Add(("{0}:{1}  規約 §6 が禁じる章（独立性の高い章を設けない）: {2}" -f $rel, $h.Line, $h.Text))
        }
        if ($h.Level -gt 4) { $styleIssues.Add(("{0}:{1}  見出しの階層が 4 より深い: {2}" -f $rel, $h.Line, $h.Text)); continue }
        $m = $rxHeadingNumber.Match($h.Text)
        if (-not $m.Success) { $styleIssues.Add(("{0}:{1}  見出しに番号が無い: {2}" -f $rel, $h.Line, $h.Text)); continue }
        $numText = $m.Groups[1].Value
        $dot = $m.Groups[2].Value
        $rest = $m.Groups[3].Value
        $parts = $numText.Split('.')
        if ($parts.Count -ne ($h.Level - 1)) {
            $styleIssues.Add(("{0}:{1}  番号の階層が見出しと一致しない: {2}" -f $rel, $h.Line, $h.Text))
            continue
        }
        if ($h.Level -eq 2 -and $dot -ne '.') { $styleIssues.Add(("{0}:{1}  `##` の番号にピリオドが無い: {2}" -f $rel, $h.Line, $h.Text)) }
        if ($h.Level -ne 2 -and $dot -eq '.') { $styleIssues.Add(("{0}:{1}  番号に余分なピリオドがある: {2}" -f $rel, $h.Line, $h.Text)) }

        $sequential = $true
        for ($lv = 0; $lv -lt $parts.Count; $lv++) {
            $key = $lv + 2
            if ($lv -eq ($parts.Count - 1)) { $counters[$key] = [int]$counters[$key] + 1 }
            elseif (-not $counters.ContainsKey($key)) { $counters[$key] = 1 }
            if ($counters[$key] -ne [int]$parts[$lv]) { $sequential = $false }
        }
        for ($lv = ($h.Level + 1); $lv -le 6; $lv++) { [void]$counters.Remove($lv) }
        if (-not $sequential) { $styleIssues.Add(("{0}:{1}  番号が連番でない: {2}" -f $rel, $h.Line, $h.Text)) }

        $display = if ($h.Level -eq 2) { "$numText." } else { $numText }
        $full = "$display $rest"
        $indent = ' ' * (($h.Level - 2) * 2)
        $expectedToc.Add(("{0}- [{1}](#{2})" -f $indent, $full, (Get-MdSlug $full)))
    }

    $lines = Get-Lines $f.FullName
    $tocIdx = -1
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($rxTocHeading.IsMatch(($lines[$i] -replace '^## ', ''))) { $tocIdx = $i; break } }
    if ($isSpec) {
        if ($tocIdx -lt 0) { $styleIssues.Add(("{0}  目次が無い（規約 §7）" -f $rel)) }
        else {
            $firstH2 = @($headings | Where-Object { $_.Level -eq 2 } | Select-Object -First 1)
            if ($firstH2 -and -not ($rxTocHeading.IsMatch($firstH2.Text))) { $styleIssues.Add(("{0}  目次が最初の ## 見出しでない（最初は「{1}」）" -f $rel, $firstH2.Text)) }
        }
    }
    if ($tocIdx -ge 0) {
        $actualToc = [System.Collections.Generic.List[string]]::new()
        for ($i = $tocIdx + 1; $i -lt $lines.Count; $i++) {
            if ($rxTocItem.IsMatch($lines[$i])) { $actualToc.Add($lines[$i]); continue }
            if ($lines[$i].Trim() -eq '' -and $actualToc.Count -eq 0) { continue }
            break
        }
        if ($actualToc.Count -ne $expectedToc.Count) {
            $styleIssues.Add(("{0}  目次の件数が一致しない: 実測 {1} / 期待 {2}（見出しを変えたら目次を直す）" -f $rel, $actualToc.Count, $expectedToc.Count))
        }
        else {
            for ($i = 0; $i -lt $actualToc.Count; $i++) {
                if ($actualToc[$i] -ne $expectedToc[$i]) {
                    $styleIssues.Add(("{0}  目次が一致しない: 実測 [{1}] / 期待 [{2}]" -f $rel, $actualToc[$i], $expectedToc[$i]))
                    break
                }
            }
        }
    }
}
Add-Result '文書の体裁' ($styleIssues.Count -eq 0) "番号・目次・禁止章 $($styleIssues.Count) 件"
$styleIssues | Select-Object -First 10 | ForEach-Object { "      $_" }

# 8g) パス記法（規約 §8: プロジェクト内のパスは `src/<プロジェクト名>/…` から書く）
#     プロジェクト内のフォルダ名（`src/<プロジェクト>/src|tests|sample` の直下）で始まる `` `A/B` `` 形式の
#     引用は、どのプロジェクトのものか読めない（実測: `Native/Win` のような記述が残っていた）。
#     リポジトリ ルートのフォルダ（`publish/` / `.work/` 等）、リンクの表示文、コードフェンス内は対象外にする。
$innerFolders = [System.Collections.Generic.HashSet[string]]::new()
foreach ($root in $rootNames) {
    foreach ($p in (Get-GitList @('ls-files', "$root/*/src/*", "$root/*/tests/*", "$root/*/sample/*"))) {
        $segments = $p.Split('/')
        if ($segments.Count -ge 5) { [void]$innerFolders.Add($segments[3]) }
    }
}
$rxAllowedRoot = [regex]("^(?:$rootRx|tests|sample|docs|buildScript|publish|artifacts|\.github|\.work|\.vscode|obj|bin)/")
$rxPathLike = [regex]'^[A-Za-z][\w.\-]*/[\w.\-]+$'
$rxBacktick = [regex]'`([^`]+)`'
$pathNotation = [System.Collections.Generic.List[string]]::new()
foreach ($f in $docs) {
    $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    # 本検査の説明（規則が反例を引用する）は対象外にする（8c / 8e と同じ扱い）
    if ($rel -match '(検証手順|ドキュメント規約)\.md$') { continue }
    $lines = Get-Lines $f.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i].StartsWith('```')) { continue }
        foreach ($m in $rxBacktick.Matches($lines[$i])) {
            $token = $m.Groups[1].Value
            if (-not $rxPathLike.IsMatch($token)) { continue }
            if ($rxAllowedRoot.IsMatch($token)) { continue }
            if ($lines[$i].Substring($m.Index + $m.Length) -like '](*') { continue }
            if ($innerFolders.Contains($token.Split('/')[0])) { $pathNotation.Add(("{0}:{1}  {2}" -f $rel, ($i + 1), $token)) }
        }
    }
}
Add-Result '文書のパス記法' ($pathNotation.Count -eq 0) "$($pathNotation.Count) 件"
$pathNotation | Select-Object -First 10 | ForEach-Object { "      $_" }

# 8h) 文書とコメントの記載範囲（依存関係 §7 / ドキュメント規約 §11）
#     本リポジトリに無いプロジェクト名を、文書とコメントに書かない（読者が「その型を直接使える」と
#     誤解し、可搬性が損なわれる）。 対象の一覧はリポジトリごとに違う（本リポジトリが持たない
#     プロジェクトの名前を並べる必要がある）ため、スクリプトへ書かず
#     `.github/release-config.json` の `layout.externalDocTokens` が持つ（依存関係 §3 が所有者）。
#     - 独立パッケージ用の複製の「複製元」を示すコメントは規則が認める例外である（依存関係 §4.1）。
#     - 相対リンクの URL 部は対象外（リンク先は実在のパスを指す必要がある）。
if ($externalDocTokens.Count -eq 0) {
    Add-Info '文書の記載範囲' '省略（.github/release-config.json の layout.externalDocTokens が無い）'
}
else {
    $rxExternalProject = [regex]('(?<![\w.-])(' + (($externalDocTokens | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')(?![\w.-])')
    $scopeIssues = [System.Collections.Generic.List[string]]::new()
    $rxMdLinkUrl = [regex]'\]\([^)]*\)'
    foreach ($target in @($allCsproj)) {
        $targetDir = Split-Path -Parent $target.FullName
        $targetPrefix = $targetDir + [IO.Path]::DirectorySeparatorChar
        $targetRel = $targetDir.Substring($repoRoot.Length + 1).Replace('\', '/')
        if ($targetRel -match '^buildScript') { continue }
        $scopeFiles = @(@($allCs | Where-Object { $_.FullName.StartsWith($targetPrefix, [StringComparison]::OrdinalIgnoreCase) }) +
            @($docs | Where-Object { $_.FullName.StartsWith($targetPrefix, [StringComparison]::OrdinalIgnoreCase) }))
        foreach ($f in $scopeFiles) {
            $fileRel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
            $lines = Get-Lines $f.FullName
            for ($i = 0; $i -lt $lines.Count; $i++) {
                $line = $lines[$i]
                if ($line.Contains('複製元')) { continue }
                if ($line.IndexOf('](') -ge 0) { $line = $rxMdLinkUrl.Replace($line, ']()') }
                if ($rxExternalProject.IsMatch($line)) {
                    $scopeIssues.Add(("{0}:{1}  [{2}]" -f $fileRel, ($i + 1), $rxExternalProject.Match($line).Value))
                }
            }
        }
    }
    Add-Result '文書の記載範囲' ($scopeIssues.Count -eq 0) "本リポジトリ外のプロジェクト $($scopeIssues.Count) 件"
    $scopeIssues | Select-Object -First 10 | ForEach-Object { "      $_" }
}

# 8i) 状態を表す真偽値の命名（命名規約: 述語の接頭辞）
#     状態・判定の真偽値は Is / Has / Can / Should / Show で始める（`Redraw` では true の意味が読めない）。
#     過去形（`Was`）は使わない（真偽値の種類を増やさない）。
#     設定・外観（Settings / Options / Appearance / Config）の真偽値は動作を表す動詞句でもよい（.NET の慣習）。
$rxStateBool = [regex]'(?<![\w.])(?:public|internal|private|protected)\s+(?:static\s+)?(?:virtual\s+|override\s+|sealed\s+|new\s+|readonly\s+)*bool\s+(?<n>[A-Za-z_]\w*)\s*\{'
$rxBoolPredicate = [regex]'^(Is|Has|Can|Should|Show|Use|Supports|Contains|Needs)'
$rxSettingsTypeName = [regex]'(Settings|Options|Appearance|Config)$'
$boolIssues = [System.Collections.Generic.List[string]]::new()
foreach ($f in ($allCs)) {
    $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    $lines = Get-Lines $f.FullName
    # ファイル先頭の型をそのファイルの型とみなす（1 ファイル 1 型の規約）
    $declaredType = ''
    foreach ($line in $lines) {
        $typeMatch = [regex]::Match($line, '(?:class|struct|record(?:\s+(?:class|struct))?)\s+([A-Za-z_]\w*)')
        if ($typeMatch.Success) { $declaredType = $typeMatch.Groups[1].Value; break }
    }
    if ($declaredType -match $rxSettingsTypeName) { continue }
    for ($i = 0; $i -lt $lines.Count; $i++) {
        foreach ($m in $rxStateBool.Matches($lines[$i])) {
            $name = $m.Groups['n'].Value
            if (-not $rxBoolPredicate.IsMatch($name)) { $boolIssues.Add(("{0}:{1}  [{2}] {3}" -f $rel, ($i + 1), $declaredType, $name)) }
        }
    }
}
Add-Result '状態の真偽値の命名' ($boolIssues.Count -eq 0) "$($boolIssues.Count) 件"
$boolIssues | Select-Object -First 10 | ForEach-Object { "      $_" }

# 8j) 命名と構造（規約が手作業に委ねていた項目を機械検査へ移したもの）
#     - テスト メソッドは internal（テスト規約。 インターフェースを実装するクラスは除外する）
#     - テスト クラス名はファイル名から `.Tests` を除き `.` を除去した名前（命名規約）
#     - 可搬な層のカテゴリ フォルダは単数形（命名規約）
#     - `async void` はイベント ハンドラーだけ（コーディング規約 §7）
$codeIssues = [System.Collections.Generic.List[string]]::new()
foreach ($f in @($allCs | Where-Object { $_.FullName -match '[\\/]tests[\\/]' })) {
    $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    $lines = Get-Lines $f.FullName
    # インターフェースを実装するクラスの行範囲（モック・スパイの public 実装を許すため）
    $ifaceClassStarts = [System.Collections.Generic.List[int]]::new()
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $classMatch = [regex]::Match($lines[$i], '(?<![\w.])class\s+[A-Za-z_]\w*\s*(?<rest>.*)$')
        if (-not $classMatch.Success) { continue }
        $decl = $classMatch.Groups['rest'].Value
        $j = $i
        while ($decl -notmatch '\{' -and $j + 1 -lt $lines.Count) { $j++; $decl += ' ' + $lines[$j] }
        if ($decl -match ':\s*[^[{]*\bI[A-Z]\w*') { $ifaceClassStarts.Add($i) }
    }
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -notmatch '^\s{4,}public\s+(?:async\s+)?(?:void|Task|ValueTask)\s+(?<n>[A-Za-z_]\w*)\s*\(') { continue }
        $inIfaceClass = $false
        foreach ($start in $ifaceClassStarts) { if ($i -gt $start) { $inIfaceClass = $true } }
        if (-not $inIfaceClass) { $codeIssues.Add(("{0}:{1}  テスト メソッドが public（internal にする）: {2}" -f $rel, ($i + 1), $Lines[$i].Trim())) }
    }
    $base = [IO.Path]::GetFileNameWithoutExtension($f.Name)
    if ($base -match '\.Tests$') {
        $expectedClass = (($base -replace '\.Tests$', '') -replace '\.', '') + 'Tests'
        $classNames = @([regex]::Matches((Get-Text $f.FullName), '(?m)^\s*(?:public|internal)\s+(?:sealed\s+)?class\s+([A-Za-z_]\w*)') | ForEach-Object { $_.Groups[1].Value })
        if ($classNames.Count -gt 0 -and $classNames -notcontains $expectedClass) {
            $codeIssues.Add(("{0}  テスト クラス名 [{1}] がファイル名から決まる [{2}] と一致しない" -f $rel, ($classNames -join ', '), $expectedClass))
        }
    }
}
foreach ($f in @($allCs | Where-Object { $_.FullName -notmatch '[\\/](tests|sample)[\\/]' })) {
    $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    $lines = Get-Lines $f.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $m = [regex]::Match($lines[$i], 'async\s+void\s+[A-Za-z_]\w*\s*\((?<p>[^)]*)\)')
        if (-not $m.Success) { continue }
        # イベント ハンドラーは `(object?, EventArgs 派生)` の 2 引数
        if ($m.Groups['p'].Value -match '^\s*object\??\s+\w+\s*,\s*\w+EventArgs\??\s+\w+\s*$') { continue }
        $codeIssues.Add(("{0}:{1}  イベント ハンドラー以外の `async void`（`async Task` にして失敗をログへ記録する）: {2}" -f $rel, ($i + 1), $lines[$i].Trim()))
    }
}
# 可搬な層のカテゴリ フォルダは単数形（型の集合を表すカテゴリは複数形でよい）
$allowedPluralFolders = @('Models', 'Services', 'Views', 'Enums', 'Controls', 'Extensions', 'Properties', 'Interop')
foreach ($projDir in @($rootPaths | ForEach-Object { Get-ChildItem -Path $_ -Directory } | Where-Object { Test-PortableProject $_.Name })) {
    foreach ($d in (Get-ChildItem -Path (Join-Path $projDir.FullName 'src') -Recurse -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' })) {
        if ($d.Name -cin $allowedPluralFolders) { continue }
        if ($d.Name -cmatch 's$' -and $d.Name -notmatch '(ss|us|is|as|ics|ous)$') {
            $codeIssues.Add(("{0}  カテゴリ フォルダが複数形（単数形にする。 型の集合を表すカテゴリは除く）" -f $d.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')))
        }
    }
}
Add-Result '命名と構造' ($codeIssues.Count -eq 0) "テストの可視性・クラス名・フォルダ・async void $($codeIssues.Count) 件"
$codeIssues | Select-Object -First 10 | ForEach-Object { "      $_" }

# 8j-2) フィールドとインターフェースの命名（[.editorconfig] の命名規則を機械検査へ写したもの）
#     命名規則（`dotnet_naming_rule.*`）は **ビルドでは報告されない**（`dotnet format` だけが検出する）。
#     検査を本スクリプトへ写すことで `dotnet format`（実測 20 秒）を既定の経路から外せる。
#     規則の所有者は [.editorconfig] であり、規則を足したら本検査も足す（ずれは本検査の不合格として現れる）。
$rxFieldDecl = [regex]'^\s*(?<access>private|private\s+protected|protected\s+private)\s+(?<mods>(?:(?:static|readonly|const|volatile|event|new|unsafe|required)\s+)+)?(?<type>[\w\.\<\>\[\]\?\,\s]+?)\s+(?<name>[A-Za-z_]\w*)\s*(?:=[^=>]|;|\{)'
$rxTypeKeyword = [regex]'\b(class|struct|record|interface|enum|delegate)\b'
$rxPascalCase = [regex]'^[A-Z]'
$rxCamelWithPrefix = [regex]'^_?[a-z][A-Za-z0-9]*$'
$rxInterfaceDecl = [regex]'^\s*(?:(?:public|internal|private|protected|file)\s+)*(?:partial\s+)?interface\s+(?<name>[A-Za-z_]\w*)'
$namingIssues = [System.Collections.Generic.List[string]]::new()
foreach ($f in $allCs) {
    $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    # 自動生成のファイル（`*.g.cs` / `AssemblyInfo.cs`）は対象外
    if ($f.Name -match '\.g\.cs$|AssemblyInfo\.cs$') { continue }
    $lines = Get-Lines $f.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        foreach ($m in $rxFieldDecl.Matches($line)) {
            $name = $m.Groups['name'].Value
            $type = $m.Groups['type'].Value
            # 型の宣言（入れ子の enum / class 等）と、式形式のメンバーはフィールドではない
            if ($rxTypeKeyword.IsMatch($type)) { continue }
            if ($line -match '=>') { continue }
            # 型と名前の境目が取れない行（メソッド・プロパティ・式）は対象外にする
            if ($name -in @('get', 'set', 'init', 'return', 'new', 'if', 'else', 'while', 'value')) { continue }
            $mods = $m.Groups['mods'].Value
            if ($mods -match '\bconst\b') {
                if (-not $rxPascalCase.IsMatch($name)) {
                    $namingIssues.Add(("{0}:{1}  const フィールド '{2}' は PascalCase にする（.editorconfig）" -f $rel, ($i + 1), $name))
                }
            }
            elseif ($mods -match '\bstatic\b') {
                if ($name -notmatch '^s_[a-z]') {
                    $namingIssues.Add(("{0}:{1}  private static フィールド '{2}' は 's_' + camelCase にする（.editorconfig）" -f $rel, ($i + 1), $name))
                }
            }
            elseif (-not $rxCamelWithPrefix.IsMatch($name) -or $name -notmatch '^_') {
                $namingIssues.Add(("{0}:{1}  private フィールド '{2}' は '_' + camelCase にする（.editorconfig）" -f $rel, ($i + 1), $name))
            }
        }

        foreach ($m in $rxInterfaceDecl.Matches($line)) {
            $name = $m.Groups['name'].Value
            if ($name -notmatch '^I[A-Z]') {
                $namingIssues.Add(("{0}:{1}  interface '{2}' は 'I' + PascalCase にする（.editorconfig）" -f $rel, ($i + 1), $name))
            }
        }
    }
}
Add-Result 'フィールドとインターフェースの命名' ($namingIssues.Count -eq 0) "$($namingIssues.Count) 件"
$namingIssues | Select-Object -First 10 | ForEach-Object { "      $_" }

# 8k) イベントの発火は Raise ヘルパーへ集約する（`event` へ `?.Invoke` を書かない。 コーディング規約 §7）
$rxEventDecl = [regex]'(?<![\w.])(?:public|internal|private|protected)\s+(?:static\s+)?event\s+(?:EventHandler(?:<[^>]+>)?|\w+(?:<\w+>)?)\s+(?<n>[A-Za-z_]\w*)'
$invokeIssues = [System.Collections.Generic.List[string]]::new()
foreach ($f in @($allCs | Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' })) {
    $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    $text = Get-Text $f.FullName
    $eventNames = @($rxEventDecl.Matches($text) | ForEach-Object { $_.Groups['n'].Value })
    if ($eventNames.Count -eq 0) { continue }
    $lines = Get-Lines $f.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        foreach ($n in $eventNames) {
            if ($lines[$i] -match ("(?<![\w.])" + [regex]::Escape($n) + "\?\.Invoke\(")) { $invokeIssues.Add(("{0}:{1}  {2}" -f $rel, ($i + 1), $lines[$i].Trim())) }
        }
    }
}
Add-Result 'イベントの発火' ($invokeIssues.Count -eq 0) "event への ?.Invoke $($invokeIssues.Count) 件（Raise ヘルパーへ集約する）"
$invokeIssues | Select-Object -First 10 | ForEach-Object { "      $_" }

# 8l) ログ定義のドキュメントコメントへレベルと出力書式を書く（ログ規約 §5）
$logDocIssues = [System.Collections.Generic.List[string]]::new()
foreach ($f in @($allCs | Where-Object { $_.FullName -notmatch '[\\/](obj|bin|tests|sample)[\\/]' })) {
    $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    $lines = Get-Lines $f.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -notmatch '\[LoggerMessage\(') { continue }
        $doc = [System.Collections.Generic.List[string]]::new()
        for ($j = $i - 1; $j -ge 0 -and $lines[$j].TrimStart().StartsWith('///'); $j--) { $doc.Insert(0, $lines[$j]) }
        $text = $doc -join "`n"
        if ($text -notmatch '(LogLevel|ログレベル):') { $logDocIssues.Add(("{0}:{1}  ログレベルが doc にない" -f $rel, ($i + 1))) }
        if ($text -notmatch '出力書式:') { $logDocIssues.Add(("{0}:{1}  出力書式が doc にない" -f $rel, ($i + 1))) }
    }
}
Add-Result 'ログ定義の doc' ($logDocIssues.Count -eq 0) "レベル・出力書式の記載漏れ $($logDocIssues.Count) 件"
$logDocIssues | Select-Object -First 10 | ForEach-Object { "      $_" }

# 8m) コードの抑止は ビルド設定規約.md の「付録 A 抑止の一覧」にあるものだけ（規約が例外の所有者）
# 規則文書が無いリポジトリ（規則をまだ持たない）では、この検査だけを省略する（他の検査は動かす）。
$buildRulesPath = Join-Path $repoRoot '.github/ビルド設定規約.md'
$buildRules = if (Test-Path -LiteralPath $buildRulesPath) { [IO.File]::ReadAllText($buildRulesPath, $enc) } else { $null }
$suppressSites = [System.Collections.Generic.List[object]]::new()
# 列挙は 8j で作った一覧を再利用する（`-Include` は obj/bin まで全ファイルを走査してから絞るため遅い。 実測 6 秒 → 0.5 秒）
foreach ($f in @($allCs + $allCsproj)) {
    $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    $lines = Get-Lines $f.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        # 抑止の記述が無い行は正規表現を走らせない（実測: 2.5 秒 → 1 秒未満）
        if ($lines[$i].IndexOf('pragma') -lt 0 -and $lines[$i].IndexOf('SuppressMessage') -lt 0 -and $lines[$i].IndexOf('NoWarn') -lt 0) { continue }
        $m = [regex]::Match($lines[$i], '#pragma\s+warning\s+disable\s+(?<c>[A-Z]+\d+)')
        if ($m.Success) { $suppressSites.Add([pscustomobject]@{ File = $rel; Line = $i + 1; Code = $m.Groups['c'].Value }) }
        $m = [regex]::Match($lines[$i], 'SuppressMessage\([^)]*"(?<c>[A-Z]+\d+)"')
        if ($m.Success) { $suppressSites.Add([pscustomobject]@{ File = $rel; Line = $i + 1; Code = $m.Groups['c'].Value }) }
        $m = [regex]::Match($lines[$i], '<NoWarn>(?<c>[^<]+)</NoWarn>')
        if ($m.Success) { foreach ($c in ($m.Groups['c'].Value -split '[;,]')) { if ($c.Trim() -match '^[A-Z]+\d+$') { $suppressSites.Add([pscustomobject]@{ File = $rel; Line = $i + 1; Code = $c.Trim() }) } } }
    }
}
$unlisted = @($suppressSites | Where-Object { $buildRules -notmatch [regex]::Escape($_.Code) -or $buildRules -notmatch [regex]::Escape($_.File) })
if ($null -eq $buildRules) {
    Add-Info '抑止の一覧' "省略（.github/ビルド設定規約.md が無い。 規則文書を持つリポジトリだけが検査する）"
}
else {
    Add-Result '抑止の一覧' ($unlisted.Count -eq 0) "付録 A にない抑止 $($unlisted.Count) 件 / 抑止 $($suppressSites.Count) 件"
}
$unlisted | Select-Object -First 10 | ForEach-Object { "      {0}:{1}  {2}（付録 A へ理由付きで追加する）" -f $_.File, $_.Line, $_.Code }

# 8n) 文書の H1（ドキュメント規約 §2。 対象はリポジトリ直下と src/ 配下の文書）
$h1Issues = [System.Collections.Generic.List[string]]::new()
$docFiles = @(@(Get-ChildItem -Path $repoRoot -File -Filter '*.md') + @(Get-ChildItem -Path $rootPaths -Recurse -File -Filter '*.md'))
foreach ($f in @($docFiles | Where-Object { $_.FullName -notmatch '[\\/](obj|bin|archive)[\\/]' })) {
    $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    if ($rel -eq "$($rootNames[0])/README.md") { continue }
    $h1 = $null
    foreach ($l in (Get-Lines $f.FullName)) { if ($l.StartsWith('# ')) { $h1 = $l.Substring(2).Trim(); break } }
    $expected = $null
    # ルートの H1 は製品名（リポジトリごとに違うため release-config.json から取る）
    if ($rel -eq 'README.md') { $expected = $productName }
    elseif ($rel -match "^(?<root>$rootRx)/(?<p>[^/]+)/docs/SPEC_(?<k>[A-Za-z]+)\.md$") {
        $title = Get-ProjectTitle $Matches['root'] $Matches['p']
        $kind = @{ 'API' = 'API リファレンス'; 'System' = 'システム仕様書'; 'ExtFunc' = '外部機能仕様書' }[$Matches['k']]
        if ($kind) { $expected = "$title $kind" }
        elseif ($h1 -notlike "$title *") { $h1Issues.Add(("{0}  H1 [{1}] が「{2} <用途名>」の形でない" -f $rel, $h1, $title)) }
        continue
    }
    elseif ($rel -match "^(?<root>$rootRx)/(?<p>[^/]+)/sample/README\.md$") { $expected = "$(Get-ProjectTitle $Matches['root'] $Matches['p']) サンプルアプリケーション" }
    # UI の README の H1 は「プロジェクト フォルダ名」か「製品名（UI 名）」のどちらでもよい
    # （プロジェクト名をそのまま見出しにするリポジトリと、製品名＋UI 名にするリポジトリがある）。
    elseif ($rel -match "^(?<root>$rootRx)/(?<p>[^/]+)/README\.md$") {
        $title = Get-ProjectTitle $Matches['root'] $Matches['p']
        if ($h1 -and ($h1 -eq $Matches['p'] -or $h1 -eq $title -or $h1 -like "$productName*")) { continue }
        $expected = $title
    }
    if ($null -eq $expected) { continue }
    if ($null -eq $h1) { $h1Issues.Add(("{0}  H1 がない" -f $rel)) }
    elseif ($h1 -ne $expected) { $h1Issues.Add(("{0}  H1 [{1}] / 期待 [{2}]" -f $rel, $h1, $expected)) }
}
Add-Result '文書の H1' ($h1Issues.Count -eq 0) "$($h1Issues.Count) 件"
$h1Issues | Select-Object -First 10 | ForEach-Object { "      $_" }

# 8o) 禁止した識別子（命名規約: 略語・短縮語 / 座標系の禁止語）
#     宣言された名前（型・メンバー）のセグメントが禁止語と一致しないことを見る。
#     フレームワークが定める名前（MewUI の OnPaintSurface 等）は許容する。
$forbiddenSegments = @('Msg', 'Btn', 'Ctx', 'Svc', 'Impl', 'Mgr', 'Tmp', 'Legacy', 'Deprecated', 'Domain', 'Surface')
$frameworkNames = @('OnPaintSurface')
$rxSplit = [regex]'(?<=[a-z0-9])(?=[A-Z])|_'
$rxDeclPatterns = @(
    [regex]'(?<![\w.])(?:class|struct|interface|enum)\s+(?<n>[A-Za-z_]\w*)',
    [regex]'(?<![\w.])(?:record(?:\s+(?:class|struct))?)\s+(?<n>[A-Za-z_]\w*)',
    [regex]'(?m)^\s{4}(?:public|internal|private|protected)\s+(?:static\s+|readonly\s+|sealed\s+|override\s+|virtual\s+|async\s+|partial\s+|unsafe\s+|new\s+|required\s+|volatile\s+)*[\w<>,\[\]\?\.]+\s+(?<n>[A-Za-z_]\w*)\s*(?:\(|\{|=>|;|=)'
)
$identIssues = [System.Collections.Generic.List[string]]::new()
foreach ($f in $allCs) {
    $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    $lines = Get-Lines $f.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*(//|///|\*)') { continue }
        foreach ($rx in $rxDeclPatterns) {
            foreach ($m in $rx.Matches($lines[$i])) {
                $name = $m.Groups['n'].Value
                if ($frameworkNames -contains $name) { continue }
                foreach ($seg in ($rxSplit.Split($name))) {
                    if ($forbiddenSegments -contains $seg) {
                        $identIssues.Add(("{0}:{1}  [{2} in {3}]（略語・座標系の禁止語。 命名規約）" -f $rel, ($i + 1), $seg, $name))
                    }
                }
            }
        }
    }
}
Add-Result '禁止した識別子' ($identIssues.Count -eq 0) "略語・禁止語 $($identIssues.Count) 件"
$identIssues | Select-Object -First 10 | ForEach-Object { "      $_" }

# 8p) 例外の扱い（例外処理規約 §4: 捕捉したら記録する / 空の catch を作らない）
#     記録とは「ログ・Debug/Trace 出力・理由コメント」のいずれかである。
$catchIssues = [System.Collections.Generic.List[string]]::new()
foreach ($f in $allCs) {
    $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    $lines = Get-Lines $f.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*(//|///|\*)') { continue }
        if ($lines[$i] -notmatch '\bcatch\b') { continue }
        # catch のブロックを集める
        $depth = 0
        $start = $i
        $j = $i
        while ($j -lt $lines.Count -and $lines[$j] -notmatch '\{') { $j++ }
        $block = [System.Collections.Generic.List[string]]::new()
        for ($k = $j; $k -lt $lines.Count; $k++) {
            $block.Add($lines[$k])
            $depth += ([regex]::Matches($lines[$k], '\{')).Count - ([regex]::Matches($lines[$k], '\}')).Count
            if ($depth -le 0) { break }
        }
        $body = $block -join "`n"
        # 波かっこの中身（`{}` の中が空かどうか）
        $inner = [regex]::Match($body, '\{(?<inner>.*)\}', [Text.RegularExpressions.RegexOptions]::Singleline).Groups['inner'].Value
        $inner = [regex]::Replace($inner, '(?m)^\s*//.*$', '')   # コメント行を除く
        if ($inner.Trim().Length -eq 0 -and $body -notmatch '//') {
            $catchIssues.Add(("{0}:{1}  空の catch（理由コメントを書くか、記録する）" -f $rel, ($start + 1)))
            continue
        }
        if ($body -notmatch '\.Log[A-Z]|Log[A-Z]\w*\(|Debug\.WriteLine|Trace\.WriteLine|Console\.' -and $body -notmatch '//') {
            $catchIssues.Add(("{0}:{1}  捕捉した例外を記録していない（ログ・Debug 出力・理由コメントのいずれかを書く）" -f $rel, ($start + 1)))
        }
    }
}
Add-Result '例外の記録' ($catchIssues.Count -eq 0) "記録のない catch $($catchIssues.Count) 件"
$catchIssues | Select-Object -First 10 | ForEach-Object { "      $_" }

# 8q) イベント ハンドラーの登録は XAML とコードのどちらか一方（コーディング規約 §7）
$wiringIssues = [System.Collections.Generic.List[string]]::new()
foreach ($projDir in (Get-ChildItem -Path $rootPaths -Directory)) {
    $axamlFiles = @(Get-ChildItem -Path $projDir.FullName -Recurse -File -Filter '*.axaml' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' })
    if ($axamlFiles.Count -eq 0) { continue }
    $csFiles = @(Get-ChildItem -Path $projDir.FullName -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' })
    $csText = ($csFiles | ForEach-Object { Get-Text $_.FullName }) -join "`n"
    $handlerNames = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($x in $axamlFiles) {
        foreach ($m in [regex]::Matches((Get-Text $x.FullName), '="(?<n>On[A-Z]\w+)"')) { [void]$handlerNames.Add($m.Groups['n'].Value) }
    }
    foreach ($n in $handlerNames) {
        if ($csText -match ('\+=\s*' + [regex]::Escape($n) + '\b')) {
            $wiringIssues.Add(("{0}  {1} を XAML とコードの両方で登録している（どちらか一方に統一する）" -f $projDir.Name, $n))
        }
    }
}
Add-Result 'ハンドラーの配線' ($wiringIssues.Count -eq 0) "$($wiringIssues.Count) 件"
$wiringIssues | Select-Object -First 10 | ForEach-Object { "      $_" }

# 8r) README の必須節（ドキュメント規約 §3。 番号は spec の体裁検査が担保する）
#     必須: 概要（または目的）/ 使い方（クイックスタート・実行・インストール）/ テスト（または検証）/
#           ドキュメント（または関連ドキュメント）/ ライセンス（末尾）
$readmeRequired = @(
    [pscustomobject]@{ Name = '概要または目的'; Pattern = '^(概要|目的)$' },
    [pscustomobject]@{ Name = '使い方'; Pattern = '(クイックスタート|実行|インストール|使い方|主な API|使用例)' },
    [pscustomobject]@{ Name = 'テストまたは検証'; Pattern = '(テスト|検証)' },
    [pscustomobject]@{ Name = 'ドキュメント'; Pattern = '(ドキュメント|SPEC_)' },
    [pscustomobject]@{ Name = 'ライセンス'; Pattern = 'ライセンス' }
)
$readmeIssues = [System.Collections.Generic.List[string]]::new()
foreach ($f in @(Get-ChildItem -Path $rootPaths -Recurse -File -Filter 'README.md' |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin|sample)[\\/]' })) {
    $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    # `src/README.md` は層構成の文書（ドキュメント規約 §2 の例外）
    if ($rel -eq "$($rootNames[0])/README.md") { continue }
    $headings = @(Get-Lines $f.FullName |
        Where-Object { $_ -match '^#{2,3} ' } |
        ForEach-Object { ($_ -replace '^#{2,3}\s*', '') -replace '^\d+(\.\d+)*\.?\s*', '' })
    foreach ($req in $readmeRequired) {
        if (@($headings | Where-Object { $_ -match $req.Pattern }).Count -eq 0) {
            $readmeIssues.Add(("{0}  「{1}」の節がない（ドキュメント規約 §3）" -f $rel, $req.Name))
        }
    }
    # ライセンスは末尾に置く
    if ($headings.Count -gt 0 -and $headings[$headings.Count - 1] -notmatch 'ライセンス') {
        $readmeIssues.Add(("{0}  ライセンスの節が末尾でない（末尾は「{1}」）" -f $rel, $headings[$headings.Count - 1]))
    }
}
Add-Result 'README の必須節' ($readmeIssues.Count -eq 0) "$($readmeIssues.Count) 件"
$readmeIssues | Select-Object -First 10 | ForEach-Object { "      $_" }

# 8s) SPEC_System の必須節（ドキュメント規約 §6。 節の名前は同義語を許す）
$sysRequired = @(
    [pscustomobject]@{ Name = '概要'; Pattern = '(概要|目的)' },
    [pscustomobject]@{ Name = '技術スタック'; Pattern = '(技術スタック|依存パッケージ)' },
    [pscustomobject]@{ Name = 'アーキテクチャ'; Pattern = '(アーキテクチャ|構成図)' },
    [pscustomobject]@{ Name = 'コンポーネント'; Pattern = '(コンポーネント|クラス図|モジュール)' },
    [pscustomobject]@{ Name = '依存関係'; Pattern = '(依存|参照)' },
    [pscustomobject]@{ Name = '例外処理ポリシー'; Pattern = '(例外|エラー)' }
)
$sysIssues = [System.Collections.Generic.List[string]]::new()
foreach ($f in @(Get-ChildItem -Path $rootPaths -Recurse -File -Filter 'SPEC_System.md' |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' })) {
    $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    $headings = @(Get-Lines $f.FullName |
        Where-Object { $_ -match '^#{2,3} ' } |
        ForEach-Object { ($_ -replace '^#{2,3}\s*', '') -replace '^\d+(\.\d+)*\.?\s*', '' })
    foreach ($req in $sysRequired) {
        if (@($headings | Where-Object { $_ -match $req.Pattern }).Count -eq 0) {
            $sysIssues.Add(("{0}  「{1}」の節がない（ドキュメント規約 §6）" -f $rel, $req.Name))
        }
    }
}
Add-Result 'SPEC_System の必須節' ($sysIssues.Count -eq 0) "$($sysIssues.Count) 件"
$sysIssues | Select-Object -First 10 | ForEach-Object { "      $_" }

# 8t) テストの `{対象}` の実在（テスト規約 §2。 削除・改名した対象のテストを残さない）
#     `{対象}` は実在のソース（ファイル名または型名）と一致するか、その名前で始まる（`<対象><観点>` を許す）。
$rxTestFileName = [regex]'(?<target>.+)\.Tests\.cs$'
$testTargetIssues = [System.Collections.Generic.List[string]]::new()
foreach ($projDir in (Get-ChildItem -Path $rootPaths -Directory)) {
    $srcDir = Join-Path $projDir.FullName 'src'
    if (-not (Test-Path -LiteralPath $srcDir)) { continue }
    # ソースの型名・ファイル名の集合
    $srcNames = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($cs in (Get-ChildItem -Path $srcDir -Recurse -File -Filter '*.cs' | Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' })) {
        $name = [IO.Path]::GetFileNameWithoutExtension($cs.Name)
        foreach ($part in $name.Split('.')) { [void]$srcNames.Add($part) }
        foreach ($m in [regex]::Matches((Get-Text $cs.FullName), '(?m)^\s*(?:(?:public|internal|private|protected|file)\s+)*(?:static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+|ref\s+)*(?:class|struct|interface|enum|record(?:\s+(?:class|struct))?)\s+([A-Za-z_]\w*)')) {
            [void]$srcNames.Add($m.Groups[1].Value)
        }
    }
    foreach ($t in (Get-ChildItem -Path (Join-Path $projDir.FullName 'tests') -Recurse -File -Filter '*.Tests.cs' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' })) {
        $rel = $t.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
        $m = $rxTestFileName.Match($t.Name)
        if (-not $m.Success) { continue }
        $target = $m.Groups['target'].Value
        # 対象の先頭の語（`CaptureTargetContract` → `Capture`）。 `<対象><観点>` と、複数の型をまたぐ検証を許す。
        $first = ([regex]::Match($target, '^([A-Z][a-z0-9]*|[A-Z]+)')).Value
        $matched = $false
        foreach ($n in $srcNames) {
            if ($target -eq $n -or
                $target.StartsWith($n, [StringComparison]::Ordinal) -or
                $n.StartsWith($target, [StringComparison]::Ordinal) -or
                ($first.Length -gt 0 -and $n.StartsWith($first, [StringComparison]::Ordinal))) { $matched = $true; break }
        }
        if (-not $matched) { $testTargetIssues.Add(("{0}  `{{対象}}` [{1}] に対応するソースがない" -f $rel, $target)) }
    }
}
Add-Result 'テストの対象の実在' ($testTargetIssues.Count -eq 0) "$($testTargetIssues.Count) 件"
$testTargetIssues | Select-Object -First 10 | ForEach-Object { "      $_" }

# 8u) SPEC_API の internal 型には印を付ける（ドキュメント規約 §4）
#     型の節（`## N. `Type`` / `### N.M `Type``）の型がソースで `internal` なら、見出しに「（内部」が要る。
$rxTypeHeading = [regex]'^#{2,3} \d+(?:\.\d+)*\.?\s*(?:`(?<t>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)`|(?<t2>[A-Za-z_]\w*))'
$internalIssues = [System.Collections.Generic.List[string]]::new()
foreach ($f in @(Get-ChildItem -Path $rootPaths -Recurse -File -Filter 'SPEC_API.md' |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' })) {
    $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    $projectDir = (Get-Item $f.FullName).Directory.Parent.FullName
    $srcDir = Join-Path $projectDir 'src'
    if (-not (Test-Path -LiteralPath $srcDir)) { continue }
    # そのプロジェクトの internal 型の集合
    $internalTypes = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($cs in (Get-ChildItem -Path $srcDir -Recurse -File -Filter '*.cs' | Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' })) {
        foreach ($m in [regex]::Matches((Get-Text $cs.FullName), '(?m)^\s*internal\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+|ref\s+)*(?:class|struct|interface|enum|record(?:\s+(?:class|struct))?)\s+([A-Za-z_]\w*)')) {
            [void]$internalTypes.Add($m.Groups[1].Value)
        }
    }
    foreach ($line in (Get-Lines $f.FullName)) {
        $m = $rxTypeHeading.Match($line)
        if (-not $m.Success) { continue }
        $name = if ($m.Groups['t'].Success) { $m.Groups['t'].Value } else { $m.Groups['t2'].Value }
        $last = ($name -split '\.')[-1]
        if (-not $internalTypes.Contains($last)) { continue }
        # 印は「（内部」または「`internal`」のいずれか（公開面と取り違えないための明示）
        if ($line -notmatch '（内部' -and $line -notmatch '`internal`') { $internalIssues.Add(("{0}  internal 型 `{1}` の節に「（内部）」の印がない" -f $rel, $last)) }
    }
}
Add-Result 'SPEC_API の internal 印' ($internalIssues.Count -eq 0) "$($internalIssues.Count) 件"
$internalIssues | Select-Object -First 10 | ForEach-Object { "      $_" }

# 8v) ワークフローの失敗の握りつぶし（例外の記録（8p）と同じ考え方。 規則は .github/検証手順.md）
#     `run:` の中で失敗を無視すると、**検証が通ったように見える**（実測 2026-11-06: 展開済みキャッシュの
#     削除に失敗しても続行したため、古いパッケージで検証が通った）。 失敗は隠さず、ワークフローを止める。
$rxSuppressFailure = [regex]'(?i)-ErrorAction\s+(SilentlyContinue|Ignore)|\|\|\s*true(?=\s|$)|2>\s*\$null|continue-on-error:\s*true'
$workflowIssues = [System.Collections.Generic.List[string]]::new()
foreach ($f in @(Get-ChildItem -Path (Join-Path $repoRoot '.github/workflows') -File -Filter '*.yml' -ErrorAction SilentlyContinue)) {
    $lines = Get-Lines $f.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i].TrimStart().StartsWith('#')) { continue }
        if ($lines[$i] -match $rxSuppressFailure) {
            $workflowIssues.Add(("{0}:{1}  失敗を握りつぶしている（{2}）。 失敗させて止めるか、理由を書いて別の手段で確かめる" -f $f.Name, ($i + 1), $lines[$i].Trim()))
        }
    }
}
Add-Result 'ワークフローの失敗の握りつぶし' ($workflowIssues.Count -eq 0) "$($workflowIssues.Count) 件"
$workflowIssues | Select-Object -First 10 | ForEach-Object { "      $_" }

# ============================================================
# 9. 改行コード
# ============================================================
Write-Output '== 9. 改行コード =='
# **作業ツリーの改行は OS で異なる**（Windows は `.editorconfig` の CRLF、Linux は LF）。
# リポジトリの内容そのものの混在（index）は OS に依存しないため、Windows 以外では index を見る
# （CI の docs ジョブは ubuntu で走る）。
$eolPattern = if ($IsWindows) { 'w/mixed|w/lf' } else { 'i/mixed|w/mixed' }
$eolIssues = @(Get-GitList @('ls-files', '--eol') | Select-String $eolPattern)
Add-Result '改行コード' (@($eolIssues).Count -eq 0) "$(@($eolIssues).Count) 件"
$eolIssues | Select-Object -First 5 | ForEach-Object { "      $($_.Line)" }

# 9b) バッチ ファイルの文字コード（ASCII のみ。 `cmd.exe` は読み取り位置を復号後の文字数で数えるため、
#      非 ASCII 文字は読み取りをずらし、出力の化けとコメント断片の実行を招く。 根拠は §19）
$batIssues = [System.Collections.Generic.List[string]]::new()
foreach ($f in (Get-GitList @('-c', 'core.quotepath=false', 'ls-files'))) {
    if ($f -notmatch '\.bat$') { continue }
    $path = Join-Path $repoRoot $f
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $bytes = [IO.File]::ReadAllBytes($path)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -gt 0x7F) {
            $batIssues.Add(("{0}: 非 ASCII バイト（0x{1:X2}）。 バッチ ファイルは ASCII だけで書く" -f $f.Replace('\', '/'), $bytes[$i]))
            break
        }
    }
}
Add-Result 'バッチの文字コード' ($batIssues.Count -eq 0) "$($batIssues.Count) 件"
$batIssues | Select-Object -First 5 | ForEach-Object { "      $_" }

# ============================================================
# 10. フォルダ（空フォルダ / 孤立した bin・obj）
# ============================================================
Write-Output '== 10. フォルダ =='
# .gitignore が無視するパスは検査の対象外にする（リポジトリの構造を成さないため。 実測: C# Dev Kit が作る
# src/obj を孤立 bin・obj と誤認していた）。 入れ子の .gitignore と否定規則の解釈は git に委ねる。
$ignoredDirs = @(Get-GitList @('ls-files', '--others', '--ignored', '--exclude-standard', '--directory') |
        Where-Object { $_ -match '/$' } |
        ForEach-Object { Join-Path $repoRoot ($_.TrimEnd('/') -replace '/', '\') })
function Test-IgnoredFolder([string]$Folder) {
    foreach ($ignored in $ignoredDirs) {
        if ($Folder.Equals($ignored, [StringComparison]::OrdinalIgnoreCase)) { return $true }
        if ($Folder.StartsWith("$ignored\", [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}
# ビルド出力の有無（`bin` / `obj` の残骸の判定に使う。 ツールが置くだけのフォルダを残骸と見なさない）
$rxBuildOutput = [regex]'^(project\.assets\.json|.+\.nuget\.g\.(props|targets)|.+\.csproj\.nuget\.dgspec\.json)$'
function Test-BuildOutput([string]$Folder) {
    return @(Get-ChildItem -LiteralPath $Folder -Recurse -File -Force -ErrorAction SilentlyContinue |
            Where-Object { $rxBuildOutput.IsMatch($_.Name) -or $_.Extension -in @('.dll', '.exe', '.pdb', '.cache') }).Count -gt 0
}
$emptyDirs = @(Get-ChildItem -Path $rootPaths -Recurse -Directory |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } |
        Where-Object { -not (Test-IgnoredFolder $_.FullName) } |
        Where-Object { @(Get-ChildItem -LiteralPath $_.FullName -Force).Count -eq 0 })
# 孤立した bin/obj は「親に csproj が無い」ことで判定する（親を毎回列挙せず、csproj を持つフォルダーの集合と照合する）。
# 無視されるフォルダは対象外にするが、`bin` / `obj` の残骸だけは例外とする（ビルド出力は .gitignore に載るのが
# 前提であり、無視されることを理由にすべて外すと検査が成立しない）。 残骸と見なすのはビルド出力を含むものに限る。
$csprojDirs = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($csproj in $allCsproj) { [void]$csprojDirs.Add([IO.Path]::GetDirectoryName($csproj.FullName)) }
$orphanBins = @(Get-ChildItem -Path $rootPaths -Recurse -Directory |
        Where-Object { $_.Name -in @('bin', 'obj') } |
        Where-Object { -not $csprojDirs.Contains([IO.Path]::GetDirectoryName($_.FullName)) } |
        Where-Object { -not (Test-IgnoredFolder $_.FullName) -or (Test-BuildOutput $_.FullName) })
Add-Result 'フォルダ' ($emptyDirs.Count -eq 0 -and $orphanBins.Count -eq 0) "空 $($emptyDirs.Count) / 孤立 bin・obj $($orphanBins.Count)"
$emptyDirs | Select-Object -First 5 | ForEach-Object { "      $($_.FullName.Substring($repoRoot.Length + 1))" }
$orphanBins | Select-Object -First 5 | ForEach-Object { "      $($_.FullName.Substring($repoRoot.Length + 1))" }

# 10a) ソリューションの網羅（起点フォルダ直下のプロジェクトは、すべてそのソリューションに入れる）
#      入れ忘れると、ビルド・テスト・パッケージ作成が静かにそのプロジェクトを飛ばす
#      （実測 2026-10-26: Interop が 4 つの UI ソリューションから漏れ、テストは 26 プロジェクト・
#       パッケージは 26 個しか作られなかった）。 規則は .github/命名規約.md §2。
$solutionFiles = @(if ($repoConfig.solutions) { $repoConfig.solutions } elseif ($repoConfig.solutionFile) { $repoConfig.solutionFile })
$solutionMissing = [System.Collections.Generic.List[string]]::new()
foreach ($solution in $solutionFiles) {
    $slnxPath = Join-Path $repoRoot $solution
    if (-not (Test-Path -LiteralPath $slnxPath)) { $solutionMissing.Add("$solution がありません"); continue }
    $slnxDir = Split-Path -Parent $slnxPath
    # ソリューションが起点フォルダの外（リポジトリ ルート）にある場合は、起点フォルダ全体を対象にする
    $scopeDirs = if ($slnxDir -eq $repoRoot) { $rootPaths } else { @($slnxDir) }
    $declared = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($m in [regex]::Matches((Get-Text $slnxPath), 'Project\s+Path="(?<p>[^"]+\.csproj)"')) {
        [void]$declared.Add([IO.Path]::GetFullPath((Join-Path $slnxDir $m.Groups['p'].Value)))
    }
    foreach ($scopeDir in $scopeDirs) {
        foreach ($csproj in @(Get-ChildItem -Path $scopeDir -Recurse -File -Filter '*.csproj' |
                Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' })) {
            if (-not $declared.Contains($csproj.FullName)) {
                $solutionMissing.Add($csproj.FullName.Substring($repoRoot.Length + 1))
            }
        }
    }
}
Add-Result 'ソリューションの網羅' ($solutionMissing.Count -eq 0) "未登録 $($solutionMissing.Count) 件"
$solutionMissing | Select-Object -First 10 | ForEach-Object { "      $_（.slnx へ追加する）" }

# 10b) テスト ファイル名（`.` を挟まない `{対象}Tests.cs` を使わない）
#      ファイル名は `{対象}.Tests.cs`、クラス名は `{対象}Tests` とする（命名規約）。 クラス名だけから
#      作った `{対象}Tests.cs` は、ファイル名とクラス名の対応が崩れる（実測: 17 ファイルを統一した）
$testNameIssues = [System.Collections.Generic.List[string]]::new()
foreach ($f in (Get-GitList @('-c', 'core.quotepath=false', 'ls-files'))) {
    if ($f -notmatch '/tests/' -or $f -notmatch '\.cs$') { continue }
    $name = [IO.Path]::GetFileName($f)
    if (-not $name.EndsWith('Tests.cs')) { continue }
    if ($name.EndsWith('.Tests.cs')) { continue }
    $testNameIssues.Add($f)
}
Add-Result 'テストの命名' ($testNameIssues.Count -eq 0) "`{対象}Tests.cs $($testNameIssues.Count) 件"
$testNameIssues | Select-Object -First 10 | ForEach-Object { "      $_" }

# 10d) XAML の名前空間宣言（宣言した `xmlns:接頭辞` が使われているか）
#      XAML の名前空間宣言は、その接頭辞を書かなくなった時点で不要になる（エディタは
#      「名前空間宣言が不要」と指摘するが、ビルドは通るため機械検査が要る）。
#      宣言そのもの（`xmlns:接頭辞="..."`）を取り除いた残りで `接頭辞:` が使われているかを見る。
$xamlNamespaceIssues = [System.Collections.Generic.List[string]]::new()
foreach ($f in (Get-ChildItem -Path $rootPaths -Recurse -File -Filter '*.axaml' |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' })) {
    $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
    $lines = Get-Lines $f.FullName
    $prefixes = [System.Collections.Generic.List[string]]::new()
    foreach ($line in $lines) {
        foreach ($m in [regex]::Matches($line, 'xmlns:([A-Za-z_]\w*)\s*=')) {
            if (-not $prefixes.Contains($m.Groups[1].Value)) { $prefixes.Add($m.Groups[1].Value) }
        }
    }
    foreach ($prefix in $prefixes) {
        $rxDeclaration = [regex]("xmlns:$([regex]::Escape($prefix))\s*=\s*""[^""]*""")
        $rxUse = [regex]("(?<![\w:])$([regex]::Escape($prefix)):")
        $used = $false
        foreach ($line in $lines) {
            if ($rxUse.IsMatch($rxDeclaration.Replace($line, ''))) { $used = $true; break }
        }
        if (-not $used) { $xamlNamespaceIssues.Add(("{0}  未使用の名前空間宣言: {1}" -f $rel, $prefix)) }
    }
}
Add-Result 'XAML の名前空間' ($xamlNamespaceIssues.Count -eq 0) "未使用の宣言 $($xamlNamespaceIssues.Count) 件"
$xamlNamespaceIssues | Select-Object -First 10 | ForEach-Object { "      $_" }

# 10c) ソースとテストの対応（参考値。 合否に含めない）
#      テストはソースのフォルダを鏡映し、専用ファイルの名前を `{ソース名}.Tests.cs` とする。 ただし
#      専用ファイルを持つかは検証の単位で決めるため（公開 API 経由で網羅する型は持たなくてよい）、
#      合否にはせず件数だけを表示する
$testPairs = 0
$testTotal = 0
foreach ($proj in (Get-ChildItem -Path $rootPaths -Directory)) {
    $srcDir = Join-Path $proj.FullName 'src'
    $testDir = Join-Path $proj.FullName 'tests'
    if (-not (Test-Path -LiteralPath $srcDir) -or -not (Test-Path -LiteralPath $testDir)) { continue }
    foreach ($f in (Get-ChildItem -Path $srcDir -Recurse -File -Filter '*.cs' | Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' })) {
        $testTotal++
        $rel = $f.FullName.Substring($srcDir.Length + 1)
        $fileName = [IO.Path]::GetFileNameWithoutExtension($rel) + '.Tests.cs'
        $dir = [IO.Path]::GetDirectoryName($rel)
        $expected = if ([string]::IsNullOrEmpty($dir)) { Join-Path $testDir $fileName } else { Join-Path (Join-Path $testDir $dir) $fileName }
        if (Test-Path -LiteralPath $expected) { $testPairs++ }
    }
}
Add-Info 'ソースとテストの対応' "専用ファイルあり $testPairs / ソース $testTotal（参考値。 公開 API 経由で検証する型は専用ファイルを持たない）"

# ============================================================
# 走査の終わり（入口が本行を見て、走査が完走したかを判定する）
# ============================================================
$failed = @($script:Results | Where-Object { $_.Kind -eq 'Check' -and -not $_.Ok })
$checks = @($script:Results | Where-Object { $_.Kind -eq 'Check' }).Count
Write-Output ("##DOCS-DONE {0} {1}" -f $checks, $failed.Count)
if ($failed.Count -eq 0) { exit 0 }
exit 1
