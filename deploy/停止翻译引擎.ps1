# Hy-MT2 翻译引擎 —— 命令行兜底关闭
# 正常情况下直接关掉引擎窗口就行；这个是窗口找不到了时用的。

$p = Get-Process llama-server -ErrorAction SilentlyContinue
if (-not $p) {
    Write-Host '引擎本来就没开。' -ForegroundColor DarkGray
    exit
}
$p | Stop-Process -Force -Confirm:$false
Start-Sleep -Milliseconds 600
if (Get-Process llama-server -ErrorAction SilentlyContinue) {
    Write-Host '进程还在，需要手动结束 llama-server.exe' -ForegroundColor Red
} else {
    Write-Host '引擎已停止，显存已释放。' -ForegroundColor Green
}
