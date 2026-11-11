@echo off
rem  编码：GBK + CRLF，不要加 chcp（切代码页会把后面的中文行拆断）
rem  ============================================================
rem  双击本文件 = 在新窗口启动 Hy-MT2 翻译引擎（127.0.0.1:57530）
rem  那个新窗口就是服务本体：
rem      窗口在   = 引擎在跑
rem      关掉窗口 = 引擎停止、显存释放
rem  （已在运行时由 ps1 自己提示，不会重复占端口）
rem
rem  引擎接口就绪后会自动拉起 LiveCaptionsTranslator（LCT）。
rem  语音语言（日语 / 英语）在 LCT 主窗口标题栏里切，不用再开不同的 bat。
rem  ============================================================
title 启动翻译引擎 + LCT
cd /d "%~dp0"

where pwsh >nul 2>&1
if %errorlevel% equ 0 (
    start "Hy-MT2 翻译引擎" pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0启动翻译引擎.ps1"
) else (
    echo [info] 没找到 pwsh 7，改用 Windows PowerShell 5.1
    start "Hy-MT2 翻译引擎" powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0启动翻译引擎.ps1"
)
exit /b
