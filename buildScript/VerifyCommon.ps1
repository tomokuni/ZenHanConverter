# 検証スクリプトが共有する土台（結果の記録と、ファイル・ネイティブ コマンドの読み込みの使い回し）。
# 所有者は .github/検証手順.md であり、手順の実装は各リポジトリの buildScript/verify.ps1（入口）と
# buildScript/verifyDocs.ps1（文書とコードの走査。 入口から起動される）が担う。
# 本ファイルは検査そのものを持たない（**読み込む検証スクリプトは同じ書式で結果を出し、同じ読み込みを使う**。
# 入口から起動される 2 本のほかに、**リポジトリが独自に持つ検証スクリプトも本ファイルを読み込む**）。
# **本ファイルは複数のリポジトリへ同じ内容で置くため、リポジトリ固有の名前（プロジェクト名・スクリプト名）を
# 書かない**（書くと、他方のリポジトリの走査が「本リポジトリに無い名前」として検出する）。

$script:Results = [System.Collections.Generic.List[object]]::new()

# ネイティブ コマンド（git / dotnet / npx）の出力は UTF-8 である。 一方、PowerShell の `& <コマンド>` は
# その出力を [Console]::OutputEncoding で復号する。 この値は**コンソールの出力コードページ**（日本語環境の
# 既定は CP932）であり、`-NoProfile` で起動すると UTF-8 の出力が文字化けする。
# 文字化けは表示だけの問題ではない（実測: 日本語で書いた除外条件が一致しなくなり、verifyDocs.ps1 の
# 「文書の引用」が誤検出になり、verify.ps1 が `dotnet test` の件数を読めずに 0/0 になった）。
# 復号は Invoke-NativeText が単一所有する（コンソールの表示側＝本スクリプト自身の出力は変えない）。
$script:NativeEncoding = [Text.UTF8Encoding]::new($false)

# ネイティブ コマンドを実行し、**UTF-8 として復号した出力**と終了コードを返す。
# 作業ディレクトリは現在の場所（`$PWD`）にする（`& <コマンド>` と同じ扱い。 既定では本プロセスの
# 起動時のディレクトリになり、`Set-Location` した場所と食い違う）。
# <param name="Command">実行するコマンド（`git` / `dotnet` / `$env:ComSpec`）。</param>
# <param name="Arguments">コマンドへ渡す引数。</param>
# <returns>`Text`（標準出力）/ `ErrorText`（標準エラー）/ `ExitCode` を持つオブジェクト。</returns>
function Invoke-NativeText([string]$Command, [string[]]$Arguments) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new($Command)
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $startInfo.UseShellExecute = $false
    $startInfo.WorkingDirectory = $PWD.Path
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = $script:NativeEncoding
    $startInfo.StandardErrorEncoding = $script:NativeEncoding

    $process = [Diagnostics.Process]::Start($startInfo)
    try {
        # 2 つのストリームは同時に読み取る（片方を待つと、もう片方のバッファが埋まって止まる）
        $standard = $process.StandardOutput.ReadToEndAsync()
        $errorText = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        return [pscustomobject]@{
            Text      = $standard.Result
            ErrorText = $errorText.Result
            ExitCode  = $process.ExitCode
        }
    }
    finally {
        $process.Dispose()
    }
}

function Add-Result([string]$Name, [bool]$Ok, [string]$Detail) {
    $script:Results.Add([pscustomobject]@{ Name = $Name; Ok = $Ok; Kind = 'Check'; Detail = $Detail })
    $mark = if ($Ok) { 'OK  ' } else { 'FAIL' }
    Write-Output "$mark  $Name`t$Detail"
}

# 合否に含めない参考値（数値を見るためだけの項目）
function Add-Info([string]$Name, [string]$Detail) {
    $script:Results.Add([pscustomobject]@{ Name = $Name; Ok = $true; Kind = 'Info'; Detail = $Detail })
    Write-Output "INFO  $Name`t$Detail"
}

# ファイルの中身とリポジトリのファイル一覧は多数の検査が共有するため、1 回だけ読んで使い回す
# （実測: 検査ごとに `*.cs` を列挙して読み直すと 51 秒かかっていた。 ディスクからの読み直しを無くす）。
# 自動修正（markdownlint --fix / dotnet format）の後は Clear-FileCache で捨てる（古い内容で判定しないため）。
$script:LineCache = [System.Collections.Generic.Dictionary[string, string[]]]::new()
$script:TextCache = [System.Collections.Generic.Dictionary[string, string]]::new()
$script:ListCache = [System.Collections.Generic.Dictionary[string, object]]::new()

function Get-Lines([string]$Path) {
    $cached = $null
    if ($script:LineCache.TryGetValue($Path, [ref]$cached)) { return $cached }
    $value = [IO.File]::ReadAllLines($Path, [Text.UTF8Encoding]::new($false))
    $script:LineCache[$Path] = $value
    return $value
}

function Get-Text([string]$Path) {
    $cached = ''
    if ($script:TextCache.TryGetValue($Path, [ref]$cached)) { return $cached }
    $value = [IO.File]::ReadAllText($Path, [Text.UTF8Encoding]::new($false))
    $script:TextCache[$Path] = $value
    return $value
}

function Get-GitList([string[]]$GitArguments) {
    $key = $GitArguments -join ' '
    $cached = $null
    if ($script:ListCache.TryGetValue($key, [ref]$cached)) { return $cached }
    # 標準エラーは一覧へ混ぜない（`2>$null` と同じ扱い。 git の警告を一覧の要素にしない）
    $result = Invoke-NativeText 'git' $GitArguments
    $value = @($result.Text -split "`r?`n" | Where-Object { $_ -ne '' })
    $script:ListCache[$key] = $value
    return $value
}

function Clear-FileCache {
    $script:LineCache.Clear()
    $script:TextCache.Clear()
    $script:ListCache.Clear()
}

# リポジトリの設定（.github/release-config.json）を単一の取得口にする。
# **製品名・ソリューション ファイル・配布物の名前はリポジトリごとに違う**ため、スクリプトの本文へ
# 書かずにここから取る（本文を同じに保ち、リポジトリ間で検証スクリプトを共有できるようにする）。
# <param name="RepoRoot">リポジトリのルート。省略すると本ファイルの親フォルダー。</param>
# <returns>`product` / `solutionFile` などを持つオブジェクト。</returns>
function Get-RepoConfig([string]$RepoRoot) {
    if (-not $RepoRoot) { $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path }
    $path = Join-Path $RepoRoot '.github/release-config.json'
    if (-not (Test-Path -LiteralPath $path)) { throw "リポジトリの設定がありません: $path" }
    return [IO.File]::ReadAllText($path, [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json
}

# 検証するソリューションの一覧を設定から取り出す。
# **アプリのリポジトリは 1 本（`solutionFile`）**、**複数のソリューションを持つリポジトリは配列（`solutions`）**を
# 書く。 どちらの形でも同じスクリプトが動くよう、解決はここへ集約する（リポジトリ間で本文を共有するため）。
# <param name="RepoConfig">`Get-RepoConfig` の戻り値。</param>
# <returns>ソリューション ファイルの配列（リポジトリ ルートからの相対パス）。</returns>
function Get-SolutionList([object]$RepoConfig) {
    if ($RepoConfig.solutions) { return @($RepoConfig.solutions) }
    if ($RepoConfig.solutionFile) { return @($RepoConfig.solutionFile) }
    return @()
}