using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;

namespace English_Listen_WinUI.Views
{
    public sealed partial class AboutPage : Page
    {
        public AboutPage()
        {
            this.InitializeComponent();
            Loaded += (_, _) =>
            {
                // 版本号唯一来源：Package.appxmanifest
                var v = Package.Current.Id.Version;
                VersionTextBlock.Text = $"English Listen v{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
            };
        }
    }
}
