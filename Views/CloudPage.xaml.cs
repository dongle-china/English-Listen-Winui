using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using English_Listen_WinUI.Services;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace English_Listen_WinUI.Views
{
    public sealed partial class CloudPage : Page
    {
        private readonly AuthService _authService = new();

        public CloudPage()
        {
            this.InitializeComponent();
            Loaded += CloudPage_Loaded;
        }

        private async void CloudPage_Loaded(object sender, RoutedEventArgs e)
        {
            await UpdateStatusAsync();
        }

        private async Task UpdateStatusAsync()
        {
            if (SupabaseService.Instance.IsLoggedIn)
            {
                var user = SupabaseService.Instance.CurrentUser;
                StatusText.Text = "欢迎回来";
                LoginPanel.Visibility = Visibility.Collapsed;
                LoggedInPanel.Visibility = Visibility.Visible;
                RegisterLink.Visibility = Visibility.Collapsed;
                CloudSyncPanel.Visibility = Visibility.Visible;

                UserEmailText.Text = user?.Email ?? "";

                var profile = await _authService.GetProfileAsync();
                UserRoleText.Text = profile?.Role ?? "user";
            }
            else
            {
                StatusText.Text = "登录";
                LoginPanel.Visibility = Visibility.Visible;
                LoggedInPanel.Visibility = Visibility.Collapsed;
                RegisterLink.Visibility = Visibility.Visible;
                CloudSyncPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async void LoginBtn_Click(object sender, RoutedEventArgs e)
        {
            var email = EmailBox.Text?.Trim();
            var password = PasswordBox.Password;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                MainWindow.ShowNotification("请输入邮箱和密码");
                return;
            }

            LoginBtn.IsEnabled = false;
            LoginBtn.Content = "登录中...";

            var result = await _authService.SignInAsync(email, password);
            if (result.Success)
            {
                // 统一登录同步管线：云端配置 + 离线记录迁移 + 词库 + 学习进度
                MainWindow.ShowPersistentNotification("正在从云端拉取数据...", "云同步");
                try
                {
                    await LoginFlow.CompleteLoginAsync(App.SharedViewModel!, email,
                        new Progress<string>(m => MainWindow.UpdateNotification(m)));
                    MainWindow.HideNotification();
                    Debug.WriteLine("[CloudSync] Login sync completed.");
                }
                catch (Exception syncEx)
                {
                    MainWindow.HideNotification();
                    MainWindow.ShowNotification($"登录成功，但云端同步失败: {syncEx.Message}");
                }
                await UpdateStatusAsync();
            }
            else
            {
                MainWindow.ShowNotification($"登录失败: {result.ErrorMessage}");
            }

            LoginBtn.IsEnabled = true;
            LoginBtn.Content = "登 录";
        }

        private async void LogoutBtn_Click(object sender, RoutedEventArgs e)
        {
            await _authService.SignOutAsync();
            MainWindow.ShowNotification("已退出登录");
            await UpdateStatusAsync();
        }

        private void GoToRegister_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.GoToPage(typeof(RegisterPage));
        }

        // ── Manual Cloud Sync Buttons ───────────────────────────

        private async void CloudSyncAllBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!SupabaseService.Instance.IsLoggedIn)
            {
                MainWindow.ShowNotification("请先登录");
                return;
            }
            SetCloudButtonsEnabled(false);
            MainWindow.ShowPersistentNotification("正在同步所有数据...", "云端同步");

            try
            {
                var sync = new CloudSyncService();

                // 1. 下载云端 settings.json + 翻译文件 + 分组 → 写磁盘
                MainWindow.UpdateNotification("正在拉取云端数据...");
                await sync.DownloadSettingsFileAsync();
                await sync.DownloadTranslationFileAsync();
                await sync.DownloadWordGroupsFileAsync();

                // 2. 从磁盘重载
                await App.SharedViewModel!.Settings.LoadSettingsAsync();
                App.SharedViewModel.StudyPlan = App.SharedViewModel.Settings.Settings.StudyPlan;

                // 3. 上传本地 settings.json + 翻译文件 + 分组 + 词库
                MainWindow.UpdateNotification("正在上传本地数据...");
                await sync.UploadSettingsFileAsync();
                await sync.UploadTranslationFileAsync();
                await sync.UploadWordGroupsFileAsync();
                var wordBankService = new WordBankService();
                await wordBankService.AutoUploadLocalWordBanksAsync();

                // 4. 重载词库列表
                MainWindow.UpdateNotification("正在重新装载词库状态...");
                await App.SharedViewModel!.LoadWordListFilesAsync();

                MainWindow.HideNotification();
                MainWindow.ShowNotification("同步成功！");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CloudSync] SyncAll error: {ex.Message}");
                MainWindow.HideNotification();
                MainWindow.ShowNotification($"同步失败: {ex.Message}");
            }
            SetCloudButtonsEnabled(true);
        }

        private async void CloudUploadStudyBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sync = new CloudSyncService();
                await sync.UploadSettingsFileAsync();
                MainWindow.ShowNotification("配置已上传");
            }
            catch (Exception ex) { MainWindow.ShowNotification($"上传失败: {ex.Message}"); }
        }

        private async void CloudDownloadStudyBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sync = new CloudSyncService();
                var ok = await sync.DownloadSettingsFileAsync();
                if (ok)
                {
                    await App.SharedViewModel!.Settings.LoadSettingsAsync();
                    App.SharedViewModel.StudyPlan = App.SharedViewModel.Settings.Settings.StudyPlan;
                    await App.SharedViewModel!.LoadWordListFilesAsync();
                    MainWindow.ShowNotification("配置已下载并重载");
                }
                else MainWindow.ShowNotification("云端无配置数据");
            }
            catch (Exception ex) { MainWindow.ShowNotification($"下载失败: {ex.Message}"); }
        }

        private async void CloudUploadGroupsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sync = new CloudSyncService();
                await sync.UploadWordGroupsFileAsync();
                MainWindow.ShowNotification("词库分组已上传");
            }
            catch (Exception ex) { MainWindow.ShowNotification($"上传失败: {ex.Message}"); }
        }

        private async void CloudDownloadGroupsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sync = new CloudSyncService();
                var ok = await sync.DownloadWordGroupsFileAsync();
                MainWindow.ShowNotification(ok ? "词库分组已下载" : "云端无分组数据");
            }
            catch (Exception ex) { MainWindow.ShowNotification($"下载失败: {ex.Message}"); }
        }

        private async void CloudUploadTransBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sync = new CloudSyncService();
                await sync.UploadTranslationFileAsync();
                MainWindow.ShowNotification("翻译文件已上传");
            }
            catch (Exception ex) { MainWindow.ShowNotification($"上传失败: {ex.Message}"); }
        }

        private async void CloudDownloadTransBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sync = new CloudSyncService();
                var ok = await sync.DownloadTranslationFileAsync();
                MainWindow.ShowNotification(ok ? "翻译文件已下载" : "云端无翻译文件");
            }
            catch (Exception ex) { MainWindow.ShowNotification($"下载失败: {ex.Message}"); }
        }

        private async void CloudUploadWordsBtn_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.ShowPersistentNotification("正在上传词库...", "词库上传");
            try
            {
                var wordBankService = new WordBankService();
                var uploaded = await wordBankService.AutoUploadLocalWordBanksAsync((cur, total, name) =>
                    MainWindow.UpdateNotification($"({cur}/{total}) {name}"));
                MainWindow.HideNotification();
                MainWindow.ShowNotification(uploaded > 0 ? $"已上传 {uploaded} 个词库" : "词库已是最新");
            }
            catch (Exception ex)
            {
                MainWindow.HideNotification();
                MainWindow.ShowNotification($"上传失败: {ex.Message}");
            }
        }

        private async void CloudDownloadWordsBtn_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.ShowPersistentNotification("正在下载词库...", "词库下载");
            try
            {
                var wordBankService = new WordBankService();
                var downloaded = await wordBankService.AutoDownloadWordBanksAsync((cur, total, name) =>
                    MainWindow.UpdateNotification($"({cur}/{total}) {name}"));
                MainWindow.HideNotification();
                MainWindow.ShowNotification(downloaded > 0 ? $"已下载 {downloaded} 个词库" : "词库已是最新");
            }
            catch (Exception ex)
            {
                MainWindow.HideNotification();
                MainWindow.ShowNotification($"下载失败: {ex.Message}");
            }
        }

        private void SetCloudButtonsEnabled(bool enabled)
        {
            CloudSyncAllBtn.IsEnabled = enabled;
            CloudUploadStudyBtn.IsEnabled = enabled;
            CloudDownloadStudyBtn.IsEnabled = enabled;
            CloudUploadGroupsBtn.IsEnabled = enabled;
            CloudDownloadGroupsBtn.IsEnabled = enabled;
            CloudUploadTransBtn.IsEnabled = enabled;
            CloudDownloadTransBtn.IsEnabled = enabled;
            CloudUploadWordsBtn.IsEnabled = enabled;
            CloudDownloadWordsBtn.IsEnabled = enabled;
        }
    }
}