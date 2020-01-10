using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using AllaganNode.Properties;
using AllaganNode.UI;

namespace AllaganNode
{
    // ReSharper disable once UnusedMember.Global
    public partial class MainWindow
    {
        private readonly string[] _requiredFiles =
        {
            "ffxiv_dx11.exe",
            "sqpack/ffxiv/000000.win32.dat0",
            "sqpack/ffxiv/000000.win32.index",
            "sqpack/ffxiv/000000.win32.index2",
            "sqpack/ffxiv/0a0000.win32.dat0",
            "sqpack/ffxiv/0a0000.win32.index",
            "sqpack/ffxiv/0a0000.win32.index2"
        };

        private readonly DisplayLanguage[] _supportedLanguages =
            {new DisplayLanguage("en-us", "English"), new DisplayLanguage("ko-kr", "한국어")};

        public MainWindow()
        {
            InitializeComponent();

            CheckAndUpdateLanguage();

            Title = Properties.Resources.ProgramTitle + " " + Assembly.GetExecutingAssembly().GetName().Version;

            CheckAndUpdateGlobalDir();
            CheckAndUpdateKoreanDir();
        }

        private void CheckAndUpdateLanguage()
        {
            var languageSelector = new LanguageSelector(_supportedLanguages);
            languageSelector.ShowDialog();

            var cultureInfo = new CultureInfo(languageSelector.SelectedLanguage.Code);
            Thread.CurrentThread.CurrentCulture = cultureInfo;
            Thread.CurrentThread.CurrentUICulture = cultureInfo;
        }

        private void CheckAndUpdateGlobalDir()
        {
            if (!string.IsNullOrEmpty(Settings.Default.GlobalDir))
                if (Directory.Exists(Settings.Default.GlobalDir))
                    if (CheckRequiredFiles(Settings.Default.GlobalDir))
                        return;

            MessageBox.Show(Properties.Resources.MainWindow_CheckAndUpdateGlobalDir_AutoDetectQuestion,
                Properties.Resources.ProgramTitle, MessageBoxButton.YesNo, MessageBoxImage.Question);
        }

        private void CheckAndUpdateKoreanDir()
        {
            if (!string.IsNullOrEmpty(Settings.Default.GlobalDir)) MessageBox.Show("TEST");
        }

        private bool CheckRequiredFiles(string targetDir)
        {
            return _requiredFiles.All(requiredFile => File.Exists(Path.Combine(targetDir, requiredFile)));
        }
    }
}