using System.Diagnostics;
using System.Windows.Automation;

using LiveCaptionsTranslator.apis;

namespace LiveCaptionsTranslator.utils
{
    /// <summary>
    /// 切换 LiveCaptions 的「实时字幕语言」——也就是它听什么语言（英语 / 日语）。
    ///
    /// Windows 11 24H2 起，这个语言在系统设置里没有可编程入口，注册表也没有，
    /// 只能操作 LiveCaptions 自己的 UIA 菜单：
    ///   设置按钮 → 更改语言 → 实时字幕语言(SpeechModelDropDown) → 选语言 → 继续
    /// </summary>
    public static class LiveCaptionsLanguageHandler
    {
        public const string EnglishUS = "英语(美国)";
        public const string Japanese = "日语(日本)";

        /// <summary>
        /// 切换语音识别语言。成功返回空字符串，失败返回给用户看的原因。
        /// 注意：这是阻塞操作（约 3~5 秒），必须在后台线程调用。
        /// </summary>
        public static string Switch(string languageName)
        {
            bool wasHidden = false;
            try
            {
                var window = FindLiveCaptionsWindow();
                if (window == null)
                    return "LiveCaptions 没有在运行。请先点上方的 Show 启动它，再切换语言。";

                // 记住切换前的隐藏状态。展开设置菜单必须先把窗口调到前台，
                // 但用户本来就把它藏着的话，切完得还回去，不能让它一直挂在屏幕上。
                wasHidden = IsLiveCaptionsHidden(window);
                LiveCaptionsTranslator.Utils.OverlayDiag.Log(
                    $"[lang] 开始, 目标={languageName}, wasHidden={wasHidden}");

                // 刚启动的 LiveCaptions 界面还没就绪，点设置不会弹出菜单，先等它准备好
                WaitUntilReady(window);

                // Flyout 只在窗口处于前台时才会展开
                LiveCaptionsHandler.RestoreLiveCaptions(window);
                System.Threading.Thread.Sleep(400);

                if (!LiveCaptionsHandler.ClickSettingsButton(window))
                    return "没找到 LiveCaptions 的设置按钮。";

                var changeLanguageItem = WaitForAId(window, "ChangeLanguageMenuFlyoutItem", 4000);
                if (changeLanguageItem == null)
                {
                    // 菜单偶尔不弹，再点一次
                    LiveCaptionsHandler.RestoreLiveCaptions(window);
                    System.Threading.Thread.Sleep(600);
                    LiveCaptionsHandler.ClickSettingsButton(window);
                    changeLanguageItem = WaitForAId(window, "ChangeLanguageMenuFlyoutItem", 5000);
                }
                if (changeLanguageItem == null)
                    return "设置菜单没有弹出来，请再试一次。";

                Invoke(changeLanguageItem);

                var combo = WaitForAId(window, "SpeechModelDropDown", 6000);
                if (combo == null)
                    return "没能进入语言选择界面，请再试一次。";

                ExpandCollapsePattern? expandCollapse = null;
                if (combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object? ecObj)
                    && ecObj is ExpandCollapsePattern ec)
                {
                    expandCollapse = ec;
                    ec.Expand();
                    System.Threading.Thread.Sleep(700);
                }

                var item = FindListItem(combo, languageName);
                if (item == null)
                {
                    expandCollapse?.Collapse();
                    return $"语言列表里没有「{languageName}」。";
                }

                if (!item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? siObj)
                    || siObj is not SelectionItemPattern selectionItem)
                {
                    expandCollapse?.Collapse();
                    return "这个语言项不支持选择操作。";
                }

                selectionItem.Select();
                System.Threading.Thread.Sleep(300);
                expandCollapse?.Collapse();

                var continueButton = WaitForAId(window, "ContinueButton", 4000);
                if (continueButton == null)
                    return "没找到「继续」按钮，LiveCaptions 可能停在语言选择界面，请手动点一下继续。";

                Invoke(continueButton);

