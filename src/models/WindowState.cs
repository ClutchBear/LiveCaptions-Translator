using System.ComponentModel;
using System.Runtime.CompilerServices;
using LiveCaptionsTranslator.Utils;

namespace LiveCaptionsTranslator.models
{
    public class MainWindowState : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool topmost = true;
        private bool captionLogEnabled = false;
        private bool latencyShow = false;
        private int originalFontSize = 15;
        private int translatedFontSize = 18;
        private string spokenLanguage = "英语(美国)";

        public bool Topmost
        {
            get => topmost;
            set
            {
                topmost = value;
                OnPropertyChanged("Topmost");
            }
        }
        public bool CaptionLogEnabled
        {
            get => captionLogEnabled;
            set
            {
                captionLogEnabled = value;
                OnPropertyChanged("CaptionLogEnabled");
            }
        }
        public bool LatencyShow
        {
            get => latencyShow;
            set
            {
                latencyShow = value;
                OnPropertyChanged("LatencyShow");
            }
        }
        public int OriginalFontSize
        {
            get => originalFontSize;
            set
            {
                originalFontSize = value;
                OnPropertyChanged("OriginalFontSize");
            }
        }
        public int TranslatedFontSize
        {
            get => translatedFontSize;
            set
            {
                translatedFontSize = value;
                OnPropertyChanged("TranslatedFontSize");
            }
        }

        // LiveCaptions 的语音识别语言（它听什么语言），用于设置页记住当前选择
        public string SpokenLanguage
        {
            get => spokenLanguage;
            set
            {
                spokenLanguage = value;
                OnPropertyChanged("SpokenLanguage");
            }
        }

        public void OnPropertyChanged([CallerMemberName] string propName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
            Translator.Setting?.Save();
        }
    }

    public class OverlayWindowState : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private int fontSize = 15;
        private Color fontColor = Color.White;
        private FontBold fontBold = FontBold.None;
        private double fontStroke = 0.0;

        private Color backgroundColor = Color.Black;
        private int opacity = 150;
        private int idleClearSeconds = 30;

        public int FontSize
        {
            get => fontSize;
            set
            {
                fontSize = value;
                OnPropertyChanged("FontSize");
            }
        }
        public Color FontColor
        {
            get => fontColor;
            set
            {
                fontColor = value;
                OnPropertyChanged("FontColor");
            }
        }
        public FontBold FontBold
        {
            get => fontBold;
            set
            {
                fontBold = value;
                OnPropertyChanged("FontBold");
            }
        }
        public double FontStroke
        {
            get => fontStroke;
            set
            {
                fontStroke = value;
                OnPropertyChanged("FontStroke");
            }
        }
        public Color BackgroundColor
        {
            get => backgroundColor;
            set
            {
                backgroundColor = value;
                OnPropertyChanged("BackgroundColor");
            }
        }
        public int Opacity
        {
            get => opacity;
            set
            {
                opacity = value;
                OnPropertyChanged("Opacity");
            }
        }

        // 多少秒没有新译文就把字幕内容清空（窗口和背景框保留在原位）；0 = 关闭此功能
        public int IdleClearSeconds
        {
            get => idleClearSeconds;
            set
            {
                idleClearSeconds = value;
                OnPropertyChanged("IdleClearSeconds");
            }
        }

        public void OnPropertyChanged([CallerMemberName] string propName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
            Translator.Setting?.Save();
        }
    }
}