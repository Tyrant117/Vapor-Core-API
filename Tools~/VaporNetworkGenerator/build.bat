@echo off
setlocal enableextensions

rem Builds the Vapor network Roslyn source generator (rpcs + formatters) and drops the DLL where
rem Unity picks it up.
rem Usage:  build.bat            (Release)
rem         build.bat Debug      (or any configuration name)
rem
rem After it runs, Unity reimports the DLL. The .meta beside it carries the RoslynAnalyzer label,
rem which is what makes Unity feed it to the C# compiler rather than treat it as a managed plugin.

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Release"
set "ROOT=%~dp0"
set "PROJ=%ROOT%src\VaporNetworkGenerator.csproj"
set "DEST=%ROOT%..\..\Runtime\Networking\Analyzers"

where dotnet >nul 2>nul
if errorlevel 1 goto no_dotnet

echo ============================================================
echo  Building Vapor.Network.SourceGenerator  [%CONFIG%]
echo ============================================================

dotnet build "%PROJ%" -c %CONFIG% -v minimal
if errorlevel 1 goto build_failed

set "DLL=%ROOT%src\bin\%CONFIG%\netstandard2.0\Vapor.Network.SourceGenerator.dll"
if not exist "%DLL%" goto no_dll

if not exist "%DEST%" mkdir "%DEST%"
copy /y "%DLL%" "%DEST%\Vapor.Network.SourceGenerator.dll" >nul
if errorlevel 1 goto copy_failed

echo.
echo [DONE] Generator copied to:
echo   %DEST%\Vapor.Network.SourceGenerator.dll
echo.
echo Switch to Unity and let it reimport. Check Console for "VNET" diagnostics.
goto end

:no_dll
echo [ERROR] Build reported success but the DLL was not found at:
echo   %DLL%
echo Check TargetFramework in src\VaporNetworkGenerator.csproj.
goto end

:copy_failed
echo [ERROR] Could not copy the DLL into "%DEST%".
echo If Unity is open it may hold a lock - close it and run again.
goto end

:build_failed
echo.
echo [BUILD FAILED] See the errors above.
goto end

:no_dotnet
echo [ERROR] 'dotnet' is not on your PATH. Install the .NET SDK:
echo   https://dotnet.microsoft.com/download

:end
echo.
endlocal
