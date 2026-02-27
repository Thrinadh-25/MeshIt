@echo off
echo =============================================
echo  meshIt Publish Script
echo =============================================
echo.

echo [1/3] Restoring packages...
dotnet restore
if %errorlevel% neq 0 (
    echo ERROR: Restore failed!
    pause
    exit /b 1
)

echo [2/3] Publishing self-contained win-x64...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\win-x64
if %errorlevel% neq 0 (
    echo ERROR: Publish failed!
    pause
    exit /b 1
)

echo [3/3] Publishing self-contained win-arm64...
dotnet publish -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\win-arm64
if %errorlevel% neq 0 (
    echo WARNING: ARM64 publish failed (optional)
)

echo.
echo =============================================
echo  Published to:
echo    publish\win-x64\meshIt.exe
echo    publish\win-arm64\meshIt.exe
echo =============================================
pause
