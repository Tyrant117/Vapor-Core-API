@echo off
setlocal enableextensions

rem Builds the Vapor Keys Rider/ReSharper plugin.
rem Usage:  build.bat            (Release)
rem         build.bat Debug      (or any configuration name)

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Release"
set "ROOT=%~dp0"
set "SLN=%ROOT%VaporKeysPlugin.sln"

where dotnet >nul 2>nul
if errorlevel 1 goto no_dotnet

echo ============================================================
echo  Building VaporKeysPlugin  [%CONFIG%]
echo ============================================================
echo The first build restores the JetBrains SDK package and can take a few minutes.
echo.

dotnet build "%SLN%" -c %CONFIG% -v minimal
if errorlevel 1 goto build_failed

rem for /r yields the name for EVERY directory whether or not the file exists, so guard with
rem "if exist" - otherwise an empty TFM folder that happens to sort last (e.g. net9.0-windows)
rem leaves DLL pointing at nothing and the zip silently ships without the dll.
set "DLL="
for /r "%ROOT%src\bin\%CONFIG%" %%f in (VaporKeysPlugin.dll) do if exist "%%f" set "DLL=%%f"

echo.
echo [BUILD SUCCEEDED] Packaging Rider plugin zip...
if not defined DLL goto no_dll
if not exist "%ROOT%META-INF\plugin.xml" goto no_pluginxml

rem Rider loads a backend plugin from a zip laid out as:
rem   VaporKeysPlugin/META-INF/plugin.xml
rem   VaporKeysPlugin/dotnet/VaporKeysPlugin.dll   (Rider supplies the ReSharper SDK assemblies)
set "STAGE=%ROOT%build\zip"
set "PLUGIN=%STAGE%\VaporKeysPlugin"
set "ZIP=%ROOT%VaporKeysPlugin.zip"

if exist "%STAGE%" rmdir /s /q "%STAGE%"
mkdir "%PLUGIN%\dotnet"
mkdir "%PLUGIN%\META-INF"
copy /y "%DLL%" "%PLUGIN%\dotnet\VaporKeysPlugin.dll" >nul
copy /y "%ROOT%META-INF\plugin.xml" "%PLUGIN%\META-INF\plugin.xml" >nul
if not exist "%PLUGIN%\dotnet\VaporKeysPlugin.dll" goto stage_failed
if not exist "%PLUGIN%\META-INF\plugin.xml" goto stage_failed
if exist "%ZIP%" del /q "%ZIP%"
rem NOTE: do NOT use Compress-Archive here - it writes backslash entry names that Rider rejects
rem ("Corrupted archive (no file entries)"). pack.ps1 writes forward-slash entries instead.
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%pack.ps1" -StageRoot "%STAGE%" -Zip "%ZIP%"
if not exist "%ZIP%" goto zip_failed

echo.
echo [DONE] Plugin package:
echo   %ZIP%
echo.
echo Install in Rider:  Settings ^> Plugins ^> gear icon ^> "Install Plugin from Disk..."
echo                    pick the ZIP above, then restart Rider.
goto end

:no_pluginxml
echo [ERROR] Missing "%ROOT%META-INF\plugin.xml"; cannot package the plugin zip.
echo         Compiled DLL is at: %DLL%
goto end

:stage_failed
echo [ERROR] Failed to stage the plugin files into "%STAGE%". Compiled DLL is at:
echo   %DLL%
goto end

:zip_failed
echo [ERROR] Failed to create the plugin zip. Compiled DLL is at:
echo   %DLL%
goto end

:no_dll
echo Build succeeded but VaporKeysPlugin.dll was not found under:
echo   %ROOT%src\bin\%CONFIG%
echo Check TargetFramework in src\VaporKeysPlugin.csproj.
goto end

:build_failed
echo.
echo [BUILD FAILED] See the errors above.
echo If they are missing JetBrains.* types: set your Rider version in
echo   src\VaporKeysPlugin.csproj  and fill the TODO(SDK) spots - see README.md.
goto end

:no_dotnet
echo [ERROR] 'dotnet' is not on your PATH.
echo Install the .NET SDK that matches your Rider - net8 for modern Rider:
echo   https://dotnet.microsoft.com/download

:end
echo.
pause
endlocal