                // 切换会让 LiveCaptions 重建内部 UI 元素：
                // 1) 清掉缓存的字幕元素，让 GetCaptions 重新查找；
                // 2) 重新查找并刷新 Translator.Window —— 不能直接用切换前那个引用，
                //    万一 LC 在这期间被重启，旧引用已失效，会让 TranslateLoop 反复重启 LC。
                System.Threading.Thread.Sleep(600);
                LiveCaptionsHandler.ResetCaptionCache();
                LiveCaptionsTranslator.Translator.Window = FindLiveCaptionsWindow();
                LiveCaptionsTranslator.Utils.OverlayDiag.Log(
                    "[lang] 切换完成, LC 窗口=" +
                    (LiveCaptionsTranslator.Translator.Window == null ? "null" : "ok"));

                return string.Empty;
            }
            catch (Exception ex)
            {
                return "切换语言失败：" + ex.Message;
            }
            finally
            {
                // 展开 LiveCaptions 的菜单必须先把窗口调到前台，所以上面无条件
                // RestoreLiveCaptions 过。用户原本把它藏着的话，这里必须还回去，
                // 否则切完它就永远留在屏幕上了。放在 finally 是为了中途失败也能还原。
                LiveCaptionsTranslator.Utils.OverlayDiag.Log($"[lang] finally, wasHidden={wasHidden}");
                if (wasHidden)
                    RestoreHiddenState();
            }
        }

        /// <summary>
        /// 判断 LiveCaptions 当前是否处于「隐藏」状态。
        /// 隐藏 = 最小化 + 带 WS_EX_TOOLWINDOW（从 Alt+Tab 里消失），两者任一成立即算隐藏。
        /// </summary>
        private static bool IsLiveCaptionsHidden(AutomationElement window)
        {
            nint hWnd = new nint((long)window.Current.NativeWindowHandle);
            if (hWnd == nint.Zero)
                return false;

            if (WindowsAPI.IsIconic(hWnd))
                return true;

            int exStyle = WindowsAPI.GetWindowLong(hWnd, WindowsAPI.GWL_EXSTYLE);
            return (exStyle & WindowsAPI.WS_EX_TOOLWINDOW) != 0;
        }

        /// <summary>
        /// 把 LiveCaptions 重新藏起来。窗口句柄可能在切换过程中重建，所以重新查一次。
        /// </summary>
        private static void RestoreHiddenState()
        {
            try
            {
                var window = LiveCaptionsTranslator.Translator.Window ?? FindLiveCaptionsWindow();
                if (window == null)
                    return;
                LiveCaptionsHandler.HideLiveCaptions(window);
            }
            catch
            {
                // 还原失败不该盖掉切换本身的结果
            }
        }

        /// <summary>
        /// 等 LiveCaptions 界面就绪。刚启动时它还在加载，此时点设置不会弹出菜单。
        /// 判据：主界面出现「已准备好在…」或「实时字幕语言」或已有字幕元素。
        /// </summary>
        private static void WaitUntilReady(AutomationElement window)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                if (LiveCaptionsHandler.FindElementByAId(window, "ReadyToCaptionTextBlock") != null
                    || LiveCaptionsHandler.FindElementByAId(window, "SelectionReadyToCaptionTextBlock") != null
                    || LiveCaptionsHandler.FindElementByAId(window, "CaptionsTextBlock") != null)
                    return;
                System.Threading.Thread.Sleep(300);
            }
        }

        private static AutomationElement? FindLiveCaptionsWindow()
        {
            foreach (var process in Process.GetProcessesByName(LiveCaptionsHandler.PROCESS_NAME))
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
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                var element = LiveCaptionsHandler.FindElementByAId(root, automationId);
                if (element != null)
                    return element;
                System.Threading.Thread.Sleep(150);
            }
            return null;
        }

        private static AutomationElement? FindListItem(AutomationElement combo, string name)
        {
            var condition = new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(AutomationElement.NameProperty, name));
            return combo.FindFirst(TreeScope.Descendants, condition);
        }

        private static void Invoke(AutomationElement element)
        {
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out object? obj)
                && obj is InvokePattern invokePattern)
                invokePattern.Invoke();
        }
    }
}
