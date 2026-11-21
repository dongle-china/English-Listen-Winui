using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using English_Listen_WinUI.Helpers;
using English_Listen_WinUI.Models;
using English_Listen_WinUI.Services;
using English_Listen_WinUI.ViewModels;
using English_Listen_WinUI.Views;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel;
using Windows.System;
using Windows.UI;

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

        private static readonly Dictionary<string, string> PageTitles = new()
        {
            ["HomePage"] = "主页",
            ["MediaLibraryPage"] = "素材库",
            ["WordsPage"] = "训练 · 查看单词",
            ["MemorizePage"] = "训练 · 背单词",
            ["DictationTestPage"] = "听写测试",
            ["SentenceTrainingPage"] = "句子训练",
            ["ProgressPage"] = "学习历史",
            ["ProfilePage"] = "个人中心",
            ["SettingsPage"] = "设置",
            ["HelpPage"] = "帮助",
            ["AboutPage"] = "关于",
            ["CloudPage"] = "云端同步",
            ["AnswersPage"] = "答案"
        };

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

        public MainWindow()
        {
            Debug.WriteLine("[MainWindow] Constructor started");
            try
            {
                InitializeComponent();
                Debug.WriteLine("[MainWindow] InitializeComponent completed");
                ExtendsContentIntoTitleBar = true;
                AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
                AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
                AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                SetTitleBar(AppTitleBar);
                Activated += (s, e) => SetTitleBarDim(e.WindowActivationState == WindowActivationState.Deactivated);
                SetWindowIcon();
                _viewModel = App.SharedViewModel ?? throw new InvalidOperationException("SharedViewModel is null");
                Debug.WriteLine("[MainWindow] ViewModel assigned");
                _currentInstance = this;
                _notificationTimer = new DispatcherTimer();
                _notificationTimer.Interval = TimeSpan.FromSeconds(5);
                _notificationTimer.Tick += (s, e) => HideNotification();
                BuildAvatarFlyouts();
                MainNavigationView.SelectionChanged += NavigationView_SelectionChanged;
                MainNavigationView.ItemInvoked += NavigationView_ItemInvoked;
                MainNavigationView.BackRequested += NavigationView_BackRequested;
                ContentFrame.Navigated += ContentFrame_Navigated;
                Debug.WriteLine("[MainWindow] Navigation handlers attached");
                UpdateBackButtonState();
                SetLoggedOutAvatar();
                WebApiService.LoginStateChanged += AuthService_LoginStateChanged;
                _ = UpdateAvatarStateAsync();
                NavigateToPage(typeof(HomePage));
                Debug.WriteLine("[MainWindow] Initial navigation to HomePage completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[MainWindow] FATAL initialization failed: " + ex.Message);
                Debug.WriteLine("[MainWindow] Stack trace: " + ex.StackTrace);
                try
                {
                    string path = Path.Combine(AppContext.BaseDirectory, "window_error.log");
                    File.WriteAllText(path, $"[{DateTime.Now}] WINDOW ERROR:\n{ex}\n\nStack:\n{ex.StackTrace}");
                }
                catch
                {
                }
            }
            Debug.WriteLine("[MainWindow] Constructor completed");
        }

        private void SetTitleBarDim(bool dim)
        {
            double opacity = dim ? 0.5 : 1.0;
            AppTitleText.Opacity = opacity;
            PageStatusText.Opacity = opacity;
        }

        private void UpdatePageStatus(Type? pageType)
        {
            string key = pageType?.Name ?? "";
            PageStatusText.Text = PageTitles.TryGetValue(key, out string? value) ? value : "";
        }

        public static void SetTitleStatus(string? status)
        {
            _currentInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                if (_currentInstance != null)
                {
                    _currentInstance.PageStatusText.Text = status ?? "";
                }
            });
        }

        private void AuthService_LoginStateChanged()
        {
            DispatcherQueue.TryEnqueue(() => { _ = UpdateAvatarStateAsync(); });
        }

        private async Task UpdateAvatarStateAsync()
        {
            try
            {
                WebApiService web = WebApiService.Instance;
                if (!web.IsLoggedIn)
                {
                    await UpdateLocalAccountAvatarAsync();
                    return;
                }
                string displayName = string.IsNullOrWhiteSpace(web.Nickname) ? (web.Email ?? "在线账户") : web.Nickname;
                string? avatarUrl = web.AvatarUrl;
                _userInfoItem.Text = displayName;
                if (!string.IsNullOrEmpty(avatarUrl))
                {
                    BitmapImage avatarImage = new BitmapImage(new Uri(avatarUrl));
                    _userInfoItem.Icon = new ImageIcon { Source = avatarImage };
                    AvatarButton.Content = new Border
                    {
                        Width = 28.0,
                        Height = 28.0,
                        CornerRadius = new CornerRadius(14.0),
                        Child = new Image { Source = avatarImage, Stretch = Stretch.UniformToFill }
                    };
                }
                else
                {
                    _userInfoItem.Icon = new FontIcon { Glyph = "\uE77B" };
                    string initial = displayName.Length > 0 ? char.ToUpperInvariant(displayName[0]).ToString() : "?";
                    Color accent = (Color)Application.Current.Resources["SystemAccentColor"];
                    AvatarButton.Content = new Border
                    {
                        Width = 28.0,
                        Height = 28.0,
                        CornerRadius = new CornerRadius(14.0),
                        Background = new SolidColorBrush(accent),
                        Child = new TextBlock
                        {
                            Text = initial,
                            FontSize = 14.0,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = new SolidColorBrush(Colors.White),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };
                }
                AvatarButton.Flyout = _loggedInFlyout;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Avatar] UpdateAvatarState failed: " + ex.Message);
                SetLoggedOutAvatar();
            }
        }

        private void SetLoggedOutAvatar()
        {
            AvatarButton.Content = new FontIcon { Glyph = "\uE77B", FontSize = 18.0 };
            AvatarButton.Flyout = _loggedOutFlyout;
        }

        private Task UpdateLocalAccountAvatarAsync()
        {
            string? currentUser = _viewModel?.Settings?.Settings?.CurrentUser;
            UserData? userData = string.IsNullOrEmpty(currentUser) || currentUser == "未登录"
                ? null
                : _viewModel?.Users?.FirstOrDefault(u => u.Username == currentUser);
            if (userData == null)
            {
                SetLoggedOutAvatar();
                return Task.CompletedTask;
            }
            string displayName = string.IsNullOrWhiteSpace(userData.Nickname) ? userData.Username : userData.Nickname;
            _userInfoItem.Text = displayName;
            _userInfoItem.Icon = new FontIcon { Glyph = "\uE77B" };
            string initial = displayName.Length > 0 ? char.ToUpperInvariant(displayName[0]).ToString() : "?";
            Color accent = (Color)Application.Current.Resources["SystemAccentColor"];
            AvatarButton.Content = new Border
            {
                Width = 28.0,
                Height = 28.0,
                CornerRadius = new CornerRadius(14.0),
                Background = new SolidColorBrush(accent),
                Child = new TextBlock
                {
                    Text = initial,
                    FontSize = 14.0,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            AvatarButton.Flyout = _loggedInFlyout;
            return Task.CompletedTask;
        }

        private void LoginMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowLoginDialogAsync();
        }

        public void ShowLoginDialogFromProfile()
        {
            _ = ShowLoginDialogAsync();
        }

        private async Task ShowLoginDialogAsync()
        {
            HyperlinkButton openDevicePageLink = new HyperlinkButton
            {
                Content = "1. 在网页端登录后打开 jetcpp.ccwu.cc/device，生成登录码"
            };
            TextBox codeBox = new TextBox
            {
                Header = "2. 输入 8 位登录码",
                PlaceholderText = "5 分钟内有效，例如 A2B4C6D8",
                MaxLength = 8
            };
            StackPanel onlinePanel = new StackPanel { MinWidth = 300.0, Spacing = 4.0 };
            onlinePanel.Children.Add(openDevicePageLink);
            onlinePanel.Children.Add(codeBox);

            ComboBox localUserCombo = new ComboBox
            {
                Header = "选择本地账户",
                PlaceholderText = _viewModel != null && _viewModel.Users.Count > 0 ? "请选择账户" : "暂无本地账户，请先新建",
                DisplayMemberPath = "Nickname",
                ItemsSource = _viewModel?.Users,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            PasswordBox localPasswordBox = new PasswordBox { Header = "密码", Margin = new Thickness(0.0, 8.0, 0.0, 0.0) };
            HyperlinkButton createUserLink = new HyperlinkButton
            {
                Content = "新建本地账户",
                Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
            };
            StackPanel localPanel = new StackPanel { MinWidth = 300.0, Spacing = 4.0 };
            localPanel.Children.Add(localUserCombo);
            localPanel.Children.Add(localPasswordBox);
            localPanel.Children.Add(createUserLink);

            Pivot pivot = new Pivot { MinWidth = 320.0 };
            pivot.Items.Add(new PivotItem { Header = "设备登录", Content = onlinePanel });
            pivot.Items.Add(new PivotItem { Header = "本地账户", Content = localPanel });

            ContentDialog dialog = new ContentDialog
            {
                Title = "登录",
                Content = pivot,
                PrimaryButtonText = "登录",
                CloseButtonText = "取消",
                XamlRoot = Content?.XamlRoot
            };

            openDevicePageLink.Click += async (_, _) =>
            {
                await Launcher.LaunchUriAsync(new Uri("https://jetcpp.ccwu.cc/device"));
            };
            createUserLink.Click += async (_, _) =>
            {
                dialog.Hide();
                await ShowCreateLocalUserDialogAsync();
            };

            while (true)
            {
                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    return;
                }
                if (pivot.SelectedIndex == 1)
                {
                    if (localUserCombo.SelectedItem is not UserData user)
                    {
                        ShowNotification("请选择本地账户");
                        continue;
                    }
                    if (localPasswordBox.Password.Length == 0)
                    {
                        ShowNotification("请输入密码");
                        continue;
                    }
                    if (!PasswordService.VerifyPassword(localPasswordBox.Password, user.PasswordHash))
                    {
                        ShowNotification("密码错误", null, InfoBarSeverity.Error);
                        continue;
                    }
                    await LoginAsLocalUserAsync(user);
                    return;
                }
                string code = codeBox.Text?.Trim() ?? "";
                if (code.Length == 0)
                {
                    ShowNotification("请输入登录码");
                    continue;
                }
                WebAuthResult result = await WebApiService.Instance.ExchangeCodeAsync(code);
                if (result.Success)
                {
                    break;
                }
                ShowNotification("登录失败: " + result.ErrorMessage, null, InfoBarSeverity.Error);
            }
            await SyncAfterLoginAsync(WebApiService.Instance.Email ?? "");
        }

        private async Task ShowCreateLocalUserDialogAsync()
        {
            if (_viewModel?.Settings == null)
            {
                return;
            }
            TextBox usernameBox = new TextBox { Header = "用户名（登录用，唯一）", PlaceholderText = "例如：dongle" };
            TextBox nicknameBox = new TextBox { Header = "昵称（显示用，可留空）", PlaceholderText = "例如：小东" };
            PasswordBox passwordBox = new PasswordBox { Header = "密码（至少 4 位）" };
            PasswordBox confirmBox = new PasswordBox { Header = "确认密码" };
            StackPanel panel = new StackPanel { MinWidth = 300.0, Spacing = 4.0 };
            panel.Children.Add(usernameBox);
            panel.Children.Add(nicknameBox);
            panel.Children.Add(passwordBox);
            panel.Children.Add(confirmBox);

            ContentDialog dialog = new ContentDialog
            {
                Title = "新建本地账户",
                Content = panel,
                PrimaryButtonText = "创建并登录",
                CloseButtonText = "取消",
                XamlRoot = Content?.XamlRoot
            };

            string username;
            while (true)
            {
                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    return;
                }
                username = usernameBox.Text?.Trim() ?? "";
                string nickname = nicknameBox.Text?.Trim() ?? "";
                string password = passwordBox.Password;
                if (username.Length == 0)
                {
                    ShowNotification("请输入用户名");
                    continue;
                }
                if (password.Length < 4)
                {
                    ShowNotification("密码至少 4 位");
                    continue;
                }
                if (password != confirmBox.Password)
                {
                    ShowNotification("两次输入的密码不一致");
                    continue;
                }
                if (await _viewModel.Settings.CreateUserAsync(username, nickname.Length > 0 ? nickname : username, password))
                {
                    break;
                }
                ShowNotification("创建失败：用户名已存在", null, InfoBarSeverity.Error);
            }
            List<UserData> users = await _viewModel.Settings.LoadUsersAsync();
            await _viewModel.LoadUsersFromListAsync(users);
            UserData? newUser = users.FirstOrDefault(u => u.Username == username);
            if (newUser != null)
            {
                await LoginAsLocalUserAsync(newUser);
            }
        }

        private async Task LoginAsLocalUserAsync(UserData user)
        {
            if (_viewModel?.Settings == null)
            {
                return;
            }
            user.LastLoginTime = DateTime.Now;
            await _viewModel.Settings.SaveUsersAsync(_viewModel.Users.ToList());
            _viewModel.Settings.Settings.CurrentUser = user.Username;
            await _viewModel.Settings.SaveSettingsAsync();
            await _viewModel.LoadTestHistoryAsync();
            WebApiService.RaiseLoginStateChanged();
            ShowNotification("已登录本地账户：" + user.Nickname, null, InfoBarSeverity.Success);
        }

        private async void LogoutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            bool wasOnline = WebApiService.Instance.IsLoggedIn;
            if (wasOnline)
            {
                await WebApiService.Instance.SignOutAsync();
            }
            if (_viewModel?.Settings != null)
            {
                _viewModel.Settings.Settings.CurrentUser = null;
                await _viewModel.Settings.SaveSettingsAsync();
                await _viewModel.LoadTestHistoryAsync();
            }
            WebApiService.RaiseLoginStateChanged();
            ShowNotification(wasOnline ? "已退出登录" : "已退出本地账户");
        }

        private async Task SyncAfterLoginAsync(string email)
        {
            if (_viewModel == null)
            {
                return;
            }
            ShowPersistentNotification("正在同步云端数据...", "登录");
            try
            {
                await LoginFlow.CompleteLoginAsync(_viewModel, email, new Progress<string>(m => UpdateNotification(m)));
                HideNotification();
                ShowNotification("登录成功，数据已同步", null, InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                HideNotification();
                ShowNotification("登录成功，但云端同步失败: " + ex.Message, null, InfoBarSeverity.Warning);
            }
        }

        private void NavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                NavigateToSettings();
            }
        }

        private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (_isNavigating)
            {
                return;
            }
            try
            {
                if (args.IsSettingsSelected)
                {
                    NavigateToSettings();
                }
                else if (args.SelectedItem is NavigationViewItem { Tag: var tag })
                {
                    NavigateToPageByName(tag?.ToString() ?? "");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Navigation failed: " + ex.Message);
                try
                {
                    ShowErrorDialog("导航失败", "页面导航失败: " + ex.Message);
                }
                catch
                {
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
                Debug.WriteLine("Settings navigation failed: " + ex.Message);
                ShowErrorDialog("设置错误", "无法打开设置页面: " + ex.Message);
            }
        }

        private void NavigateToPageByName(string pageName)
        {
            if (string.IsNullOrEmpty(pageName))
            {
                return;
            }
            Type pageType = pageName switch
            {
                "HomePage" => typeof(HomePage),
                "MediaLibraryPage" => typeof(MediaLibraryPage),
                "WordsPage" => typeof(WordsPage),
                "MemorizePage" => typeof(MemorizePage),
                "ProfilePage" => typeof(ProfilePage),
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
                if (Content?.XamlRoot == null)
                {
                    Debug.WriteLine("[Dialog] Cannot show dialog - XamlRoot is null");
                    return;
                }
                ContentDialog dialog = new ContentDialog
                {
                    Title = title,
                    Content = message,
                    CloseButtonText = "确定",
                    XamlRoot = Content.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error dialog failed: " + ex.Message);
            }
        }

        private async void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ScrollViewer scrollViewer = new ScrollViewer();
                TextBlock textBlock = new TextBlock
                {
                    Text = $"English Listen v{AppVersion.Current}\r\n\r\n一个帮助学习英语的听写练习工具\r\n\r\n基于 WinUI3 框架开发，支持多种语音引擎。\r\n\r\n功能特点：\r\n- 支持自定义词库\r\n- 可调节朗读时间间隔\r\n- 支持深色/浅色主题切换\r\n- 提供测试历史记录查看\r\n- 界面简洁易用\r\n\r\n版权 © 2026 dongle。本软件使用 MIT 许可证发布。",
                    TextWrapping = TextWrapping.Wrap
                };
                scrollViewer.Content = textBlock;
                scrollViewer.MaxHeight = 400.0;
                if (Content?.XamlRoot != null)
                {
                    ContentDialog dialog = new ContentDialog
                    {
                        Title = "关于 English Listen",
                        Content = scrollViewer,
                        CloseButtonText = "关闭",
                        XamlRoot = Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("About dialog failed: " + ex.Message);
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
                Debug.WriteLine("Failed to set sidebar visibility: " + ex.Message);
            }
        }

        public void NavigateToHome()
        {
            NavigateToPage(typeof(HomePage));
        }

        private void NavigationView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            if (ContentFrame != null && ContentFrame.CanGoBack)
            {
                ContentFrame.GoBack();
                UpdateBackButtonState();
            }
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            if (e.Content == null)
            {
                return;
            }
            Type type = e.Content.GetType();
            UpdateNavigationViewSelection(type);
            UpdatePageStatus(type);
            UpdateBackButtonState();
            // 听写测试全屏模式：隐藏侧栏
            MainNavigationView.IsPaneVisible = type != typeof(DictationTestPage);
        }

        private void UpdateNavigationViewSelection(Type pageType)
        {
            if (pageType == null)
            {
                return;
            }
            if (pageType == typeof(SettingsPage))
            {
                _isNavigating = true;
                MainNavigationView.SelectedItem = MainNavigationView.SettingsItem;
                _isNavigating = false;
                return;
            }
            NavigationViewItem? item = FindNavItem(MainNavigationView.MenuItems, pageType.Name)
                ?? FindNavItem(MainNavigationView.FooterMenuItems, pageType.Name);
            if (item != null)
            {
                _isNavigating = true;
                MainNavigationView.SelectedItem = item;
                _isNavigating = false;
            }
        }

        private static NavigationViewItem? FindNavItem(IEnumerable<object> items, string tag)
        {
            foreach (object item in items)
            {
                if (item is not NavigationViewItem { Tag: var itemTag } navigationViewItem)
                {
                    continue;
                }
                if (itemTag?.ToString() == tag)
                {
                    return navigationViewItem;
                }
                IList<object> menuItems = navigationViewItem.MenuItems;
                if (menuItems != null && menuItems.Count > 0)
                {
                    NavigationViewItem? found = FindNavItem(navigationViewItem.MenuItems, tag);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }
            return null;
        }

        private void UpdateBackButtonState()
        {
            if (ContentFrame != null)
            {
                MainNavigationView.IsBackEnabled = ContentFrame.CanGoBack;
            }
        }

        private void NavigateToPage(Type pageType)
        {
            if (pageType == null)
            {
                return;
            }
            try
            {
                _isNavigating = true;
                Debug.WriteLine("[Navigation] Navigating to " + pageType.Name);
                if (ContentFrame?.Content?.GetType() != pageType)
                {
                    if (ContentFrame == null || !ContentFrame.Navigate(pageType))
                    {
                        Debug.WriteLine("Failed to navigate to " + pageType.Name);
                        ShowErrorDialog("导航失败", "无法导航到页面: " + pageType.Name);
                    }
                    else
                    {
                        Debug.WriteLine("[Navigation] Successfully navigated to " + pageType.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Page navigation failed: " + ex.Message);
                try
                {
                    ShowErrorDialog("导航错误", "页面导航出错: " + ex.Message);
                }
                catch
                {
                }
            }
            finally
            {
                _isNavigating = false;
                UpdateBackButtonState();
            }
        }

        private static InfoBarSeverity DetectSeverity(string message)
        {
            return message.Contains("失败") || message.Contains("错误")
                ? InfoBarSeverity.Error
                : message.Contains("成功") ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
        }

        public static void ShowNotification(string message, string? title = null, InfoBarSeverity? severity = null)
        {
            _currentInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                _currentInstance?.ShowNotificationInternal(message, title, severity);
            });
        }

        public static void ShowPersistentNotification(string message, string? title = null, InfoBarSeverity? severity = null)
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
                if (_currentInstance != null)
                {
                    _currentInstance.ToastInfoBar.Message = message;
                }
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
            _notificationTimer.Start();
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
            _currentInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                _currentInstance?.HideNotificationInternal();
            });
        }

        private void HideNotificationInternal()
        {
            _notificationTimer?.Stop();
            if (!_isToastHovered)
            {
                ToastInfoBar.IsOpen = false;
            }
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
            _notificationTimer.Start();
        }

        private void SetWindowIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico");
                if (File.Exists(iconPath))
                {
                    AppWindow.SetIcon(iconPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SetWindowIcon failed: " + ex.Message);
            }
        }
    }
}
