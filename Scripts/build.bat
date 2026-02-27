@echo off
echo =============================================
echo  meshIt Build Script
echo =============================================
echo.

echo [1/3] Restoring NuGet packages...
dotnet restore
if %errorlevel% neq 0 (
    echo ERROR: NuGet restore failed!
    pause
    exit /b 1
)

echo [2/3] Building Release...
dotnet build -c Release --no-restore
if %errorlevel% neq 0 (
    echo ERROR: Build failed!
    pause
    exit /b 1
)

echo [3/3] Build successful!
echo.
echo Output: bin\Release\net8.0-windows10.0.19041.0\
echo =============================================
pause
