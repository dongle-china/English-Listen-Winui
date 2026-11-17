using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using English_Listen_WinUI.Services;
using English_Listen_WinUI.ViewModels;
using English_Listen_WinUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace English_Listen_WinUI
{
    public sealed partial class MainWindow : Window
    {
        private static DispatcherTimer? _notificationTimer;
        private static MainWindow? _currentInstance;
        private static bool _isToastHovered = false;
        private readonly MainViewModel? _viewModel;
        private bool _isNavigating = false;

        // shortcut: XamlCompiler 1.8 对 Window.Resources 里的 MenuFlyout 会崩溃，头像菜单只能代码构建
        private readonly MenuFlyout _loggedOutFlyout = new();
        private readonly MenuFlyout _loggedInFlyout = new();
        private readonly MenuFlyoutItem _userInfoItem = new() { Text = "用户", IsEnabled = false };

        private void BuildAvatarFlyouts()
        {
            var loginItem = new MenuFlyoutItem { Text = "登录", Icon = new FontIcon { Glyph = "\uE77B" } };
            loginItem.Click += LoginMenuItem_Click;
            _loggedOutFlyout.Items.Add(loginItem);

            _userInfoItem.Icon = new FontIcon { Glyph = "\uE77B" };
            _loggedInFlyout.Items.Add(_userInfoItem);
            _loggedInFlyout.Items.Add(new MenuFlyoutSeparator());
            var logoutItem = new MenuFlyoutItem { Text = "退出登录", Icon = new FontIcon { Glyph = "\uE7E8" } };
            logoutItem.Click += LogoutMenuItem_Click;
            _loggedInFlyout.Items.Add(logoutItem);
        }

        /// <summary>页面类型名 → 标题栏显示名。</summary>
        private static readonly Dictionary<string, string> PageTitles = new()
        {
            ["HomePage"] = "主页",
            ["MediaLibraryPage"] = "素材库",
            ["WordsPage"] = "训练 · 查看单词",
            ["MemorizePage"] = "训练 · 背单词",
            ["DictationTestPage"] = "听写测试",
            ["SentenceTrainingPage"] = "句子精听",
            ["ProgressPage"] = "学习统计",
            ["UserPage"] = "用户管理",
            ["SettingsPage"] = "设置",
            ["HelpPage"] = "帮助",
            ["AboutPage"] = "关于",
            ["CloudPage"] = "云端同步",
            ["RegisterPage"] = "注册",
            ["AnswersPage"] = "答案",
        };

        public MainWindow()
        {
            Debug.WriteLine("[MainWindow] Constructor started");

            try
            {
                this.InitializeComponent();
                Debug.WriteLine("[MainWindow] InitializeComponent completed");

                // Windows 11 原生外壳：Mica + 自定义标题栏（拖动区域 = AppTitleBar）
                ExtendsContentIntoTitleBar = true;
                AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;
                AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                SetTitleBar(AppTitleBar);

                // 窗口失焦时淡化标题文字，还原 Win11 原生质感（WinUI3 无 Deactivated 事件）
                Activated += (s, e) => SetTitleBarDim(e.WindowActivationState == WindowActivationState.Deactivated);

                // ponytail: set title bar icon via AppWindow
                SetWindowIcon();

                _viewModel = App.SharedViewModel ?? throw new InvalidOperationException("SharedViewModel is null");
                Debug.WriteLine("[MainWindow] ViewModel assigned");

                _currentInstance = this;

                _notificationTimer = new DispatcherTimer();
                _notificationTimer.Interval = TimeSpan.FromSeconds(5);
                _notificationTimer.Tick += (s, e) => HideNotification();

                BuildAvatarFlyouts();

                // Set up navigation
                MainNavigationView.SelectionChanged += NavigationView_SelectionChanged;
                MainNavigationView.ItemInvoked += NavigationView_ItemInvoked;
                MainNavigationView.BackRequested += NavigationView_BackRequested;
                ContentFrame.Navigated += ContentFrame_Navigated;
                Debug.WriteLine("[MainWindow] Navigation handlers attached");

                // 设置返回按钮状态
                UpdateBackButtonState();

                // 用户头像：登录/登出/会话恢复时刷新
                SetLoggedOutAvatar();
                AuthService.LoginStateChanged += AuthService_LoginStateChanged;
                _ = UpdateAvatarStateAsync();

                // Navigate to home page initially
                NavigateToPage(typeof(HomePage));
                Debug.WriteLine("[MainWindow] Initial navigation to HomePage completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] FATAL initialization failed: {ex.Message}");
                Debug.WriteLine($"[MainWindow] Stack trace: {ex.StackTrace}");

                // Write error to log file
                try
                {
                    var logPath = Path.Combine(AppContext.BaseDirectory, "window_error.log");
                    File.WriteAllText(logPath, $"[{DateTime.Now}] WINDOW ERROR:\n{ex}\n\nStack:\n{ex.StackTrace}");
                }
                catch
                {
                }
            }

            Debug.WriteLine("[MainWindow] Constructor completed");
        }

        // ── 标题栏 ─────────────────────────────────────────────

        private void SetTitleBarDim(bool dim)
        {
            var opacity = dim ? 0.5 : 1.0;
            AppTitleText.Opacity = opacity;
            PageStatusText.Opacity = opacity;
        }

        private void UpdatePageStatus(Type? pageType)
        {
            var key = pageType?.Name ?? "";
            PageStatusText.Text = PageTitles.TryGetValue(key, out var title) ? title : "";
        }

        /// <summary>训练页上报训练阶段状态（如"听写 · 第 3/20 个"），导航时自动复位为页面名。</summary>
        public static void SetTitleStatus(string? status)
        {
            _currentInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                if (_currentInstance != null)
                    _currentInstance.PageStatusText.Text = status ?? "";
            });
        }

        // ── 用户头像 / 登录 ────────────────────────────────────

        private void AuthService_LoginStateChanged()
        {
            DispatcherQueue.TryEnqueue(() => { _ = UpdateAvatarStateAsync(); });
        }

        private async Task UpdateAvatarStateAsync()
        {
            try
            {
                if (!SupabaseService.Instance.IsInitialized || !SupabaseService.Instance.IsLoggedIn)
                {
                    SetLoggedOutAvatar();
                    return;
                }

                var authService = new AuthService();
                var user = authService.CurrentUser;
                var profile = await authService.GetProfileAsync();

                var email = user?.Email ?? "";
                var displayName = string.IsNullOrWhiteSpace(profile?.Nickname) ? email : profile!.Nickname!;
                var avatarUrl = profile?.AvatarUrl;

                _userInfoItem.Text = displayName;

                if (!string.IsNullOrEmpty(avatarUrl))
                {
                    // 服务端头像
                    var avatarImage = new BitmapImage(new Uri(avatarUrl));
                    _userInfoItem.Icon = new ImageIcon { Source = avatarImage };
                    AvatarButton.Content = new Border
                    {
                        Width = 28,
                        Height = 28,
                        CornerRadius = new CornerRadius(14),
                        Child = new Image { Source = avatarImage, Stretch = Stretch.UniformToFill }
                    };
                }
                else
                {
                    // 无头像：首字母圆形头像
                    _userInfoItem.Icon = new FontIcon { Glyph = "\uE77B" };
                    var initial = displayName.Length > 0 ? char.ToUpperInvariant(displayName[0]).ToString() : "?";
                    var accent = (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
                    AvatarButton.Content = new Border
                    {
                        Width = 28,
                        Height = 28,
                        CornerRadius = new CornerRadius(14),
                        Background = new SolidColorBrush(accent),
                        Child = new TextBlock
                        {
                            Text = initial,
                            FontSize = 14,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };
                }

                AvatarButton.Flyout = _loggedInFlyout;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Avatar] UpdateAvatarState failed: {ex.Message}");
                SetLoggedOutAvatar();
            }
        }

        private void SetLoggedOutAvatar()
        {
            AvatarButton.Content = new FontIcon { Glyph = "\uE77B", FontSize = 18 };
            AvatarButton.Flyout = _loggedOutFlyout;
        }

        private async void LoginMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var emailBox = new TextBox { Header = "邮箱", PlaceholderText = "you@example.com" };
            var passwordBox = new PasswordBox { Header = "密码" };
            var registerLink = new HyperlinkButton
            {
                Content = "没有账号？注册新账号",
                Margin = new Thickness(0, 12, 0, 0)
            };

            var panel = new StackPanel { MinWidth = 300, Spacing = 4 };
            panel.Children.Add(emailBox);
            panel.Children.Add(passwordBox);
            panel.Children.Add(registerLink);

            var dialog = new ContentDialog
            {
                Title = "登录",
                Content = panel,
                PrimaryButtonText = "登录",
                CloseButtonText = "取消",
                XamlRoot = this.Content?.XamlRoot
            };

            registerLink.Click += (_, _) =>
            {
                dialog.Hide();
                GoToPage(typeof(RegisterPage));
            };

            // 失败时重开对话框重试；成功后走统一登录同步管线
            while (true)
            {
                var choice = await dialog.ShowAsync();
                if (choice != ContentDialogResult.Primary) return;

                var email = emailBox.Text?.Trim() ?? "";
                if (email.Length == 0 || passwordBox.Password.Length == 0)
                {
                    ShowNotification("请输入邮箱和密码");
                    continue;
                }

                var result = await new AuthService().SignInAsync(email, passwordBox.Password);
                if (!result.Success)
                {
                    ShowNotification($"登录失败: {result.ErrorMessage}", severity: InfoBarSeverity.Error);
                    continue;
                }

                await SyncAfterLoginAsync(email);
                return;
            }
        }

        private async void LogoutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await new AuthService().SignOutAsync();
            ShowNotification("已退出登录");
        }

        private async Task SyncAfterLoginAsync(string email)
        {
            if (_viewModel == null) return;

            ShowPersistentNotification("正在同步云端数据...", "登录");
            try
            {
                await LoginFlow.CompleteLoginAsync(_viewModel, email,
                    new Progress<string>(m => UpdateNotification(m)));
                HideNotification();
                ShowNotification("登录成功，数据已同步", severity: InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                HideNotification();
                ShowNotification($"登录成功，但云端同步失败: {ex.Message}", severity: InfoBarSeverity.Warning);
            }
        }

        // ── 导航 ───────────────────────────────────────────────

        private void NavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            // Handle item invoked to prevent double navigation
            if (args.IsSettingsInvoked)
            {
                NavigateToSettings();
            }
        }

        private void NavigationView_SelectionChanged(NavigationView sender,
            NavigationViewSelectionChangedEventArgs args)
        {
            if (_isNavigating) return;

            try
            {
                if (args.IsSettingsSelected)
                {
                    NavigateToSettings();
                    return;
                }

                if (args.SelectedItem is NavigationViewItem selectedItem)
                {
                    string pageName = selectedItem.Tag?.ToString() ?? "";
                    NavigateToPageByName(pageName);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Navigation failed: {ex.Message}");
                try
                {
                    ShowErrorDialog("导航失败", $"页面导航失败: {ex.Message}");
                }
                catch
                {
                    // Ignore dialog errors
                }
            }
        }

        private void NavigateToSettings()
        {
            try
            {
                NavigateToPage(typeof(SettingsPage));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Settings navigation failed: {ex.Message}");
                ShowErrorDialog("设置错误", $"无法打开设置页面: {ex.Message}");
            }
        }

        private void NavigateToPageByName(string pageName)
        {
            if (string.IsNullOrEmpty(pageName)) return;

            Type pageType = pageName switch
            {
                "HomePage" => typeof(HomePage),
                "MediaLibraryPage" => typeof(MediaLibraryPage),
                "WordsPage" => typeof(WordsPage),
                "MemorizePage" => typeof(MemorizePage),
                "UserPage" => typeof(UserPage),
                "ProgressPage" => typeof(ProgressPage),
                "HelpPage" => typeof(HelpPage),
                "AboutPage" => typeof(AboutPage),
                _ => typeof(HomePage)
            };

            NavigateToPage(pageType);
        }


        private async void ShowErrorDialog(string title, string message)
        {
            try
            {
                // Ensure we have a valid XamlRoot
                if (this.Content?.XamlRoot == null)
                {
                    Debug.WriteLine($"[Dialog] Cannot show dialog - XamlRoot is null");
                    return;
                }

                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = message,
                    CloseButtonText = "确定",
                    XamlRoot = this.Content.XamlRoot
                };

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error dialog failed: {ex.Message}");
            }
        }

        // About button click handler
        private async void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var scrollViewer = new ScrollViewer();
                var textBlock = new TextBlock
                {
                    Text =
                        $@"English Listen v{Package.Current.Id.Version.Major}.{Package.Current.Id.Version.Minor}.{Package.Current.Id.Version.Build}

一个帮助学习英语的听写练习工具

基于 WinUI3 框架开发，支持多种语音引擎。

功能特点：
- 支持自定义词库
- 可调节朗读时间间隔
- 支持深色/浅色主题切换
- 提供测试历史记录查看
- 界面简洁易用

版权 © 2026 dongle。本软件使用 MIT 许可证发布。",
                    TextWrapping = TextWrapping.Wrap
                };
                scrollViewer.Content = textBlock;
                scrollViewer.MaxHeight = 400;

                if (this.Content?.XamlRoot != null)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "关于 English Listen",
                        Content = scrollViewer,
                        CloseButtonText = "关闭",
                        XamlRoot = this.Content.XamlRoot
                    };

                    await dialog.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"About dialog failed: {ex.Message}");
            }
        }

        public void SetSidebarVisibility(bool isVisible)
        {
            try
            {
                MainNavigationView.IsPaneVisible = isVisible;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to set sidebar visibility: {ex.Message}");
            }
        }

        public void NavigateToHome()
        {
            NavigateToPage(typeof(HomePage));
        }

        // 返回按钮点击事件处理
        private void NavigationView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            if (ContentFrame != null && ContentFrame.CanGoBack)
            {
                ContentFrame.GoBack();
                UpdateBackButtonState();
            }
        }

        // 内容框架导航完成事件处理
        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            if (e.Content != null)
            {
                var currentPageType = e.Content.GetType();
                UpdateNavigationViewSelection(currentPageType);
                UpdatePageStatus(currentPageType);
                UpdateBackButtonState();

                // 根据页面类型自动隐藏/显示侧边栏
                // 听考单词页面隐藏侧边栏，其他页面显示
                if (currentPageType == typeof(DictationTestPage))
                {
                    MainNavigationView.IsPaneVisible = false;
                }
                else
                {
                    MainNavigationView.IsPaneVisible = true;
                }
            }
        }

        // 根据页面类型更新导航栏选中项（递归查找分组子项）
        private void UpdateNavigationViewSelection(Type pageType)
        {
            if (pageType == null) return;

            if (pageType == typeof(SettingsPage))
            {
                _isNavigating = true;
                MainNavigationView.SelectedItem = MainNavigationView.SettingsItem;
                _isNavigating = false;
                return;
            }

            var selectedItem = FindNavItem(MainNavigationView.MenuItems, pageType.Name)
                               ?? FindNavItem(MainNavigationView.FooterMenuItems, pageType.Name);

            if (selectedItem != null)
            {
                _isNavigating = true;
                MainNavigationView.SelectedItem = selectedItem;
                _isNavigating = false;
            }
        }

        private static NavigationViewItem? FindNavItem(IEnumerable<object> items, string tag)
        {
            foreach (var item in items)
            {
                if (item is not NavigationViewItem navItem) continue;
                if (navItem.Tag?.ToString() == tag) return navItem;
                if (navItem.MenuItems is { Count: > 0 })
                {
                    var child = FindNavItem(navItem.MenuItems, tag);
                    if (child != null) return child;
                }
            }
            return null;
        }

        // 更新返回按钮状态
        private void UpdateBackButtonState()
        {
            if (ContentFrame != null)
            {
                MainNavigationView.IsBackEnabled = ContentFrame.CanGoBack;
            }
        }

        // 重写导航方法以更新返回按钮状态
        private void NavigateToPage(Type pageType)
        {
            if (pageType == null) return;

            try
            {
                _isNavigating = true;
                Debug.WriteLine($"[Navigation] Navigating to {pageType.Name}");

                if (ContentFrame?.Content?.GetType() == pageType)
                {
                    // Already on this page, do nothing
                    return;
                }

                var navigationResult = ContentFrame?.Navigate(pageType) ?? false;

                if (!navigationResult)
                {
                    Debug.WriteLine($"Failed to navigate to {pageType.Name}");
                    ShowErrorDialog("导航失败", $"无法导航到页面: {pageType.Name}");
                }
                else
                {
                    Debug.WriteLine($"[Navigation] Successfully navigated to {pageType.Name}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Page navigation failed: {ex.Message}");
                try
                {
                    ShowErrorDialog("导航错误", $"页面导航出错: {ex.Message}");
                }
                catch
                {
                    // Ignore dialog errors
                }
            }
            finally
            {
                _isNavigating = false;
                // 更新返回按钮状态
                UpdateBackButtonState();
            }
        }

        // ── InfoBar 非模态通知 ─────────────────────────────────

        /// <summary>按消息内容自动识别严重级别（失败/错误→红，成功→绿）。</summary>
        private static InfoBarSeverity DetectSeverity(string message) =>
            message.Contains("失败") || message.Contains("错误")
                ? InfoBarSeverity.Error
                : message.Contains("成功")
                    ? InfoBarSeverity.Success
                    : InfoBarSeverity.Informational;

        public static void ShowNotification(string message, string? title = null, InfoBarSeverity? severity = null)
        {
            _currentInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                _currentInstance?.ShowNotificationInternal(message, title, severity);
            });
        }

        public static void ShowPersistentNotification(string message, string? title = null,
            InfoBarSeverity? severity = null)
        {
            _currentInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                _currentInstance?.ShowPersistentNotificationInternal(message, title, severity);
            });
        }

        public static void UpdateNotification(string message)
        {
            _currentInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                _currentInstance?.ToastInfoBar.Message = message;
            });
        }

        public static void GoToPage(Type pageType)
        {
            _currentInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                _currentInstance?.NavigateToPage(pageType);
            });
        }

        private void ShowNotificationInternal(string message, string? title, InfoBarSeverity? severity)
        {
            _notificationTimer?.Stop();

            ToastInfoBar.Title = title ?? "";
            ToastInfoBar.Message = message;
            ToastInfoBar.Severity = severity ?? DetectSeverity(message);
            ToastInfoBar.IsOpen = true;

            _isToastHovered = false;
            _notificationTimer!.Interval = TimeSpan.FromSeconds(5);
            _notificationTimer!.Start();
        }

        private void ShowPersistentNotificationInternal(string message, string? title, InfoBarSeverity? severity)
        {
            _notificationTimer?.Stop();

            ToastInfoBar.Title = title ?? "";
            ToastInfoBar.Message = message;
            ToastInfoBar.Severity = severity ?? DetectSeverity(message);
            ToastInfoBar.IsOpen = true;
            _isToastHovered = false;
        }

        public static void HideNotification()
        {
            _currentInstance?.DispatcherQueue.TryEnqueue(() => { _currentInstance?.HideNotificationInternal(); });
        }

        private void HideNotificationInternal()
        {
            _notificationTimer?.Stop();
            if (_isToastHovered) return;
            ToastInfoBar.IsOpen = false;
        }

        private void ToastInfoBar_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _isToastHovered = true;
            _notificationTimer?.Stop();
        }

        private void ToastInfoBar_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            _isToastHovered = false;
            _notificationTimer!.Interval = TimeSpan.FromSeconds(5);
            _notificationTimer!.Start();
        }

        private void SetWindowIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico");
                if (File.Exists(iconPath))
                    this.AppWindow.SetIcon(iconPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SetWindowIcon failed: {ex.Message}");
            }
        }
    }
}
