using System;
using English_Listen_WinUI.Helpers;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace English_Listen_WinUI.Views
{
    /// <summary>应用内 WebView2 浏览窗口：学习资源在本窗口内渲染，GitHub 等仍走系统浏览器。</summary>
    public sealed partial class BrowserWindow : Window
    {
        public BrowserWindow(string title, string url)
        {
            InitializeComponent();
            Title = title;
            Browser.Source = new Uri(url);

            // AppWindow.Resize 按物理像素计，需按窗口 DPI 缩放，避免高 DPI 下窗口过小
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
            AppWindow.Resize(new SizeInt32
            {
                Width = (int)(1280 * scale),
                Height = (int)(832 * scale),
            });
        }
    }
}
