using System.Reflection;
using System.Windows;

namespace AllaganNode
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            Title = "Allagan Node " + Assembly.GetExecutingAssembly().GetName().Version.ToString();
        }
    }
}
