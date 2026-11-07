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
rem  The modes are restricted to `aot` and `single`.
rem    aot     Native AOT (first choice).  PublishSingleFile must be false: the AOT
rem            compiler writes one native exe itself, and the combination with
rem            PublishSingleFile=true is rejected (measured 2026-11-07).
rem    single  single file of the managed runtime (fallback).  PublishAot is set to false
rem            explicitly so the fallback never inherits the AOT settings.
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
rem  projects and fails there (measured), so the per-mode switches below are used.
rem
rem  Layout: one native exe (aot) or one self extracting exe (single).
rem ============================================================

rem Move from the script folder (buildScript) to the repository root.
pushd "%~dp0.."
set "SE_MODES=aot single"
set "SE_AOT_ARGS=-p:PublishAot=true -p:PublishSingleFile=false"
set "SE_SINGLE_ARGS=-p:PublishAot=false -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true"

call "%~dp0publish_ui.bat" winui3 "src\App.WinUI3\App.WinUI3.csproj" %*
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%
