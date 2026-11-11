using System.IO;
using System.Text;

namespace LiveCaptionsTranslator.Utils
{
    /// <summary>
    /// 悬浮字幕窗的临时诊断日志。排查完删掉即可。
    /// 写到 exe 同目录的 overlay-debug.log，单文件不超过 ~1MB（超过就清空重来）。
    /// </summary>
    public static class OverlayDiag
    {
        private static readonly object gate = new();

        // 单文件发布时 AppContext.BaseDirectory 指向解压出来的临时目录，
        // 用 ProcessPath 才能定位到真正的 exe 所在目录。
        private static readonly string logPath = Path.Combine(
            Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory,
            "overlay-debug.log");

        public static void Log(string message)
        {
            try
            {
                lock (gate)
                {
                    var fi = new FileInfo(logPath);
                    if (fi.Exists && fi.Length > 1024 * 1024)
                        File.Delete(logPath);

                    File.AppendAllText(logPath,
                        $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}",
                        Encoding.UTF8);
                }
            }
            catch
            {
                // 诊断日志绝不能影响主流程
            }
        }

        public static void LogWithStack(string message)
        {
            Log(message + Environment.NewLine + "    " +
                Environment.StackTrace.Replace(Environment.NewLine, Environment.NewLine + "    "));
        }
    }
}
