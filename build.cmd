@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"

echo ============================================
echo   GalAutoLauncher 构建 / 发布
echo ============================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [错误] 未检测到 .NET SDK。请先安装 .NET 8 SDK：https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

echo [1/2] 还原并构建...
dotnet build GalAutoLauncher.sln -c Release
if errorlevel 1 (
    echo.
    echo [错误] 构建失败。
    pause
    exit /b 1
)

echo.
echo [2/2] 发布为独立单文件 exe（win-x64，无需安装 .NET）...
dotnet publish "src\GalAutoLauncher\GalAutoLauncher.csproj" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o "publish"

if errorlevel 1 (
    echo.
    echo [错误] 发布失败。
    pause
    exit /b 1
)

echo.
echo 完成！发布文件位于： %~dp0publish
echo 分发时请保持以下文件在同一目录：
echo   GalAutoLauncher.exe
echo   config.json
echo   games.json
echo   install.cmd
echo   uninstall.cmd
echo.
pause
exit /b 0
