# Hy-MT2 本地翻译引擎 —— 窗口托管版
# 用法：右键本文件 →「使用 PowerShell 运行」。弹出的窗口就是服务主体：
#       窗口在 = 引擎在跑；关掉窗口 = 引擎停止、显存释放。
# 也可以直接敲 hymt2on（会在新窗口里打开这个脚本）。
#
# 引擎就绪后会自动把 LiveCaptionsTranslator（LCT）拉起来，
# 所以「启动翻译引擎.bat」= 引擎 + LCT 一键起。
# 语音语言（日语/英语）在 LCT 主窗口标题栏里切，不用再开不同的 bat。

param(
    [string]$Model = 'Hy-MT2-1.8B-Q4_K_M.gguf' # 7B 需手动指定：hymt2on Hy-MT2-7B-Q4_K_M.gguf
)

$root   = if ($PSScriptRoot) { $PSScriptRoot } else { 'D:\tools\LiveCaptionsTranslator' }
$dir    = Join-Path $root 'llama.cpp'
$lctExe = Join-Path $root 'LiveCaptionsTranslator-win-x64-withruntime.exe'
$apiUrl = 'http://127.0.0.1:57530/v1/models'
$env:NO_COLOR = '1'
try { $Host.UI.RawUI.WindowTitle = 'Hy-MT2 翻译引擎  ·  关掉本窗口即停止' } catch { }

function Get-LctProcess {
    @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like '*LiveCaptionsTranslator*' })
}

function Start-Lct {
    if (-not (Test-Path -LiteralPath $lctExe)) {
        Write-Host "  找不到 $lctExe，跳过 LCT" -ForegroundColor Yellow
        return
    }
    $running = Get-LctProcess
    if ($running.Count -gt 0) {
        Write-Host "  LCT 已在运行（pid $($running[0].Id)），跳过" -ForegroundColor DarkGray
        return
    }
    Start-Process -FilePath $lctExe -WorkingDirectory $root | Out-Null
    Write-Host '  已启动 LiveCaptionsTranslator（语音语言在它标题栏里切）' -ForegroundColor Green
}

# 双击 .bat 时每次都会新开窗口，这里挡掉重复启动（端口 57530 只有一个能占）
$exist = Get-Process llama-server -ErrorAction SilentlyContinue | Select-Object -First 1
if ($exist) {
    Write-Host ''
    Write-Host "  引擎已在运行（pid $($exist.Id)），无需重复启动。" -ForegroundColor Yellow
    Write-Host '  要重启：先双击「停止翻译引擎.bat」，再双击「启动翻译引擎.bat」。' -ForegroundColor DarkGray
    # 引擎已在跑，但 LCT 未必开着 —— 顺手补上
    Start-Lct
    Write-Host '  本窗口 5 秒后自动关闭。' -ForegroundColor DarkGray
    Start-Sleep -Seconds 5
    return
}

Write-Host ''
Write-Host '  Hy-MT2 翻译引擎   http://127.0.0.1:57530' -ForegroundColor Cyan
Write-Host "  模型：$Model" -ForegroundColor DarkCyan
Write-Host '  ─────────────────────────────────────────────' -ForegroundColor DarkGray
Write-Host '  关闭本窗口 = 停止引擎并释放显存' -ForegroundColor Yellow
Write-Host '  引擎就绪后会自动启动 LiveCaptionsTranslator' -ForegroundColor DarkGray
Write-Host '  Ctrl+C 也可停止' -ForegroundColor DarkGray
Write-Host ''

# 后台盯端口，接口一就绪就起 LCT。
# 必须用 Start-Job（独立进程）：下面的 llama-server 是前台阻塞的，
# 同一个线程里等端口会永远等不到。
Start-Job -Name 'wait-engine-then-lct' -ScriptBlock {
    param($url, $exe, $wd)
    for ($i = 0; $i -lt 90; $i++) {
        try {
            $wc = New-Object System.Net.WebClient
            $wc.Proxy = $null
            $null = $wc.DownloadString($url)
            $wc.Dispose()
            Start-Process -FilePath $exe -WorkingDirectory $wd
            return
        } catch { }
        Start-Sleep -Milliseconds 1000
    }
} -ArgumentList $apiUrl, $lctExe, $root | Out-Null

Set-Location $dir
& "$dir\llama-server.exe" `
    --device Vulkan0 `
    -m "$dir\models\$Model" `
    --host 127.0.0.1 --port 57530 `
    -c 2048 -np 1 -ngl 99 -fa on `
    --log-colors off

$code = $LASTEXITCODE
Get-Job -Name 'wait-engine-then-lct' -ErrorAction SilentlyContinue | Stop-Job -ErrorAction SilentlyContinue
Get-Job -Name 'wait-engine-then-lct' -ErrorAction SilentlyContinue | Remove-Job -Force -ErrorAction SilentlyContinue
Write-Host ''
if ($code -ne 0) {
    Write-Host "  引擎已退出（退出码 $code），窗口 5 秒后自动关闭" -ForegroundColor Red
    Start-Sleep -Seconds 5
} else {
    Write-Host '  引擎已停止，显存已释放。' -ForegroundColor Yellow
}
