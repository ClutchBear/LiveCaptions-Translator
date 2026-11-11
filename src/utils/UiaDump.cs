using System.IO;
using System.Text;
using System.Windows.Automation;

namespace LiveCaptionsTranslator.Utils
{
    /// <summary>
    /// 临时诊断工具：把 LiveCaptions 的 UI Automation 树 dump 到 uia-dump.log。
    /// 用于定位「听的语言」下拉框的 AutomationId。排查完可删。
    /// </summary>
    public static class UiaDump
    {
        private static readonly string logPath = Path.Combine(
            Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory,
            "uia-dump.log");

        public static void Write(string message)
        {
            try
            {
                File.AppendAllText(logPath,
                    $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}", Encoding.UTF8);
            }
            catch { }
        }

        public static void Dump(AutomationElement root, string tag)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"================ {tag} ================");
            try
            {
                Walk(root, 0, sb);
            }
            catch (Exception ex)
            {
                sb.AppendLine("DUMP FAILED: " + ex);
            }
            Write(sb.ToString());
        }

        /// <summary>
        /// 诊断入口：接管（或启动）LiveCaptions，dump 主窗口 + 点开设置后的所有顶层窗口。
        /// </summary>
        public static void DumpLiveCaptions()
        {
            try
            {
                var processes = System.Diagnostics.Process.GetProcessesByName("LiveCaptions");
                if (processes.Length == 0)
                {
                    Write("LiveCaptions 未运行，尝试启动…");
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("LiveCaptions")
                        {
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        Write("启动失败: " + ex.Message);
                    }
                    System.Threading.Thread.Sleep(5000);
                    processes = System.Diagnostics.Process.GetProcessesByName("LiveCaptions");
                }

                if (processes.Length == 0)
                {
                    Write("没有找到 LiveCaptions 进程，放弃。");
                    return;
                }

                Write($"LiveCaptions 进程数 = {processes.Length}");

                AutomationElement? lcWindow = null;
                foreach (var process in processes)
                {
                    var condition = new PropertyCondition(
                        AutomationElement.ProcessIdProperty, process.Id);
                    var windows = AutomationElement.RootElement.FindAll(
                        TreeScope.Children, condition);
                    foreach (AutomationElement window in windows)
                    {
                        if (window.Current.ClassName == "LiveCaptionsDesktopWindow")
                        {
                            lcWindow = window;
                            break;
                        }
                    }
                    if (lcWindow != null)
                        break;
                }

                if (lcWindow == null)
                {
                    Write("没找到 LiveCaptionsDesktopWindow。");
                    return;
                }

                int pid = lcWindow.Current.ProcessId;
                Write($"找到主窗口 pid={pid}");

                Dump(lcWindow, "MAIN");

                // Flyout 只在窗口处于前台时才展开，先把 LC 置前
                LiveCaptionsTranslator.utils.LiveCaptionsHandler.RestoreLiveCaptions(lcWindow);
                System.Threading.Thread.Sleep(1000);

                Write("click settings: " +
                      LiveCaptionsTranslator.utils.LiveCaptionsHandler.ClickSettingsButton(lcWindow));
                System.Threading.Thread.Sleep(3000);
                DumpAllTopLevelWindows(pid, "SETTINGS");

                // 再全局扫一遍（Flyout 可能挂在别的进程下）
                DumpGlobalWindows();

                // 继续点开「更改语言」，看语言列表
                var langItem = WaitForAId(lcWindow, "ChangeLanguageMenuFlyoutItem", 6000);
                if (langItem == null)
                {
                    Write("第一次没等到菜单，重新点设置按钮再等…");
                    LiveCaptionsTranslator.utils.LiveCaptionsHandler.RestoreLiveCaptions(lcWindow);
                    System.Threading.Thread.Sleep(500);
                    LiveCaptionsTranslator.utils.LiveCaptionsHandler.ClickSettingsButton(lcWindow);
                    langItem = WaitForAId(lcWindow, "ChangeLanguageMenuFlyoutItem", 8000);
                }

                if (langItem == null)
                {
                    Write("没找到 ChangeLanguageMenuFlyoutItem");
                    return;
                }

                Write("找到 ChangeLanguageMenuFlyoutItem，支持的模式: " +
                      string.Join(",", langItem.GetSupportedPatterns().Select(p => p.ProgrammaticName)));

                string invoked = "no pattern";
                try
                {
                    if (langItem.TryGetCurrentPattern(InvokePattern.Pattern, out object? invokeObj)
                        && invokeObj is InvokePattern invokePattern)
                    {
                        invokePattern.Invoke();
                        invoked = "Invoke ok";
                    }
                    else if (langItem.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object? expandObj)
                             && expandObj is ExpandCollapsePattern expandPattern)
                    {
                        expandPattern.Expand();
                        invoked = "Expand ok";
                    }
                }
                catch (Exception ex)
                {
                    invoked = "failed: " + ex.Message;
                }

                Write("click ChangeLanguage: " + invoked);
                System.Threading.Thread.Sleep(2500);
                DumpAllTopLevelWindows(pid, "LANGUAGE");

                // 展开语言下拉框，看有哪些可选语言
                var combo = WaitForAId(lcWindow, "SpeechModelDropDown", 6000);
                if (combo == null)
                {
                    Write("没找到 SpeechModelDropDown");
                    return;
                }

                try
                {
                    if (combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object? ecObj)
                        && ecObj is ExpandCollapsePattern ec)
                    {
                        ec.Expand();
                        Write("expand combo ok");
                        System.Threading.Thread.Sleep(2500);
                        DumpAllTopLevelWindows(pid, "LANG_EXPANDED");
                        Dump(combo, "COMBO_SUBTREE");
                        ec.Collapse();
                        Write("collapse combo ok");
                    }
                }
                catch (Exception ex)
                {
                    Write("expand combo failed: " + ex.Message);
                }

                // 恢复：点「继续」回到正常识别状态
                System.Threading.Thread.Sleep(800);
                var continueButton = WaitForAId(lcWindow, "ContinueButton", 3000);
                if (continueButton != null)
                {
                    if (continueButton.TryGetCurrentPattern(InvokePattern.Pattern, out object? invObj)
                        && invObj is InvokePattern inv)
                    {
                        inv.Invoke();
                        Write("clicked ContinueButton (restored)");
                    }
                }
                else
                {
                    Write("没找到 ContinueButton");
                }
            }
            catch (Exception ex)
            {
                Write("DUMP FAILED: " + ex);
            }
        }

        /// <summary>
        /// 诊断：切换语音语言并回读 LiveCaptions 的当前状态。
        /// </summary>
        public static void TestSwitchLanguage(string languageName)
        {
            string error = LiveCaptionsTranslator.utils.LiveCaptionsLanguageHandler.Switch(languageName);
            Write($"[test-lang] target='{languageName}' result='{(string.IsNullOrEmpty(error) ? "OK" : error)}'");

            System.Threading.Thread.Sleep(2500);
            DumpCurrentState("AFTER_SWITCH");
        }

        public static void DumpCurrentState(string tag)
        {
            var window = FindLiveCaptionsWindow();
            if (window == null)
            {
                Write(tag + ": 没有找到 LiveCaptions 窗口");
                return;
            }

            // 只读关键元素，避免日志过长
            try
            {
                var ready = LiveCaptionsTranslator.utils.LiveCaptionsHandler.FindElementByAId(
                    window, "ReadyToCaptionTextBlock");
                var selection = LiveCaptionsTranslator.utils.LiveCaptionsHandler.FindElementByAId(
                    window, "SelectionReadyToCaptionTextBlock");
                var captions = LiveCaptionsTranslator.utils.LiveCaptionsHandler.FindElementByAId(
                    window, "CaptionsTextBlock");

                Write($"{tag}: pid={window.Current.ProcessId} Ready='{ready?.Current.Name}' " +
                      $"Selection='{selection?.Current.Name}' " +
                      $"Captions='{Truncate(captions?.Current.Name)}'");
            }
            catch (Exception ex)
            {
                Write(tag + " 读取失败: " + ex.Message);
            }
        }

        private static string Truncate(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? "";
            return text.Length > 60 ? text[..60] + "…" : text;
        }

        private static AutomationElement? FindLiveCaptionsWindow()
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName("LiveCaptions"))
            {
                var condition = new PropertyCondition(
                    AutomationElement.ProcessIdProperty, process.Id);
                var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, condition);
                foreach (AutomationElement window in windows)
                {
                    if (window.Current.ClassName == "LiveCaptionsDesktopWindow")
                        return window;
                }
            }
            return null;
        }

        private static AutomationElement? WaitForAId(
            AutomationElement root, string automationId, int timeoutMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                var element = LiveCaptionsTranslator.utils.LiveCaptionsHandler.FindElementByAId(
                    root, automationId);
                if (element != null)
                    return element;
                System.Threading.Thread.Sleep(150);
            }
            return null;
        }

        /// <summary>
        /// 全局扫描：列出 RootElement 下所有顶层窗口的 class / name / pid，
        /// 用于找 Flyout 这类可能挂在其他进程下的弹出窗口。
        /// </summary>
        public static void DumpGlobalWindows()
        {
            var sb = new StringBuilder();
            sb.AppendLine("================ GLOBAL WINDOWS ================");
            try
            {
                var windows = AutomationElement.RootElement.FindAll(
                    TreeScope.Children, Condition.TrueCondition);
                sb.AppendLine($"共 {windows.Count} 个顶层窗口");
                foreach (AutomationElement window in windows)
                {
                    try
                    {
                        sb.AppendLine($"  class='{window.Current.ClassName}' name='{window.Current.Name}' " +
                                      $"pid={window.Current.ProcessId}");
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("FAILED: " + ex);
            }
            Write(sb.ToString());
        }

        /// <summary>
        /// 枚举该进程的**所有顶层窗口**（设置面板是独立的 Xaml_WindowedPopupClass，
        /// 不在主窗口子树里，必须从 RootElement 按 ProcessId 找）。
        /// </summary>
        public static void DumpAllTopLevelWindows(int processId, string tag)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"================ {tag} (all windows) ================");
            try
            {
                var condition = new PropertyCondition(
                    AutomationElement.ProcessIdProperty, processId);
                var windows = AutomationElement.RootElement.FindAll(
                    TreeScope.Children, condition);

                sb.AppendLine($"共 {windows.Count} 个顶层窗口");
                foreach (AutomationElement window in windows)
                {
                    string cls, name;
                    try
                    {
                        cls = window.Current.ClassName ?? "";
                        name = window.Current.Name ?? "";
                    }
                    catch { continue; }

                    sb.AppendLine($"--- window class='{cls}' name='{name}' ---");
                    Walk(window, 1, sb);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("DUMP FAILED: " + ex);
            }
            Write(sb.ToString());
        }

        private static void Walk(AutomationElement element, int depth, StringBuilder sb)
        {
            if (depth > 14)
                return;

            string aid, name, cls, controlType;
            try
            {
                aid = element.Current.AutomationId ?? "";
                name = element.Current.Name ?? "";
                cls = element.Current.ClassName ?? "";
                controlType = element.Current.ControlType?.ProgrammaticName ?? "";
            }
            catch
            {
                return;   // 元素已失效
            }

            if (name.Length > 80)
                name = name[..80] + "…";

            sb.AppendLine($"{new string(' ', depth * 2)}<{controlType}> aid='{aid}' name='{name}' class='{cls}'");

            AutomationElement? child;
            try
            {
                child = TreeWalker.RawViewWalker.GetFirstChild(element);
            }
            catch
            {
                return;
            }

            while (child != null)
            {
                Walk(child, depth + 1, sb);
                try
                {
                    child = TreeWalker.RawViewWalker.GetNextSibling(child);
                }
                catch
                {
                    break;
                }
            }
        }
    }
}
