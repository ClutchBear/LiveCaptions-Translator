using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

using LiveCaptionsTranslator.utils;
using LiveCaptionsTranslator.Utils;
using Button = Wpf.Ui.Controls.Button;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using SelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;

namespace LiveCaptionsTranslator
{
    public partial class MainWindow : FluentWindow
    {
        public OverlayWindow? OverlayWindow { get; set; } = null;
        public bool IsAutoHeight { get; set; } = true;

        public MainWindow()
        {
            InitializeComponent();
            ApplicationThemeManager.ApplySystemTheme();

            Closing += (s, e) => OverlayDiag.Log("[main] Closing 事件");
            Closed += (s, e) => OverlayDiag.Log("[main] Closed 事件");

            Loaded += (s, e) =>
            {
                SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, true);
                RootNavigation.Navigate(typeof(CaptionPage));
                IsAutoHeight = true;

                // 语音识别语言：初始化选中项。赋值本身会触发 SelectionChanged，
                // 用标志挡住，否则一启动就会去切 LiveCaptions 的语言。
                SpokenLanguageBox.SelectedIndex =
                    Translator.Setting?.MainWindow.SpokenLanguage == LiveCaptionsLanguageHandler.Japanese
                        ? 1 : 0;
                spokenLanguageReady = true;

                CheckForFirstUse();
                CheckForUpdates();

                // 自测开关：--test-overlay 启动后自动点亮 CC 按钮，用于验证悬浮窗的空闲隐藏逻辑
                //           --test-overlay-reshow 额外在 35 秒后再点一次（模拟"隐藏后手动重新点开"）
                foreach (string arg in Environment.GetCommandLineArgs())
                {
                    if (string.Equals(arg, "--test-overlay", StringComparison.OrdinalIgnoreCase))
                    {
                        Dispatcher.InvokeAsync(
                            () => OverlayModeButton_Click(OverlayModeButton, new RoutedEventArgs()),
                            System.Windows.Threading.DispatcherPriority.Background);
                    }
                    else if (string.Equals(arg, "--test-setting", StringComparison.OrdinalIgnoreCase))
                    {
                        // 自测开关：直接跳到设置页，用于目视核对各列是否被裁切
                        Dispatcher.InvokeAsync(
                            () => RootNavigation.Navigate(typeof(SettingPage)),
                            System.Windows.Threading.DispatcherPriority.Background);
                    }
                    else if (arg.StartsWith("--test-spoken=", StringComparison.OrdinalIgnoreCase))
                    {
                        // 诊断：直接走一遍「切换语音语言」的完整 UI 路径（含提示词同步），
                        // 例：--test-spoken=日语(日本)
                        // 可加 @秒数 延后触发，便于先把 LiveCaptions 调成显示状态再切：
                        // 例：--test-spoken=en@20
                        string lang = arg["--test-spoken=".Length..];
                        int delaySeconds = 0;
                        int at = lang.IndexOf('@');
                        if (at > 0)
                        {
                            int.TryParse(lang[(at + 1)..], out delaySeconds);
                            lang = lang[..at];
                        }

                        void ApplySpoken()
                        {
                            SpokenLanguageBox.SelectedIndex =
                                lang == LiveCaptionsLanguageHandler.Japanese ? 1 : 0;
                        }

                        if (delaySeconds > 0)
                        {
                            var spokenTimer = new System.Windows.Threading.DispatcherTimer
                            {
                                Interval = TimeSpan.FromSeconds(delaySeconds)
                            };
                            spokenTimer.Tick += (ts, te) =>
                            {
                                spokenTimer.Stop();
                                OverlayDiag.Log($"[test] 延时到，切换语言 -> {lang}");
                                ApplySpoken();
                            };
                            spokenTimer.Start();
                        }
                        else
                        {
                            Dispatcher.InvokeAsync(ApplySpoken,
                                System.Windows.Threading.DispatcherPriority.Background);
                        }
                    }
                    else if (string.Equals(arg, "--dump-lc", StringComparison.OrdinalIgnoreCase))
                    {
                        // 诊断：dump LiveCaptions 的 UIA 树（含设置面板），用于定位语言下拉框
                        Task.Run(() => UiaDump.DumpLiveCaptions());
                    }
                    else if (arg.StartsWith("--test-lang=", StringComparison.OrdinalIgnoreCase))
                    {
                        // 诊断：切换语音语言并回读状态，例：--test-lang=日语(日本)
                        string target = arg["--test-lang=".Length..];
                        Task.Run(() => UiaDump.TestSwitchLanguage(target));
                    }
                    else if (string.Equals(arg, "--test-overlay-reshow", StringComparison.OrdinalIgnoreCase))
                    {
                        var reshowTimer = new System.Windows.Threading.DispatcherTimer
                        {
                            Interval = TimeSpan.FromSeconds(35)
                        };
                        reshowTimer.Tick += (ts, te) =>
                        {
                            reshowTimer.Stop();
                            OverlayDiag.Log("[test] trigger reshow click");
                            OverlayModeButton_Click(OverlayModeButton, new RoutedEventArgs());
                        };
                        reshowTimer.Start();
                    }
                }
            };

            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;

