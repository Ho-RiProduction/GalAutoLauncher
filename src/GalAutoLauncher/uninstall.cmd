@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"

set "EXE=%~dp0GalAutoLauncher.exe"

if not exist "%EXE%" (
    echo [错误] 未找到 GalAutoLauncher.exe
    echo        请将本脚本与 GalAutoLauncher.exe 放在同一目录。
    pause
    exit /b 1
)

echo 正在关闭开机自启...
"%EXE%" uninstall

echo.
echo 已取消开机自启。程序本身未被删除。
timeout /t 4 >nul
exit /b 0
