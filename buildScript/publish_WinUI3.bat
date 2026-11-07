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
rem  Layout: one native exe (plus the runtime DLLs the Windows App SDK needs to
rem  bootstrap a framework dependent app).
rem ============================================================

rem Move from the script folder (buildScript) to the repository root.
pushd "%~dp0.."
set "SE_MODES=aot"
set "SE_AOT_ARGS=-p:PublishAot=true -p:PublishSingleFile=false"

call "%~dp0publish_ui.bat" winui3 "src\App.WinUI3\App.WinUI3.csproj" %*
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%
