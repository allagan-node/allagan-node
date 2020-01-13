using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using AllaganNode.Properties;
using AllaganNode.UI;
using Microsoft.Win32;

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
            if (!string.IsNullOrEmpty(Settings.Default.GlobalDir) && Directory.Exists(Settings.Default.GlobalDir) &&
                CheckRequiredFiles(Settings.Default.GlobalDir))
                return;

            if (MessageBox.Show(Properties.Resources.MainWindow_CheckAndUpdateGlobalDir_AutoDetectGlobalClientQuestion,
                    Properties.Resources.ProgramTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) !=
                MessageBoxResult.Yes || !DetectGlobalDir() || MessageBox.Show(
                    string.Format(Properties.Resources.MainWindow_CheckAndUpdateGlobalDir_AutoDetectGlobalClientVerify,
                        Settings.Default.GlobalDir), Properties.Resources.ProgramTitle, MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                Settings.Default.GlobalDir = string.Empty;
                var openFileDialog = new OpenFileDialog
                {
                    CheckFileExists = true,
                    DefaultExt = "exe",
                    Filter = "ffxiv_dx11.exe|ffxiv_dx11.exe",
                    Multiselect = false,
                    Title = Properties.Resources.ProgramTitle
                };

                while (!Directory.Exists(Settings.Default.GlobalDir) || !CheckRequiredFiles(Settings.Default.GlobalDir))
                {
                    MessageBox.Show(
                        Properties.Resources.MainWindow_CheckAndUpdateGlobalDir_GlobalClientManualSelectInstruction,
                        Properties.Resources.ProgramTitle, MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    if (openFileDialog.ShowDialog() == true)
                        Settings.Default.GlobalDir = Path.GetDirectoryName(openFileDialog.FileName);
                }
            }

            Settings.Default.Save();
        }

        private bool DetectGlobalDir()
        {
            var uninstallKeyName = Environment.Is64BitOperatingSystem
                ? @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                : @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

            using (var uninstallKey = Registry.LocalMachine.OpenSubKey(uninstallKeyName))
            {
                if (uninstallKey == null) return false;

                foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                {
                    using var subKey = uninstallKey.OpenSubKey(subKeyName);

                    var displayName = subKey?.GetValue("DisplayName");
                    if (displayName == null || displayName.ToString() != "FINAL FANTASY XIV ONLINE") continue;

                    var iconPath = subKey.GetValue("DisplayIcon");
                    if (iconPath == null) continue;

                    var globalDir =
                        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(iconPath.ToString()),
                            "../game"));
                    if (!Directory.Exists(globalDir)) continue;
                    if (!CheckRequiredFiles(globalDir)) continue;

                    Settings.Default.GlobalDir = globalDir;
                    return true;
                }
            }

            return false;
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