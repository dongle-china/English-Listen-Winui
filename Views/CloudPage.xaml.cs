using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using English_Listen_WinUI.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace English_Listen_WinUI.Views
{
    public sealed partial class CloudPage : Page
    {
        public CloudPage()
        {
            this.InitializeComponent();
            Loaded += CloudPage_Loaded;
            WebApiService.LoginStateChanged += OnLoginStateChanged;
            Unloaded += (_, _) => WebApiService.LoginStateChanged -= OnLoginStateChanged;
        }

        private void OnLoginStateChanged()
        {
            DispatcherQueue.TryEnqueue(async () => await UpdateStatusAsync());
        }

        private async void CloudPage_Loaded(object sender, RoutedEventArgs e)
        {
            await UpdateStatusAsync();
        }

        private async Task UpdateStatusAsync()
        {
            if (WebApiService.Instance.IsLoggedIn)
            {
                var web = WebApiService.Instance;
                StatusText.Text = "欢迎回来";
                LoginPanel.Visibility = Visibility.Collapsed;
                LoggedInPanel.Visibility = Visibility.Visible;
                RegisterLink.Visibility = Visibility.Collapsed;
                CloudSyncPanel.Visibility = Visibility.Visible;

                UserEmailText.Text = string.IsNullOrWhiteSpace(web.Nickname)
                    ? (web.Email ?? "")
                    : web.Nickname!;
                UserRoleText.Text = "在线账户";
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

        private void LoginBtn_Click(object sender, RoutedEventArgs e)
        {
            // 复用标题栏登录对话框（设备登录：网页生成登录码 → 输入换凭证）
            if (App.MainWindow is MainWindow mw)
            {
                mw.ShowLoginDialogFromProfile();
            }
        }

        private async void LogoutBtn_Click(object sender, RoutedEventArgs e)
        {
            await WebApiService.Instance.SignOutAsync();
            MainWindow.ShowNotification("已退出登录");
            await UpdateStatusAsync();
        }

        private async void GoToRegister_Click(object sender, RoutedEventArgs e)
        {
            // 注册需邮箱验证码，只能在网页端完成
            await Windows.System.Launcher.LaunchUriAsync(new Uri(WebApiService.BaseUrl + "/auth"));
        }

        // ── Manual Cloud Sync Buttons ───────────────────────────

        private async void CloudSyncAllBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!WebApiService.Instance.IsLoggedIn)
            {
                MainWindow.ShowNotification("请先登录");
                return;
            }
            SetCloudButtonsEnabled(false);
            MainWindow.ShowPersistentNotification("正在同步所有数据...", "云端同步");

            try
            {
                var sync = new CloudSyncService();

                // 0. 快照本地学习状态：下载覆盖后与本快照合并，本地离线学习进度不丢
                var localPlan = App.SharedViewModel!.StudyPlan;
                var localLearned = new HashSet<string>(localPlan.LearnedWords ?? new());
                var localMemorized = new HashSet<string>(localPlan.MemorizedWords ?? new());
                var localSelectedWords = new HashSet<string>(localPlan.SelectedWords ?? new());
                var localSelectedWordLists = new List<string>(localPlan.SelectedWordLists ?? new());

                // 1. 下载云端 settings.json + 翻译文件 + 分组 → 写磁盘
                MainWindow.UpdateNotification("正在拉取云端数据...");
                await sync.DownloadSettingsFileAsync();
                await sync.DownloadTranslationFileAsync();
                await sync.DownloadWordGroupsFileAsync();

                // 2. 从磁盘重载
                await App.SharedViewModel!.Settings.LoadSettingsAsync();
                App.SharedViewModel.StudyPlan = App.SharedViewModel.Settings.Settings.StudyPlan;

                // 2.5 合并学习状态：单词列表取并集，词库选择本地优先；合并结果随后上传
                var plan = App.SharedViewModel.StudyPlan;
                localLearned.UnionWith(plan.LearnedWords ?? new());
                plan.LearnedWords = localLearned.ToList();
                localMemorized.UnionWith(plan.MemorizedWords ?? new());
                plan.MemorizedWords = localMemorized.ToList();
                localSelectedWords.UnionWith(plan.SelectedWords ?? new());
                plan.SelectedWords = localSelectedWords.ToList();
                if (localSelectedWordLists.Count > 0)
                    plan.SelectedWordLists = localSelectedWordLists;
                await App.SharedViewModel.SaveStudyPlanAsync();

                // 2.6 学习历史：云端记录合并到本地，本地独有记录补传云端
                MainWindow.UpdateNotification("正在同步学习历史...");
                if (App.SharedViewModel.TestHistory == null)
                    await App.SharedViewModel.LoadTestHistoryAsync();
                var testResultService = new TestResultService();
                await testResultService.MergeWithLocalAsync(App.SharedViewModel);
                await testResultService.UploadLocalAsync(App.SharedViewModel);

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
