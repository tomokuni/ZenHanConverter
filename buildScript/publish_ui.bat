@echo off
setlocal
chcp 65001 >nul

rem ============================================================
rem  SaveEvidence: publish procedure shared by every UI (called from publish_<UI>.bat)
rem
rem  ASCII only.  cmd.exe reads a batch file with the console code page and counts the read
rem  position in decoded characters, so non-ASCII text garbles the output and can make cmd
rem  execute fragments of the file as commands (measured 2026-10-17: the Japanese comments of
rem  this file were partly executed as commands, while chcp did not remove the garble).  The
rem  chcp below is safe for an ASCII file and keeps the UTF-8 log of dotnet readable.
rem
rem  Modes: try in order and keep the first one that succeeds.
rem    1. aot     PublishAot + PublishSingleFile (managed code compiled to native)
rem    2. single  PublishSingleFile (self-contained runtime bundled into one exe)
rem    3. exe     plain publish (exe + DLLs in a folder)
rem
rem  Usage:
rem    call publish_ui.bat <ui> <project path> [rid]
rem    <ui> is uis[].name in .github/release-config.json (lower case) and prefixes the output
rem    folder (mewui / avaloniaui / winforms / winui3).
rem
rem  Output: publish\<ui>-<rid>-<mode>.  The mode is part of the name so that the shape of the
rem  distribution (single exe or folder) can be read from the name.  The expected folder is
rem  owned by uis[].output in .github/release-config.json (buildScript/verifyPublish.ps1
rem  reports a missing folder).
rem
rem  Which modes work is decided by the UI and its framework (measured facts: .github/RELEASE.md).
rem
rem  Warnings are errors (-warnaserror), so trimming and AOT analysis warnings
rem  (IL2026 / IL2070 / IL3050 ...) fail the publish as well, except for an explicitly
rem  documented per-UI warning ID exception (currently WinUI 3 IL2104; see appendix A of
rem  the build settings rules in .github).  Measured: adding it revealed latent defects
rem  that the build had not reported.
rem
rem  Not shipped:
rem    - *.pdb        debug symbols
rem    - RestartAgent.exe  helper exe that Windows App SDK adds for the restart API
rem      (this app does not use the restart API; startup and shutdown were verified without
rem       it.  Keeping it would break the "one exe only" check)
rem  Everything else is copied.  Do not filter by extension: AOT publishes an exe plus native
rem  DLLs, and a self-contained WinUI 3 publish adds DLL + .pri + .winmd + .json, and dropping
rem  any of them breaks the app (measured: dropping .pri from the WinUI 3 distribution made
rem  the app start and then fail on exit with 0x80008083, a WinRT stowed exception).
rem ============================================================

if "%~1"=="" (
  echo [ERROR] No UI name.  Usage: call publish_ui.bat ^<ui^> ^<project^> [rid]
  exit /b 1
)
if "%~2"=="" (
  echo [ERROR] No project.  Usage: call publish_ui.bat ^<ui^> ^<project^> [rid]
  exit /b 1
)

set "UI=%~1"
set "PROJECT=%~2"
if "%~3"=="" (set "RID=win-x64") else (set "RID=%~3")

if not exist "%PROJECT%" (
  echo [ERROR] Project not found: %PROJECT%
  exit /b 1
)

rem Temporary staging folder for the publish.
rem Publishing inside the repository also creates publish folders in the referenced projects,
rem so the publish goes to a temporary folder outside the repository, and only the needed
rem files are copied to the output folder.
set "STAGE=%TEMP%\publish-ui-%UI%-%RID%"
rem One log per mode.  A shared log would be overwritten by the fallback mode, losing the reason
rem the previous mode failed (measured 2026-10-19: AOT fell back to single and only the log of
rem single was left).
set "LOGNAME=publish-ui-%UI%-%RID%"

rem The UI script can restrict the modes (`set "SE_MODES=single"`).  The default tries all
rem modes in order.  A UI that must ship more than one shape calls this script once per shape
rem from its own wrapper (WinUI 3 keeps single file publishing in publish_WinUI3.bat alone).
if not defined SE_MODES set "SE_MODES=aot single exe"

echo.
echo [INFO] UI     : %UI%
echo [INFO] Target : %RID%
echo [INFO] Modes  : %SE_MODES% (first success wins)
echo.

rem The UI script can restrict the modes (`set "SE_MODES=single"`).  The default tries all
rem modes in order.  A UI that must ship more than one shape calls this script once per shape
rem from its own wrapper (WinUI 3 keeps single file publishing in publish_WinUI3.bat alone).
set "MODE="