            var windowState = WindowHandler.LoadState(this, Translator.Setting);
            if (windowState.Left <= 0 || windowState.Left >= screenWidth ||
                windowState.Top <= 0 || windowState.Top >= screenHeight)
            {
                WindowHandler.RestoreState(this, new Rect(
                    (screenWidth - 775) / 2, screenHeight * 3 / 4 - 167, 775, 167));
            }
            else
                WindowHandler.RestoreState(this, windowState);

            ToggleTopmost(Translator.Setting.MainWindow.Topmost);
            ShowLogCard(Translator.Setting.MainWindow.CaptionLogEnabled);
        }

        private bool spokenLanguageReady = false;

        /// <summary>
        /// 切换 LiveCaptions 的语音识别语言（它「听」什么语言）。
        /// 这是去改 Windows 自带「实时辅助字幕」的设置，不是 LCT 自己的功能；
        /// 也不影响翻译目标语言（那是设置页的 Target Language）。
        /// </summary>
        private async void SpokenLanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!spokenLanguageReady)
                return;
            if (SpokenLanguageBox.SelectedItem is not ComboBoxItem item)
                return;

            string language = item.Tag as string ?? string.Empty;
            if (string.IsNullOrEmpty(language))
                return;

            // UIA 操作是阻塞的（3~5 秒），必须放后台线程，否则界面会卡住
            OverlayDiag.Log($"[spoken] 开始切换 -> {language}");
            string error;
            try
            {
                error = await Task.Run(() => LiveCaptionsLanguageHandler.Switch(language));
            }
            catch (Exception ex)
            {
                OverlayDiag.Log("[spoken] Switch 抛出未捕获异常: " + ex);
                error = "切换语言失败：" + ex.Message;
            }
            OverlayDiag.Log($"[spoken] 切换返回: '{error}'");

            if (string.IsNullOrEmpty(error))
            {
                if (Translator.Setting != null)
                {
                    Translator.Setting.MainWindow.SpokenLanguage = language;
                    ApplyPromptForLanguage(language);
                }
                OverlayDiag.Log("[spoken] 完成");
                return;
            }

            // 失败就把下拉框退回原来选中的语言
            spokenLanguageReady = false;
            SpokenLanguageBox.SelectedIndex =
                Translator.Setting?.MainWindow.SpokenLanguage == LiveCaptionsLanguageHandler.Japanese
                    ? 1 : 0;
            spokenLanguageReady = true;

            System.Windows.MessageBox.Show(error, "Spoken Language",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }

        // 语音语言和翻译提示词必须一起变：提示词里写着「输入是日语语音识别字幕」，
        // 切到英语却还用日语版，模型会被误导。只在这两句「自家标准提示词」之间替换，
        // 用户自己改过的提示词一律不碰。
        private static readonly Dictionary<string, string> PromptByLanguage = new()
        {
            [LiveCaptionsLanguageHandler.Japanese] =
                "你是实时字幕翻译器。输入是日语语音识别字幕，可能有错字或断句不全，请结合上下文判断。把每句译成自然流畅的简体中文口语。忽略其中的 🔤 符号，译文里也不要出现它。只输出译文，不要解释，不要加引号。",
            [LiveCaptionsLanguageHandler.EnglishUS] =
                "你是实时字幕翻译器。输入是英语语音识别字幕，可能有错字或断句不全，请结合上下文判断。把每句译成自然流畅的简体中文口语。忽略其中的 🔤 符号，译文里也不要出现它。只输出译文，不要解释，不要加引号。",
        };

        private void ApplyPromptForLanguage(string language)
        {
            var setting = Translator.Setting;
            if (setting == null)
                return;
            if (!PromptByLanguage.TryGetValue(language, out string? target))
                return;

            string current = setting.Prompt ?? string.Empty;
            bool isBuiltIn = string.IsNullOrWhiteSpace(current)
                             || current == PromptByLanguage[LiveCaptionsLanguageHandler.Japanese]
                             || current == PromptByLanguage[LiveCaptionsLanguageHandler.EnglishUS];
            if (!isBuiltIn || current == target)
                return;

            setting.Prompt = target;
            setting.Save();
        }

        private void TopmostButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleTopmost(!this.Topmost);
        }

        private void OverlayModeButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var symbolIcon = button?.Icon as SymbolIcon;

            if (OverlayWindow == null)
            {
                OverlayDiag.Log("[btn] branch=create");
                symbolIcon.Symbol = SymbolRegular.ClosedCaption24;
                symbolIcon.Filled = true;

                OverlayWindow = new OverlayWindow();
                OverlayWindow.SizeChanged +=
                    (s, e) => WindowHandler.SaveState(OverlayWindow, Translator.Setting);
                OverlayWindow.LocationChanged +=
                    (s, e) => WindowHandler.SaveState(OverlayWindow, Translator.Setting);

                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;

                var windowState = WindowHandler.LoadState(OverlayWindow, Translator.Setting);
                if (windowState.Left <= 0 || windowState.Left >= screenWidth ||
                    windowState.Top <= 0 || windowState.Top >= screenHeight)
                {
                    WindowHandler.RestoreState(OverlayWindow, new Rect(
                        (screenWidth - 650) / 2, screenHeight * 5 / 6 - 135, 650, 135));
                }
                else
                    WindowHandler.RestoreState(OverlayWindow, windowState);

                OverlayWindow.Show();
            }
            else if (!OverlayWindow.IsVisible)
            {
                // 被"空闲自动隐藏"收起来了 —— 按钮还显示开着，点一下先叫回来，不销毁
                OverlayDiag.Log("[btn] branch=reshow");
                OverlayWindow.Show();
            }
            else
            {
                OverlayDiag.LogWithStack("[btn] branch=CLOSE");
                symbolIcon.Symbol = SymbolRegular.ClosedCaptionOff24;
                symbolIcon.Filled = false;

                switch (OverlayWindow.OnlyMode)
                {
                    case CaptionVisible.TranslationOnly:
                        OverlayWindow.OnlyMode = CaptionVisible.SubtitleOnly;
                        OverlayWindow.OnlyMode = CaptionVisible.Both;
                        break;
                    case CaptionVisible.SubtitleOnly:
                        OverlayWindow.OnlyMode = CaptionVisible.Both;
                        break;
                }

                OverlayWindow.Close();
                OverlayWindow = null;
            }
        }

        private void LogOnlyButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var symbolIcon = button?.Icon as SymbolIcon;

            if (Translator.LogOnlyFlag)
            {
                Translator.LogOnlyFlag = false;
                symbolIcon.Filled = false;
            }
            else
            {
                Translator.LogOnlyFlag = true;
                symbolIcon.Filled = true;
            }

            Translator.ClearContexts();
        }

        private void CaptionLogButton_Click(object sender, RoutedEventArgs e)
        {
            Translator.Setting.MainWindow.CaptionLogEnabled = !Translator.Setting.MainWindow.CaptionLogEnabled;
            ShowLogCard(Translator.Setting.MainWindow.CaptionLogEnabled);
            CaptionPage.Instance?.AutoHeight();
        }

        private void MainWindow_LocationChanged(object sender, EventArgs e)
        {
            var window = sender as Window;
            WindowHandler.SaveState(window, Translator.Setting);
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            MainWindow_LocationChanged(sender, e);
            IsAutoHeight = false;
        }

        public void ToggleTopmost(bool enabled)
        {
            var button = TopmostButton as Button;
            var symbolIcon = button?.Icon as SymbolIcon;
            symbolIcon.Filled = enabled;
            this.Topmost = enabled;
            Translator.Setting.MainWindow.Topmost = enabled;
        }

        private void CheckForFirstUse()
        {
            if (!Translator.FirstUseFlag)
                return;

            RootNavigation.Navigate(typeof(SettingPage));
            LiveCaptionsHandler.RestoreLiveCaptions(Translator.Window);

            Dispatcher.InvokeAsync(() =>
            {
                var welcomeWindow = new WelcomeWindow
                {
                    Owner = this
                };
                welcomeWindow.Show();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private async Task CheckForUpdates()
        {
            if (Translator.FirstUseFlag)
                return;

            string latestVersion = string.Empty;
            try
            {
                latestVersion = await UpdateUtil.GetLatestVersion();
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] Update Check Failed.", ex.Message, SnackbarType.Error,
                    timeout: 2, closeButton: true);

                return;
            }

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
            var ignoredVersion = Translator.Setting.IgnoredUpdateVersion;
            if (!string.IsNullOrEmpty(ignoredVersion) && ignoredVersion == latestVersion)
                return;
            if (!string.IsNullOrEmpty(latestVersion) && latestVersion != currentVersion)
            {
                var dialog = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "New Version Available",
                    Content = $"A new version has been detected: {latestVersion}\n" +
                              $"Current version: {currentVersion}\n" +
                              $"Please visit GitHub to download the latest release.",
                    PrimaryButtonText = "Update",
                    CloseButtonText = "Ignore this version"
                };
                var result = await dialog.ShowDialogAsync();

                if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                    var url = UpdateUtil.GitHubReleasesUrl;
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        SnackbarHost.Show("[ERROR] Open Browser Failed.", ex.Message, SnackbarType.Error,
                            timeout: 2, closeButton: true);
                    }
                }
                else
                    Translator.Setting.IgnoredUpdateVersion = latestVersion;
            }
        }

        public void ShowLogCard(bool enabled)
        {
            if (CaptionLogButton.Icon is SymbolIcon icon)
            {
                if (enabled)
                    icon.Symbol = SymbolRegular.History24;
                else
                    icon.Symbol = SymbolRegular.HistoryDismiss24;
                CaptionPage.Instance?.CollapseTranslatedCaption(enabled);
            }
        }

        public void AutoHeightAdjust(int minHeight = -1, int maxHeight = -1)
        {
            if (minHeight > 0 && Height < minHeight)
            {
                Height = minHeight;
                IsAutoHeight = true;
            }

            if (IsAutoHeight && maxHeight > 0 && Height > maxHeight)
                Height = maxHeight;
        }
    }
}