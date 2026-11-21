using System;
using System.Linq;
using English_Listen_WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel;

namespace English_Listen_WinUI.Views
{
    /// <summary>主页功能卡片的数据项（WinUI Gallery 风格）。</summary>
    public sealed class HomeCardItem
    {
        public required Symbol Glyph { get; init; }
        public required string Title { get; init; }
        public required string Subtitle { get; init; }
        public required string PageName { get; init; }
    }

    public sealed partial class HomePage : Page
    {
        private readonly MainViewModel _viewModel;

        private static readonly HomeCardItem[] _cards =
        [
            new() { Glyph = Symbol.Video, Title = "素材库", Subtitle = "导入音视频，Whisper 本地转写自动切句", PageName = "MediaLibraryPage" },
            new() { Glyph = Symbol.Library, Title = "查看单词", Subtitle = "词库管理、翻译与听写测试入口", PageName = "WordsPage" },
            new() { Glyph = Symbol.Page2, Title = "背单词", Subtitle = "自动朗读背诵，记录每日学习进度", PageName = "MemorizePage" },
            new() { Glyph = Symbol.Calendar, Title = "学习历史", Subtitle = "听写与精听记录、成绩统计图表", PageName = "ProgressPage" },
            new() { Glyph = Symbol.Contact, Title = "个人中心", Subtitle = "账户管理、云同步与训练统计", PageName = "ProfilePage" },
            new() { Glyph = Symbol.Help, Title = "帮助", Subtitle = "功能教程、训练流程与快捷键说明", PageName = "HelpPage" },
            new() { Glyph = Symbol.ContactInfo, Title = "关于", Subtitle = "版本信息与开源许可", PageName = "AboutPage" },
        ];

        public HomePage()
        {
            this.InitializeComponent();
            _viewModel = App.SharedViewModel!;
            this.DataContext = _viewModel;
            CardsRepeater.ItemsSource = _cards;
            // XAML 里接自定义委托事件会让 XamlCompiler 崩溃，快速开始瓦片在构造函数里接线
            ImportTile.TileClick += QuickTile_Click;
            DictationTile.TileClick += QuickTile_Click;
            MemorizeTile.TileClick += QuickTile_Click;
            ProgressTile.TileClick += QuickTile_Click;
            HelpTile.TileClick += QuickTile_Click;
            // ThemeDictionaries 放 ImageSource 会让 XamlCompiler 崩溃，改由代码切换主题插画
            UpdateHeroImage();
            this.ActualThemeChanged += (s, _) => UpdateHeroImage();
            Loaded += HomePage_Loaded;
        }

        private void UpdateHeroImage()
        {
            var file = ActualTheme == ElementTheme.Dark ? "HomeHeroDark.png" : "HomeHero.png";
            HeroImage.Source = new BitmapImage(new Uri($"ms-appx:///Assets/{file}"));
        }

        private void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            // 版本号唯一来源：Package.appxmanifest
            var v = Package.Current.Id.Version;
            VersionTextBlock.Text = $"English Listen v{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";

            if (_viewModel?.TestHistory != null && _viewModel.TestHistory.Count > 0)
            {
                StatsPanel.Visibility = Visibility.Visible;
                TotalTestsInfoBar.Title = $"总测试次数: {_viewModel.TestHistory.Count}";
                var avg = _viewModel.TestHistory.Average(t => t.Accuracy);
                AvgAccuracyInfoBar.Title = $"平均正确率: {avg:F1}%";
            }
        }

        /// <summary>快速开始瓦片（PageName 模式）点击 → 应用内导航。</summary>
        private void QuickTile_Click(object? sender, string pageName)
        {
            NavigateToPage(pageName);
        }

        private void Card_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string pageName })
            {
                NavigateToPage(pageName);
            }
        }

        private static void NavigateToPage(string pageName)
        {
            Type? pageType = pageName switch
            {
                "MediaLibraryPage" => typeof(MediaLibraryPage),
                "WordsPage" => typeof(WordsPage),
                "MemorizePage" => typeof(MemorizePage),
                "ProgressPage" => typeof(ProgressPage),
                "ProfilePage" => typeof(ProfilePage),
                "HelpPage" => typeof(HelpPage),
                "AboutPage" => typeof(AboutPage),
                _ => null
            };
            if (pageType != null)
            {
                MainWindow.GoToPage(pageType);
            }
        }
    }
}
