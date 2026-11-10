@echo off
setlocal
chcp 65001 >nul

rem ============================================================
rem  ZenHanConverter (WinUI 3) publish script
rem
rem  ASCII only.  cmd.exe reads a batch file with the console code page and counts the read
rem  position in decoded characters, so non-ASCII text garbles the output and can make cmd
rem  execute fragments of the file as commands.  The chcp below keeps the UTF-8 log of
rem  dotnet readable (safe because this file is ASCII).  Rules: .github/RELEASE.md.
rem
rem  The only mode is `aot`.  WinUI 3 ships as a Native AOT exe.
rem    - PublishSingleFile must be false: the AOT compiler writes one native exe
rem      itself, and PublishSingleFile=true is rejected with it (measured 2026-11-07).
rem    - Do not fall back to another mode here.  A fallback would exit 0 with an output
rem      folder that does not match uis[].output in .github/release-config.json, so the
rem      release job would either fail on a clean machine or package a stale AOT folder.
rem      A managed single file publish must be a separate, explicit entry point.
rem
rem  Windows App Runtime is not bundled (WindowsAppSDKSelfContained is false).
rem  src\App.WinUI3\Program.cs asks EsUtil.Ui.WinUI3.Interop's WindowsAppRuntimeBootstrap
rem  to check and install the runtime before WinUI starts (measured to start with the
rem  runtime installed, 2026-11-07).
rem
rem  EnableMsixTooling is owned by src\App.WinUI3\App.WinUI3.csproj: without the generated
rem  resources.pri the published exe fails at startup in Microsoft.UI.Xaml.dll with
rem  exception code 0xc000027b (measured 2026-11-07).
rem
rem  Passing `-p:PublishSingleFile=true` on the command line flows to the referenced
rem  projects and fails there (measured), so the switches below are used.
rem
rem  Layout: one native exe, one resources.pri and the native DLLs the Windows App
rem  SDK needs to bootstrap a framework dependent app (the runtime itself is not
rem  bundled).  The whole output folder is shipped, not the exe alone.
rem ============================================================

rem  Show the lower bound of the Windows App Runtime that the AOT shape needs.
rem  The value is owned by Directory.Packages.props (Microsoft.WindowsAppSDK); the app asks for the
rem  same version at start.  Keep it in sync, do not repeat the number in this script.
set "WASDK="
for /f "tokens=3 delims==" %%v in ('findstr /c:"Include=\"Microsoft.WindowsAppSDK\"" Directory.Packages.props') do if not defined WASDK set "WASDK=%%v"
if defined WASDK set "WASDK=%WASDK:"=%"
for /f "tokens=1" %%v in ("%WASDK%") do set "WASDK=%%v"
if defined WASDK echo [INFO] Windows App Runtime: %WASDK% or later (x64) is required for the AOT shape
if not defined WASDK echo [WARN] Windows App Runtime: the lower bound was not found in Directory.Packages.props

rem Move from the script folder (buildScript) to the repository root.
pushd "%~dp0.."

rem  The UI ships **two shapes** and both are release assets:
rem    1. aot     Native AOT in a folder (framework-dependent Windows App Runtime).  Smallest and
rem               lowest memory when the runtime is installed (code pages are shared).
rem    2. single  One exe with the Windows App Runtime bundled (.NET is always bundled here).
rem               Runs on a machine where nothing can be installed (Windows Sandbox, a fresh VM).
rem  The folder name carries the shape (publish\winui3-win-x64-aot / winui3-win-x64-single).
rem  The asset name carries it too (ZenHanConverter.WinUI3.{version}.zip / ...WinUI3.Single.{version}.zip);
rem  the rule is owned by .github naming rules.
rem  The single shape needs the runtime directory to be set before program entry; Program.Main does it
rem  when it detects a single file bundle.

rem ---- Shape 1: Native AOT in a folder ----
set "SE_MODES=aot"
set "SE_AOT_ARGS=-p:PublishAot=true -p:PublishSingleFile=false"
set "SE_SINGLE_ARGS="
call "%~dp0publish_ui.bat" winui3 "src\App.WinUI3\App.WinUI3.csproj" %*
set "RESULT_AOT=%ERRORLEVEL%"
set "SE_AOT_ARGS="

rem ---- Shape 2: one exe with the Windows App Runtime bundled ----
rem  `ZenHanConverterSelfContained` bundles the Windows App Runtime (see src\App.WinUI3\App.WinUI3.csproj).
rem  `IncludeNativeLibrariesForSelfExtract` keeps the native libraries inside the exe and extracts them
rem  at start (required by the Windows App SDK single file support).
set "SE_MODES=single"
set "SE_SINGLE_ARGS=-p:ZenHanConverterSelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true"
call "%~dp0publish_ui.bat" winui3 "src\App.WinUI3\App.WinUI3.csproj" %*
set "RESULT_SINGLE=%ERRORLEVEL%"

popd
rem Both shapes are release assets, so a failure in either one fails the script.
if not "%RESULT_AOT%"=="0" exit /b %RESULT_AOT%
exit /b %RESULT_SINGLE%
