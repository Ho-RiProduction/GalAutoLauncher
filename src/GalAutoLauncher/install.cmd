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

echo 正在开启开机自启...
"%EXE%" install --start

echo.
echo 完成。程序将随 Windows 登录自动启动并驻留托盘。
echo 如需取消，请运行 uninstall.cmd
timeout /t 4 >nul
exit /b 0
