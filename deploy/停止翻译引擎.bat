@echo off
rem  编码：GBK + CRLF，不要加 chcp（切代码页会把后面的中文行拆断）
rem  ============================================================
rem  双击本文件 = 强制停止 Hy-MT2 翻译引擎并释放显存
rem  正常情况下直接关掉引擎窗口就行，这里是窗口不见了时的兜底
rem  ============================================================
title 停止 Hy-MT2 翻译引擎
cd /d "%~dp0"

where pwsh >nul 2>&1
if %errorlevel% equ 0 (
    pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0停止翻译引擎.ps1"
) else (
    powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0停止翻译引擎.ps1"
)

echo.
echo 按任意键关闭...
pause >nul
