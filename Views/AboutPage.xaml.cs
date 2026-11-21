using English_Listen_WinUI.Helpers;
using Microsoft.UI.Xaml.Controls;

namespace English_Listen_WinUI.Views
{
    public sealed partial class AboutPage : Page
    {
        public AboutPage()
        {
            this.InitializeComponent();
            Loaded += (_, _) =>
            {
                VersionTextBlock.Text = $"English Listen v{AppVersion.Current}";
            };
        }
    }
}
