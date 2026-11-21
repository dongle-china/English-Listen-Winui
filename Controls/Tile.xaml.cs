// 复制自 WinUI Gallery Controls/HomePage/Tile.xaml.cs（MIT License），命名空间改为本项目。
// 新增行为（HyperlinkButton.Click 分发）：
//   Link（外链，如 GitHub）→ NavigateUri 由平台处理，点击处理器不动作；
//   InAppLink（学习资源）  → 应用内 BrowserWindow(WebView2) 打开；
//   PageName（内部页面）   → 触发 TileClick 事件，由宿主页面完成导航。

using System;
using English_Listen_WinUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace English_Listen_WinUI.Controls;

public sealed partial class Tile : UserControl
{
    /// <summary>PageName 瓦片点击时触发，参数为页面名。</summary>
    public event EventHandler<string>? TileClick;

    public string Title
    {
        get { return (string)GetValue(TitleProperty); }
        set { SetValue(TitleProperty, value); }
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(Tile), new PropertyMetadata(null));

    public string Description
    {
        get { return (string)GetValue(DescriptionProperty); }
        set { SetValue(DescriptionProperty, value); }
    }

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(Tile), new PropertyMetadata(null));

    public object Source
    {
        get { return (object)GetValue(SourceProperty); }
        set { SetValue(SourceProperty, value); }
    }

    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(object), typeof(Tile), new PropertyMetadata(null));

    /// <summary>外部链接（系统浏览器打开，仅 GitHub 使用），由 HyperlinkButton.NavigateUri 处理。</summary>
    public string Link
    {
        get { return (string)GetValue(LinkProperty); }
        set { SetValue(LinkProperty, value); }
    }

    public static readonly DependencyProperty LinkProperty =
        DependencyProperty.Register(nameof(Link), typeof(string), typeof(Tile), new PropertyMetadata(null));

    /// <summary>点击后导航到的应用内页面名；由 TileClick 事件交给宿主页面处理。</summary>
    public string PageName
    {
        get { return (string)GetValue(PageNameProperty); }
        set { SetValue(PageNameProperty, value); }
    }

    public static readonly DependencyProperty PageNameProperty =
        DependencyProperty.Register(nameof(PageName), typeof(string), typeof(Tile), new PropertyMetadata(null));

    /// <summary>点击后在应用内 BrowserWindow(WebView2) 打开的 URL。</summary>
    public string InAppLink
    {
        get { return (string)GetValue(InAppLinkProperty); }
        set { SetValue(InAppLinkProperty, value); }
    }

    public static readonly DependencyProperty InAppLinkProperty =
        DependencyProperty.Register(nameof(InAppLink), typeof(string), typeof(Tile), new PropertyMetadata(null));

    public Tile()
    {
        this.InitializeComponent();
    }

    private void LinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (Link != null)
        {
            // 外链：NavigateUri 已由平台处理，避免重复动作
            return;
        }
        if (PageName is string page)
        {
            TileClick?.Invoke(this, page);
        }
        else if (InAppLink is string url)
        {
            new BrowserWindow(Title ?? url, url).Activate();
        }
    }
}
