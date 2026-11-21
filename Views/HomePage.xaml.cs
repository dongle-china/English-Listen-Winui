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

    /// <summary>主页瓦片数据项（复刻 WinUI Gallery Tile）：内部页面导航、应用内 WebView 窗口或外部浏览器，三选一。</summary>
    public sealed class LinkTileItem
    {
        /// <summary>内部页瓦片使用的 Segoe 图标（外链瓦片忽略）。</summary>
        public Symbol Glyph { get; init; } = Symbol.Help;
        /// <summary>外链瓦片的 SVG 图标；内部页瓦片为 null（显示 Glyph）。</summary>
        public ImageSource? Icon { get; init; }
        public required string Title { get; init; }
        public required string Description { get; init; }
        /// <summary>点击后导航到的应用内页面名；与 Url 互斥。</summary>
        public string? PageName { get; init; }
        /// <summary>点击后打开的 URL；与 PageName 互斥。</summary>
        public string? Url { get; init; }
        /// <summary>true = 系统浏览器打开（仅 GitHub）；false = 应用内 BrowserWindow(WebView2) 打开。</summary>
        public bool OpenExternally { get; init; }

        // x:Bind 直接绑定计算属性，避免 x:Bind 函数语法（XamlCompiler 对其有静默崩溃记录）
        public Visibility ArrowVisibility => Url != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SymbolVisibility => Icon == null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IconVisibility => Icon != null ? Visibility.Visible : Visibility.Collapsed;
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

        // 快速开始瓦片：内部页导航 + 本软件 GitHub 外链（样式复刻 WinUI Gallery Tile）
        private static readonly LinkTileItem[] _linkTiles =
        [
            new() { Glyph = Symbol.Upload, Title = "导入素材", Description = "导入音视频，自动转写切句", PageName = "MediaLibraryPage" },
            new() { Glyph = Symbol.Edit, Title = "开始听写", Description = "纸笔 / 在线两种听写模式", PageName = "WordsPage" },
            new() { Glyph = Symbol.Library, Title = "背单词", Description = "自动朗读背诵，记录进度", PageName = "MemorizePage" },
            new() { Glyph = Symbol.Calendar, Title = "学习历史", Description = "成绩统计与错词回顾", PageName = "ProgressPage" },
            new() { Glyph = Symbol.Help, Title = "帮助文档", Description = "训练流程与功能教程", PageName = "HelpPage" },
            new() { Icon = TileIcon("TileGitHub.svg"), Title = "GitHub 仓库", Description = "源码、Issue 与版本发布", Url = "https://github.com/dongle-china/English-Listen-Winui", OpenExternally = true },
        ];

        // 学习资源瓦片：词典 / 听力 / 阅读 / 工具，点击在应用内 BrowserWindow(WebView2) 打开
        private static readonly LinkTileItem[] _resourceTiles =
        [
            // 词典
            new() { Glyph = Symbol.Read, Title = "朗文当代 (LDOCE)", Description = "英英词典，例句地道，区分书面与口语", Url = "https://www.ldoceonline.com/" },
            new() { Glyph = Symbol.Library, Title = "牛津学习者词典", Description = "释义清晰，配套语法工具与词表", Url = "https://www.oxfordlearnersdictionaries.com/" },
            new() { Glyph = Symbol.Document, Title = "柯林斯词典", Description = "语料库释义，大量真实语境例句", Url = "https://www.collinsdictionary.com/" },
            new() { Glyph = Symbol.Bookmarks, Title = "韦氏词典", Description = "美式权威，词源历史与用法提醒", Url = "https://www.merriam-webster.com/" },
            new() { Glyph = Symbol.Find, Title = "WordHippo", Description = "同义词 / 反义词检索，替换平淡用词", Url = "https://www.wordhippo.com/" },
            // 听力
            new() { Glyph = Symbol.Play, Title = "BBC Learning English", Description = "6 Minute English，语速适中话题贴近生活", Url = "https://www.bbc.co.uk/learningenglish" },
            new() { Glyph = Symbol.Volume, Title = "VOA Learning English", Description = "慢速新闻，音频与文本同步", Url = "https://learningenglish.voanews.com/" },
            new() { Glyph = Symbol.Video, Title = "TED 演讲", Description = "长听力，积累观点与学术表达", Url = "https://www.ted.com/talks" },
            // 阅读
            new() { Glyph = Symbol.World, Title = "News in Levels", Description = "同一新闻多难度等级，附音频", Url = "https://www.newsinlevels.com/" },
            // 工具
            new() { Glyph = Symbol.Find, Title = "Linggle", Description = "搭配检索，写译防中式英语", Url = "https://linggle.com/" },
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
            ResourcesRepeater.ItemsSource = _resourceTiles;
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

        private void Tile_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not LinkTileItem tile)
            {
                return;
            }
            if (tile.PageName is string page)
            {
                NavigateToPage(page);
            }
            else if (tile.Url is string url)
            {
                if (tile.OpenExternally)
                {
                    // 仅 GitHub 等走系统浏览器
                    _ = Launcher.LaunchUriAsync(new Uri(url));
                }
                else
                {
                    // 学习资源在应用内 WebView2 窗口渲染
                    new BrowserWindow(tile.Title, url).Activate();
                }
            }
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
