using System.Windows;

using LiveCaptionsTranslator.utils;
using LiveCaptionsTranslator.Utils;

namespace LiveCaptionsTranslator
{
    public partial class App : Application
    {
        App()
        {
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                OverlayDiag.Log("[fatal] UnhandledException: " + e.ExceptionObject);
            DispatcherUnhandledException += (s, e) =>
                OverlayDiag.Log("[fatal] DispatcherUnhandledException: " + e.Exception);
            TaskScheduler.UnobservedTaskException += (s, e) =>
                OverlayDiag.Log("[fatal] UnobservedTaskException: " + e.Exception);

            Translator.Setting?.Save();

            Task.Run(() => Translator.SyncLoop());
            Task.Run(() => Translator.TranslateLoop());
            Task.Run(() => Translator.DisplayLoop());
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            OverlayDiag.Log("[exit] ProcessExit 触发");
            if (Translator.Window != null)
            {
                LiveCaptionsHandler.RestoreLiveCaptions(Translator.Window);
                LiveCaptionsHandler.KillLiveCaptions(Translator.Window);
            }
        }
    }
}
