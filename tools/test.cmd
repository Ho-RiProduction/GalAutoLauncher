@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0.."

echo ============================================
echo   GalAutoLauncher 测试（全部在隐藏桌面运行）
echo ============================================
echo.
echo 测试会真实弹出窗体（界面冒烟、模拟普通游戏窗口等）。
echo 本脚本把它们放到一个独立的隐藏桌面上，
echo 你的屏幕不会被遮挡，也不会抢焦点。
echo.

set "DOTNET_ROLL_FORWARD=LatestMajor"
set "FAILED=0"

call :run "识别逻辑测试"      "tests\GalAutoLauncher.ScannerTests"
call :run "界面冒烟测试"      "tests\GalAutoLauncher.UiSmoke"
call :run "端到端触发测试"    "tests\GalAutoLauncher.TriggerTests"
call :run "开机自启行为测试"  "tests\GalAutoLauncher.BootTests"

echo.
if "%FAILED%"=="0" (
    echo ============================================
    echo   全部测试通过
    echo ============================================
) else (
    echo ============================================
    echo   有 %FAILED% 项测试失败
    echo ============================================
)
echo.
pause
exit /b %FAILED%

:run
echo --------------------------------------------
echo [测试] %~1
echo --------------------------------------------
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "& '.\tools\run-hidden.ps1' -FilePath dotnet -Arguments @('run','--project','%~2','-c','Release','--no-build') -WorkingDirectory (Get-Location).Path; exit $LASTEXITCODE"
if errorlevel 1 (
    set /a FAILED+=1
    echo [结果] 失败
) else (
    echo [结果] 通过
)
echo.
exit /b 0