for %%m in (%SE_MODES%) do (
  if not defined MODE (
    call :TryPublish %%m
    if not errorlevel 1 set "MODE=%%m"
  )
)

if not defined MODE (
  echo.
  echo [ERROR] Publish failed in every mode.
  for %%m in (aot single exe) do if exist "%TEMP%\%LOGNAME%-%%m.log" echo      %%m: %TEMP%\%LOGNAME%-%%m.log
  if exist "%TEMP%\%LOGNAME%-exe.log" type "%TEMP%\%LOGNAME%-exe.log"
  if exist "%STAGE%" rd /s /q "%STAGE%"
  exit /b 1
)

set "OUTDIR=%CD%\publish\%UI%-%RID%-%MODE%"

rem Pick the exe to ship (the name is owned by <AssemblyName> in the csproj, not decided here).
set "APP_NAME="
set "FRAMEWORK_EXE=RestartAgent.exe"
for %%f in ("%STAGE%\*.exe") do if not defined APP_NAME if /i not "%%~nxf"=="%FRAMEWORK_EXE%" set "APP_NAME=%%~nxf"
if not defined APP_NAME (
  echo.
  echo [ERROR] No executable found in: %STAGE%
  if exist "%STAGE%" rd /s /q "%STAGE%"
  exit /b 1
)

if exist "%OUTDIR%" rd /s /q "%OUTDIR%"
mkdir "%OUTDIR%"
xcopy "%STAGE%\*" "%OUTDIR%\" /E /Q /Y >nul
del /q "%OUTDIR%\*.pdb" 2>nul
del /q "%OUTDIR%\%FRAMEWORK_EXE%" 2>nul
rd /s /q "%STAGE%"

echo [INFO] Mode   : %MODE%
echo [OK] Built: %OUTDIR%\%APP_NAME%
exit /b 0

rem ============================================================
rem  Try to publish with one mode.  Returns 0 on success, 1 on failure.
rem ============================================================
:TryPublish
set "TRY_MODE=%~1"
set "TRY_LOG=%TEMP%\%LOGNAME%-%TRY_MODE%.log"
if exist "%STAGE%" rd /s /q "%STAGE%"

set "ARGS="
rem The UI script can replace the arguments per mode (WinUI 3 keeps single file publishing in
rem its csproj, because passing it on the command line flows to the referenced projects and
rem fails there.  See buildScript/publish_WinUI3.bat).
rem `set "VAR="` makes a variable undefined in cmd, so the intent "empty" is expressed by
rem SE_SKIP_*_ARGS instead.
if not defined SE_AOT_ARGS if not defined SE_SKIP_AOT_ARGS set "SE_AOT_ARGS=-p:PublishAot=true -p:PublishSingleFile=true"
if not defined SE_SINGLE_ARGS if not defined SE_SKIP_SINGLE_ARGS set "SE_SINGLE_ARGS=-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true"
if "%TRY_MODE%"=="aot" set "ARGS=%SE_AOT_ARGS%"
if "%TRY_MODE%"=="single" set "ARGS=%SE_SINGLE_ARGS%"

rem SelfContained is not passed explicitly (-r implies it).  Passing it also flows to the
rem referenced projects, which then demand EnableMsixTooling and WindowsAppSDKSelfContained
rem from the WinUI 3 libraries and fail (measured).
rem The publish restore uses its own lock file (NuGetLockFilePath).  A restore with a RID records
rem the RID in the lock, and the build / test restore (no RID) then fails with NU1004 ("runtime
rem identifiers differ").  RestorePackagesWithLockFile=false is not an option either (NU1005 while
rem packages.lock.json exists).  The committed packages.lock.json therefore stays free of RIDs,
rem and the RID graph is written to packages.%RID%.lock.json (git-ignored).  Measured 2026-10-26.
dotnet publish "%PROJECT%" -c Release -r "%RID%" -warnaserror -p:NuGetLockFilePath=packages.%RID%.lock.json %ARGS% -o "%STAGE%" >"%TRY_LOG%" 2>&1
if errorlevel 1 (
  echo [WARN] Mode %TRY_MODE% failed.  Log: %TRY_LOG%
  exit /b 1
)
if not exist "%STAGE%\*.exe" (
  echo [WARN] Mode %TRY_MODE% failed: no exe was published.
  exit /b 1
)
exit /b 0
