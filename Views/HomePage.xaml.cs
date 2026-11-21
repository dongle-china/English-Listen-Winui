using System;
using System.Linq;
using English_Listen_WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel;
using Windows.System;

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

    /// <summary>主页外链瓦片的数据项（WinUI Gallery 头部链接复刻）。</summary>
    public sealed class LinkTileItem
    {
        public required ImageSource Icon { get; init; }
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required string Url { get; init; }
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

        // 外链瓦片：URL 与描述抄自 WinUI Gallery 头部
        private static readonly LinkTileItem[] _linkTiles =
        [
            new() { Icon = TileIcon("TileWinUI.svg"), Title = "Getting started", Description = "WinUI 入门与详细文档", Url = "https://aka.ms/winui-getstarted" },
            new() { Icon = TileIcon("TileDesign.svg"), Title = "Design", Description = "Fluent 设计指南与工具包", Url = "https://learn.microsoft.com/windows/apps/design/" },
            new() { Icon = TileIcon("TileGitHub.svg"), Title = "WinUI on GitHub", Description = "WinUI 源码与问题追踪", Url = "https://github.com/microsoft/microsoft-ui-xaml" },
            new() { Icon = TileIcon("TileToolkit.svg"), Title = "Community Toolkit", Description = "社区控件与辅助组件库示例", Url = "https://apps.microsoft.com/store/detail/windows-community-toolkit-sample-app/9NBLGGH4TLCQ" },
            new() { Icon = TileIcon("TileSamples.svg"), Title = "Code samples", Description = "按功能与 API 分类的官方示例", Url = "https://learn.microsoft.com/windows/apps/get-started/samples" },
            new() { Icon = TileIcon("TileStore.svg"), Title = "Partner Center", Description = "将应用发布到 Microsoft Store", Url = "https://developer.microsoft.com/windows/" },
        ];

        private static SvgImageSource TileIcon(string file) =>
            new(new Uri($"ms-appx:///Assets/Tiles/{file}"));

        public HomePage()
        {
            this.InitializeComponent();
            _viewModel = App.SharedViewModel!;
            this.DataContext = _viewModel;
            CardsRepeater.ItemsSource = _cards;
            LinksRepeater.ItemsSource = _linkTiles;
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

        private async void LinkTile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string url })
            {
                await Launcher.LaunchUriAsync(new Uri(url));
            }
        }

        private void Card_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string pageName })
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
}
