using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using English_Listen_WinUI.ViewModels;

namespace English_Listen_WinUI.Services
{
    /// <summary>
    /// 登录成功后的统一同步管线：云端配置拉取 → 离线记录迁移 → 词库上传 → 测试记录双向同步。
    /// 标题栏登录与云端页登录共用，避免两处行为漂移。
    /// </summary>
    public static class LoginFlow
    {
        /// <summary>离线（未登录）默认用户目录名，与 SettingsService 的回退值一致。</summary>
        public const string OfflineUser = "未登录";

        public static async Task CompleteLoginAsync(MainViewModel vm, string email, IProgress<string>? progress = null)
        {
            if (vm == null) throw new ArgumentNullException(nameof(vm));

            progress?.Report("正在从云端拉取数据...");
            var sync = new CloudSyncService();
            await sync.DownloadSettingsFileAsync();
            await sync.DownloadTranslationFileAsync();
            await sync.DownloadWordGroupsFileAsync();

            // 云端 settings.json 可能覆盖本地，重载后再固定当前用户
            await vm.Settings.LoadSettingsAsync();
            vm.Settings.Settings.CurrentUser = email;
            await vm.Settings.SaveSettingsAsync();
            vm.StudyPlan = vm.Settings.Settings.StudyPlan ?? vm.StudyPlan;

            progress?.Report("正在合并本地离线记录...");
            await MergeOfflineHistoryAsync(vm);

            progress?.Report("正在上传词库...");
            await new WordBankService().AutoUploadLocalWordBanksAsync();

            progress?.Report("正在同步学习进度...");
            var testResultService = new TestResultService();
            await testResultService.MergeWithLocalAsync(vm);
            await testResultService.UploadLocalAsync(vm);

            await vm.LoadWordListFilesAsync();
            Debug.WriteLine($"[LoginFlow] CompleteLogin finished for {email}");
        }

        /// <summary>把离线（未登录）期间产生的测试记录迁移到当前登录账号名下（按时间戳去重）。</summary>
        private static async Task MergeOfflineHistoryAsync(MainViewModel vm)
        {
            try
            {
                var offline = await vm.Settings.LoadTestHistoryAsync(OfflineUser);
                if (offline.Count == 0) return;

                var targetUser = vm.Settings.Settings.CurrentUser ?? "";
                var current = await vm.Settings.LoadTestHistoryAsync(targetUser);
                var timestamps = new HashSet<DateTime>(current.Select(r => r.Timestamp));

                var merged = current
                    .Concat(offline.Where(r => !timestamps.Contains(r.Timestamp)))
                    .OrderByDescending(r => r.Timestamp)
                    .ToList();

                await vm.Settings.SaveTestHistoryAsync(targetUser, merged);

                vm.TestHistory = merged;
                vm.TestHistoryViewModels.Clear();
                foreach (var r in merged)
                    vm.TestHistoryViewModels.Add(new TestResultViewModel { Result = r });

                Debug.WriteLine($"[LoginFlow] Merged {merged.Count - current.Count} offline records into '{targetUser}'");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LoginFlow] MergeOfflineHistory error: {ex.Message}");
            }
        }
    }
}
