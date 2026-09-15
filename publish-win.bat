@echo off
setlocal

set PROJECT=src\ErganiManager.UI\ErganiManager.UI.csproj
set OUTPUT=publish\win-x64

echo.
echo ========================================
echo   ErganiManager — Windows x64 Publish
echo ========================================
echo.

dotnet publish %PROJECT% ^
    --configuration Release ^
    --runtime win-x64 ^
    --self-contained true ^
    --output %OUTPUT% ^
    /p:PublishSingleFile=true ^
    /p:IncludeNativeLibrariesForSelfExtract=true ^
    /p:EnableCompressionInSingleFile=true ^
    /p:DebugType=embedded

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Publish failed.
    exit /b %ERRORLEVEL%
)

echo.
echo ========================================
echo   Output: %CD%\%OUTPUT%
echo ========================================
echo.
echo   Single executable: %OUTPUT%\ErganiManager.UI.exe
echo.
echo   To distribute: copy the entire %OUTPUT% folder
echo   (the .exe is self-contained — no .NET install needed on the target PC)
echo.
