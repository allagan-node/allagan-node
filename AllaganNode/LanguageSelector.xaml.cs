using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AllaganNode.UI;

namespace AllaganNode
{
    public partial class LanguageSelector
    {
        private readonly DisplayLanguage[] _supportedLanguages;
        public DisplayLanguage SelectedLanguage;

        public LanguageSelector(DisplayLanguage[] supportedLanguages)
        {
            InitializeComponent();

            _supportedLanguages = supportedLanguages;

            var backgroundBrush = new SolidColorBrush(new Color
            {
                A = 255,
                R = 50,
                G = 50,
                B = 50
            });
            var foregroundBrush = new SolidColorBrush(new Color
            {
                A = 255,
                R = 200,
                G = 200,
                B = 200
            });

            MainStackPanel.Children.Add(new TextBlock
            {
                Background = backgroundBrush,
                Foreground = foregroundBrush,
                Margin = new Thickness(10),
                Padding = new Thickness(10),
                Text = Properties.Resources.LanguageSelector_LanguageSelector_SelectLanguage,
                VerticalAlignment = VerticalAlignment.Center
            });

            foreach (var supportedLanguage in supportedLanguages)
            {
                var button = new Button
                {
                    Background = backgroundBrush,
                    BorderBrush = foregroundBrush,
                    Content = supportedLanguage.DisplayName,
                    Foreground = foregroundBrush,
                    Margin = new Thickness(10),
                    Padding = new Thickness(10)
                };

                button.Click += Button_Click;

                MainStackPanel.Children.Add(button);
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var selectedLanguage = _supportedLanguages.FirstOrDefault(supportedLanguage =>
                supportedLanguage.DisplayName == ((Button) sender).Content.ToString());

            if (selectedLanguage != null)
            {
                SelectedLanguage = selectedLanguage;

                Close();
            }
        }
    }
}