# 進捗は Write-Host で出す（ワークフローのログで読むためのもので、パイプラインへ返す値ではない）。
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification = 'CI のログへ進捗を出すため、パイプラインへ返さず Write-Host を使う')]
<#
.SYNOPSIS
CI のランナーへ Windows App Runtime を導入する。

.DESCRIPTION
WinUI 3 版は Windows App Runtime へフレームワーク依存で動く（WinUI 3 版のプロジェクトの
`WindowsAppSDKSelfContained=false`）ため、テストと配布物の起動検証にはランナーへランタイムが要る。
ランナーのイメージには 1.6 / 1.8 しか無く、本アプリが使う版が無いため、公式のインストーラーで導入する。
規則は `.github/検証手順.md` §17。

**アプリ自身も起動時に確認して導入する**（`EsUtil.Ui.WinUI3.Interop` の `Bootstrapping.WindowsAppRuntimeBootstrap`。
無ければ利用者へ案内する）。 CI でここで導入するのは、テスト（アプリを起動しない）にランタイムが要ることと、
配布物の起動検証で案内 UI を出さずに済ませるためである。

導入する版は `Directory.Packages.props` の `Microsoft.WindowsAppSDK` の版から導く（`2.6.5` なら `2.6`）。
実際に導入されるのは、その major.minor 系列の最新版である。

.PARAMETER RepoRoot
リポジトリのルート。 既定は本スクリプトの 1 つ上の階層。

.EXAMPLE
pwsh -NoProfile -File buildScript/installWindowsAppRuntime.ps1
#>
[CmdletBinding()]
param(
    [string]$RepoRoot
)

$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) { $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path }

$propsPath = Join-Path $RepoRoot 'Directory.Packages.props'
if (-not (Test-Path -LiteralPath $propsPath)) { throw "パッケージの版の所有ファイルがありません: $propsPath" }

[xml]$props = Get-Content -LiteralPath $propsPath -Raw
$sdkVersion = (@($props.Project.ItemGroup.PackageVersion) |
    Where-Object { $_.Include -eq 'Microsoft.WindowsAppSDK' } |
    Select-Object -First 1).Version
if (-not $sdkVersion) { throw "Microsoft.WindowsAppSDK の版がありません: $propsPath" }

$majorMinor = ($sdkVersion -split '\.')[0..1] -join '.'

$installer = Join-Path ([IO.Path]::GetTempPath()) "windowsappruntimeinstall-$([guid]::NewGuid().ToString('N')).exe"
$uri = "https://aka.ms/windowsappsdk/$majorMinor/latest/windowsappruntimeinstall-x64.exe"
Write-Host "Windows App Runtime $majorMinor 系を導入します: $uri"
Invoke-WebRequest $uri -OutFile $installer
try {
    & $installer --quiet
    if ($LASTEXITCODE -ne 0) { throw "Windows App Runtime の導入に失敗しました（exit $LASTEXITCODE）" }
}
finally {
    Remove-Item $installer -Force -ErrorAction SilentlyContinue
}

# 導入されたことを確かめる。 パッケージ名は 1.x が `Microsoft.WindowsAppRuntime.<major>.<minor>`、
# 2.x が `Microsoft.WindowsAppRuntime.<major>`（版は `2.6.5.0` のように minor まで持つ）である。
# 実測 2026-11-07: `Microsoft.WindowsAppRuntime.2.6*` では 2.6 を検出できない。
$major = ($majorMinor -split '\.')[0]
$installed = @(Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime.*' | Where-Object {
        $_.Name -eq "Microsoft.WindowsAppRuntime.$majorMinor" -or
        ($_.Name -eq "Microsoft.WindowsAppRuntime.$major" -and $_.Version -like "$majorMinor.*")
    })
if ($installed.Count -eq 0) { throw "Windows App Runtime $majorMinor 系を導入できませんでした" }
($installed | Select-Object Name, Version | Format-Table | Out-String).Trim() | Write-Host
