// 复制自 WinUI Gallery Controls/HomePage/Tile.xaml.cs（MIT License），命名空间改为本项目。
// 新增行为（HyperlinkButton.Click 分发）：
//   InAppLink（学习资源/GitHub）→ 应用内 BrowserWindow(WebView2) 打开；
//   PageName（内部页面）        → 触发 TileClick 事件，由宿主页面完成导航。
// 注意：原版 NavigateUri="{x:Bind Link}" 在 Link 为 null 时抛 E_INVALIDARG（ConvertValue 不接受
// null），本项目瓦片不再使用 NavigateUri，外部链接一律走 InAppLink 应用内打开。

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
