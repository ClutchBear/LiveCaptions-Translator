# LiveCaptions Translator — 本地化增强版（Fork）

> 基于 [SakiRinn/LiveCaptions-Translator](https://github.com/SakiRinn/LiveCaptions-Translator) 的个人 fork。
>
> 在上游基础上加了三件事：**悬浮字幕窗空闲自动清空**、**LiveCaptions 语音语言（日语/英语）在主窗口切换**、**翻译提示词随语言自动同步**；
> 另外配套一套**完全本地运行**的翻译引擎部署方案（llama.cpp + 腾讯混元 Hy-MT2），断网也能用、不产生任何 API 费用。

---

## 目录

- [一、这个 fork 改了什么](#一这个-fork-改了什么)
- [二、本地部署](#二本地部署windows-11)
- [三、启动与停止](#三启动与停止)
- [四、从源码构建](#四从源码构建)
- [五、使用要点](#五使用要点)
- [六、已知限制](#六已知限制)
- [七、与上游同步](#七与上游同步)

---

## 一、这个 fork 改了什么

### 1. 悬浮字幕窗：空闲自动清空（Overlay Idle Clear）

**问题**：悬浮字幕窗放久了，最后一句译文会一直挂在屏幕上，挡住画面。

**做法**：超过 N 秒没有新译文，就把字幕文字清空——**窗口和半透明背景框留在原位不动**，下一句译文到来时自动恢复显示。

| 项 | 值 |
|---|---|
| 设置项 | 设置页 → `Overlay Idle Clear` |
| 范围 | `0 ~ 600` 秒，`0` = 关闭该功能 |
| 默认 | `30` 秒 |
| 持久化 | `setting.json` → `OverlayWindow.IdleClearSeconds` |

> ⚠️ 注意与「隐藏窗口」的区别：这个功能**不隐藏窗口**，只是把文字擦掉。窗口位置、大小、背景透明度全部保持原样。

实现要点（`src/windows/OverlayWindow.xaml.cs`）：
- 隐藏的是 `TranslatedCaptionDecorator` / `OriginalCaptionDecorator`，**不能隐藏 `TextBlock` 本身**——`StrokeDecorator`（文字描边层）会自己重绘，只隐藏 TextBlock 会留下一层描边残影。
- 用 `Visibility.Hidden` 而不是 `Collapsed`，保住布局高度，否则窗口会塌缩。
- 计时基准在 UI 线程用 `Dispatcher.BeginInvoke` 重置，避免跨线程写字段。

### 2. 主窗口：LiveCaptions 语音语言切换

**问题**：Windows 11 的「实时辅助字幕」（LiveCaptions）听什么语言，只能在它自己的设置菜单里点，每次换片种都要翻两层菜单。

**做法**：在 LCT 主窗口标题栏右侧加一个下拉框，直接切 `English` / `日本語`，约 3~5 秒完成。

| 项 | 值 |
|---|---|
| 控件位置 | 主窗口标题栏 → 字幕日志按钮左侧 |
| 持久化 | `setting.json` → `MainWindow.SpokenLanguage` |
| 可选值 | `英语(美国)` / `日语(日本)`（BCP-47：`en-US` / `ja-JP`） |

> ⚠️ 这是**语音识别语言**（LiveCaptions 听什么），不是**翻译目标语言**（设置页的 `Target Language`）。两者互不影响。

实现要点（`src/utils/LiveCaptionsLanguageHandler.cs`）：
- Windows 11 24H2 起，这个语言**没有公开的编程接口**，注册表（`HKCU\Software\Microsoft\LiveCaptions\UI\CaptionLanguage`）只在 LC 启动时读取、正常退出时写回，不能作为实时开关。
- 所以走 UIA 模拟点击：`SettingsButton` → `ChangeLanguageMenuFlyoutItem` → `SpeechModelDropDown`（17 个 `ListItem`）→ `ContinueButton`。
- Flyout 只在窗口处于前台时才展开，因此切换前必须先 `RestoreLiveCaptions`（还原窗口 + 抢前台）。
- **切换完成后要把窗口的隐藏状态还回去**：进门前记 `IsIconic(hWnd) || (exStyle & WS_EX_TOOLWINDOW) != 0`，在 `finally` 里还原。否则本来藏着的字幕窗会一直留在屏幕上。

### 3. 提示词随语言自动同步

提示词里写着「输入是**日语**语音识别字幕」，切到英语却还用日语版，模型会被误导。

切换语言时，如果当前提示词是内置的那两套之一（或为空），就自动换成对应语言的版本；**用户自己改过的提示词一律不动**。

### 4. 稳定性修复

| 位置 | 问题 | 修法 |
|---|---|---|
| `Translator.SyncLoop` | LC 重建 UI 后只捕 `ElementNotAvailableException`，其它异常会让采集线程直接死掉 | 补通用 `catch`，置 `Window = null` 让主循环重连 |
| `Translator.TranslateLoop` | 重启 LC 失败会反复抛异常 | 包 `try/catch` + 退避 2 秒 |
| `LiveCaptionsLanguageHandler` | 切换后仍用旧的 `Translator.Window` 引用 | 重新 `FindLiveCaptionsWindow()` + `ResetCaptionCache()` |
| `App` | 崩溃时无任何痕迹 | 挂 `UnhandledException` / `DispatcherUnhandledException` / `UnobservedTaskException`，写日志 |

---

## 二、本地部署（Windows 11）

### 2.1 目录结构

```
D:\tools\LiveCaptionsTranslator\
├─ LiveCaptionsTranslator-win-x64-withruntime.exe     ← LCT 主程序（本 fork 构建产物）
├─ setting.json                                       ← 配置：API 端点、提示词、窗口状态
├─ 启动翻译引擎.bat                                    ← 双击入口（调同名 .ps1）
├─ 启动翻译引擎.ps1                                    ← 起引擎 + 等就绪 + 拉 LCT
├─ 停止翻译引擎.bat / .ps1                             ← 兜底停引擎
├─ translation_history.db                             ← 翻译历史（SQLite）
└─ llama.cpp\
   ├─ llama-server.exe                                ← 推理服务
   ├─ ggml-vulkan.dll 等                              ← Vulkan 后端
   ├─ ggml-cpu-*.dll                                  ← CPU 多微架构后端（运行时自动挑）
   ├─ models\
   │  └─ Hy-MT2-1.8B-Q4_K_M.gguf                      ← 翻译模型，1080.6 MB
   └─ logs\
```

> 目录名、盘符都可以换，脚本用 `$PSScriptRoot` 自动定位，不写死路径。

### 2.2 翻译引擎：llama.cpp + Hy-MT2

| 项 | 值 |
|---|---|
| 推理框架 | llama.cpp（预编译 Windows x86_64 版） |
| 版本 | `0.9.0-dev`，build `12212`，commit `d78f4255d` |
| 编译器 | Clang 20.1.8 |
| 模型 | 腾讯混元 **Hy-MT2-1.8B**，`Q4_K_M` 量化 |
| 模型文件 | `models\Hy-MT2-1.8B-Q4_K_M.gguf`（1080.6 MB） |
| 后端 | Vulkan（`ggml-vulkan.dll` 约 41 MB）+ CPU 多微架构 DLL |
| 服务地址 | `http://127.0.0.1:57530` |
| 接口 | OpenAI 兼容（`/v1/chat/completions`、`/v1/models`） |

模型只做**翻译**这一件事，1.8B 足够；Q4_K_M 在 1080 MB 左右，核显也能全量放进显存。

### 2.3 启动参数逐项说明

```powershell
llama-server.exe `
    --device Vulkan0 `
    -m "models\Hy-MT2-1.8B-Q4_K_M.gguf" `
    --host 127.0.0.1 --port 57530 `
    -c 2048 -np 1 -ngl 99 -fa on `
    --log-colors off
```

| 参数 | 含义 | 为什么这么设 |
|---|---|---|
| `--device Vulkan0` | 用第一块 Vulkan 设备 | AMD 核显（7840H）走 Vulkan 比纯 CPU 快一个量级；NVIDIA 可换成 `CUDA0` |
| `-m <path>` | 模型路径 | 换 7B 只改这里：`Hy-MT2-7B-Q4_K_M.gguf` |
| `--host 127.0.0.1` | 只监听本机回环 | 不把翻译服务暴露到局域网 |
| `--port 57530` | 端口 | 避开常用端口；LCT 侧要一致 |
| `-c 2048` | 上下文窗口 2048 token | 每次只送一两句字幕，2048 绰绰有余；开太大白占显存 |
| `-np 1` | 1 个并行槽 | 单路字幕流，多槽只浪费显存 |
| `-ngl 99` | 全部层放 GPU | 1.8B Q4 约 1 GB，核显放得下；放不下就调小 |
| `-fa on` | 开启 Flash Attention | 省显存、提速，Vulkan 后端支持 |
| `--log-colors off` | 关闭 ANSI 颜色 | Windows 控制台不乱码，日志好读 |

### 2.4 LCT 侧的 API 配置

设置页 → `API` 选 `OpenAI`（OpenAI 兼容协议），然后填：

| 字段 | 值 |
|---|---|
| `ApiUrl` | `http://127.0.0.1:57530/v1/chat/completions` |
| `ModelName` | `hy-mt2` |
| `ApiKey` | 留空（本地服务不校验） |
| `Temperature` | `0`（翻译要稳定，不要发挥） |

对应 `setting.json`：

```json
{
  "ApiName": "OpenAI",
  "TargetLanguage": "zh-CN",
  "Configs": {
    "OpenAI": [
      {
        "ApiUrl": "http://127.0.0.1:57530/v1/chat/completions",
        "ModelName": "hy-mt2",
        "ApiKey": "",
        "Temperature": 0
      }
    ]
  }
}
```

### 2.5 脚本从哪来（仓库的 `deploy/`）

仓库 `deploy/` 里放着这四个可直接使用的脚本。**要复制到 2.1 节的部署根目录**（和 `LiveCaptionsTranslator-*.exe`、`llama.cpp\` 同级），不要在 `deploy\` 子目录里直接双击——`.ps1` 用 `$PSScriptRoot` 定位模型和 exe，放在子目录会找不到。

| 文件 | 编码 | 作用 |
|---|---|---|
| `启动翻译引擎.bat` | GBK + CRLF | 双击入口，新开窗口调同名 ps1 |
| `启动翻译引擎.ps1` | UTF-8 BOM + CRLF | 起引擎 + 等接口就绪 + 拉起 LCT |
| `停止翻译引擎.bat` | GBK + CRLF | 兜底停引擎入口 |
| `停止翻译引擎.ps1` | UTF-8 BOM + CRLF | 按进程名强杀 `llama-server` |

> ⚠️ 这四个文件**不要改编码**：`.bat` 用 GBK 是给 cmd 的中文代码页用的，存成 UTF-8 双击就乱码；`.ps1` 带 BOM 是给 Windows PowerShell 5.1 用的，去掉 BOM 中文字符串会全乱。仓库的 `.gitattributes` 已把 `deploy/` 标为 `-text`，保证 git 原样保存字节、不做换行符归一化。

---

## 三、启动与停止

### 3.1 `启动翻译引擎.bat`（一键启动）

双击后依次发生：

1. 打开一个 PowerShell 窗口，标题是「Hy-MT2 翻译引擎 · 关掉本窗口即停止」——**这个窗口就是引擎本体，窗口在 = 引擎在跑**。
2. 先检查 `llama-server` 是否已在运行。已在跑就只补拉 LCT，5 秒后自动关掉本窗口（避免占两个端口）。
3. 起一个后台 Job，每秒探一次 `http://127.0.0.1:57530/v1/models`。
4. 前台启动 `llama-server`（阻塞）。
5. 接口一旦就绪，后台 Job 立刻拉起 `LiveCaptionsTranslator`。

**为什么第 3 步必须用 `Start-Job`**：`llama-server` 是前台阻塞的，同一个线程里等端口会永远等不到——必须开独立进程去探。

### 3.2 `停止翻译引擎.bat`（兜底停止）

正常情况下**直接关掉引擎窗口**就行，关窗口即停引擎、释放显存。这个脚本用于窗口找不到了（比如被误关、或 `llama-server` 变成孤儿进程）的时候，按进程名强杀。

### 3.3 启动脚本核心片段

`启动翻译引擎.ps1`（节选，完整脚本见 `deploy/启动翻译引擎.ps1`）：

```powershell
param(
    [string]$Model = 'Hy-MT2-1.8B-Q4_K_M.gguf'   # 换 7B：-Model Hy-MT2-7B-Q4_K_M.gguf
)

$root   = if ($PSScriptRoot) { $PSScriptRoot } else { 'D:\tools\LiveCaptionsTranslator' }
$dir    = Join-Path $root 'llama.cpp'
$lctExe = Join-Path $root 'LiveCaptionsTranslator-win-x64-withruntime.exe'
$apiUrl = 'http://127.0.0.1:57530/v1/models'

# 引擎已在跑 → 只补拉 LCT，然后 5 秒后关闭本窗口
$exist = Get-Process llama-server -ErrorAction SilentlyContinue | Select-Object -First 1
if ($exist) { Start-Lct; Start-Sleep -Seconds 5; return }

# 后台盯端口，接口一就绪就起 LCT
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
```

> `$wc.Proxy = $null` 这一行不能省：系统挂了全局代理时，`127.0.0.1` 的探测请求也会被代理接管，导致永远探不到就绪。

### 3.4 为什么 `启动翻译引擎.bat` 不是直接写命令

`.bat` 里含中文（路径、窗口标题），而 cmd 在中文代码页下按 GBK 读文件。编辑器一存就是 UTF-8，双击就会乱码、找不到文件、闪一下就没了。

所以约定：`.bat` 只负责**在新窗口里调同名 `.ps1`**，编码固定 **GBK + CRLF、不加 `chcp`**（切代码页会把后面的中文行拆断）；`.ps1` 用 **UTF-8 带 BOM + CRLF**（Windows PowerShell 5.1 读无 BOM 的 ps1 会按 GBK 解释，中文字符串全乱）。

`启动翻译引擎.bat` 正文（注释略）：

```bat
@echo off
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
```

`start` 新开的那个 PowerShell 窗口就是服务本体；优先用 pwsh 7，没装就退回 5.1。`%~dp0` 保证从任何工作目录双击都能定位到脚本所在目录。

---

## 四、从源码构建

需要 .NET 8 SDK。

```powershell
dotnet publish LiveCaptionsTranslator.csproj `
    -c Release -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -o publish
```

产物：`publish\LiveCaptionsTranslator.exe`（约 72 MB）。

| 参数 | 不写会怎样 |
|---|---|
| `--self-contained true` | 只有 8.5 MB，但目标机器必须装 .NET 8 Desktop Runtime |
| `-p:EnableCompressionInSingleFile=true` | 体积从 72 MB 涨到 162 MB |

> 构建时如果目标 exe 正在运行，MSBuild 会报文件占用。**判断成功与否看 `LastWriteTime`，不要只看退出码**——它可能带着错误码返回旧的 exe。

---

## 五、使用要点

1. **启动顺序**：先 `启动翻译引擎.bat`（引擎 + LCT 一起起来），不要单独开 LCT——LCT 启动时会杀掉所有 `LiveCaptions` 进程并重启，引擎没起的话翻译会报连接错误。
2. **LCT 启动后 LiveCaptions 窗口是隐藏的**，这是原版设计：`Translator` 静态构造函数里就调了 `HideLiveCaptions`。点 LCT 里的 `Show` 才显示。
3. **换片种先切语言**：主窗口标题栏下拉框选 `English` 或 `日本語`，等 3~5 秒（LC 会重建 UI），提示词自动跟着换。
4. **悬浮窗空闲清空**：设置页 `Overlay Idle Clear`，默认 30 秒，嫌短就调大，不想清就设 0。
5. **关引擎**：直接关掉那个 PowerShell 窗口，显存立刻释放。

---

## 六、已知限制

| 限制 | 说明 |
|---|---|
| 只支持 Windows 11 | 依赖系统自带的「实时辅助字幕」（LiveCaptions），Windows 10 没有 |
| 语言切换靠 UIA 模拟点击 | 微软没有公开 API；LC 大版本更新后菜单的 AutomationId 可能变，需要改 `LiveCaptionsLanguageHandler` |
| 切换时 LC 窗口会闪一下 | 必须先把它调到前台才能展开 Flyout，切完再藏回去 |
| 语言列表只有 17 项 | 取自 LC 自己的 `SpeechModelDropDown`，取决于系统装了哪些语音包 |
| 模型 1.8B | 长句、专业术语偶有生硬；要质量可换 7B（改 `-Model` 参数，显存需求约 4.5 GB） |

---

## 七、与上游同步

本仓库的 `origin` 指向 fork（`ClutchBear/LiveCaptions-Translator`），日常 `git push` 就是推自己的 fork；`upstream` 指向原作者 `SakiRinn/LiveCaptions-Translator`，只用来拉更新。

```bash
git remote add upstream https://github.com/SakiRinn/LiveCaptions-Translator.git   # 仅首次
git fetch upstream
git merge upstream/master
```

改动集中在这些文件，冲突范围可控：

```
src/utils/LiveCaptionsLanguageHandler.cs   （新增）
src/windows/OverlayWindow.xaml.cs          （空闲清空）
src/windows/MainWindow.xaml / .xaml.cs     （语言下拉框 + 提示词同步）
src/models/WindowState.cs                  （两个新配置项）
src/pages/SettingPage.xaml                 （Overlay Idle Clear 输入框）
src/apis/WindowsAPI.cs                     （IsIconic）
```

---

## 许可

沿用上游 [LICENSE](LICENSE)。
