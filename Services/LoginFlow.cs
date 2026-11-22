using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using English_Listen_WinUI.Models;
using English_Listen_WinUI.ViewModels;

namespace English_Listen_WinUI.Services
{
    /// <summary>
    /// 登录成功后的统一同步管线：云端配置拉取合并 → 离线记录迁移 → 词库上传 → 测试记录双向同步。
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

            // 下载前快照离线状态：云端文件会整文件覆盖本地，离线期间的新增要靠快照并回
            var offlinePlan = vm.Settings.Settings.StudyPlan;
            var offlineTranslation = await ReadFileIfExistsAsync(CloudSyncService.GetTranslationFilePath());
            var offlineGroups = await ReadFileIfExistsAsync(CloudSyncService.GetWordGroupsFilePath());

            await sync.DownloadSettingsFileAsync();
            await sync.DownloadTranslationFileAsync();
            await sync.DownloadWordGroupsFileAsync();

            // 云端 settings.json 覆盖本地后重载；离线学习进度并回（其余字段云端优先）
            await vm.Settings.LoadSettingsAsync();
            if (vm.Settings.Settings.StudyPlan == null)
            {
                vm.Settings.Settings.StudyPlan = offlinePlan;
            }
            else
            {
                MergeStudyPlan(vm.Settings.Settings.StudyPlan, offlinePlan);
            }
            vm.Settings.Settings.CurrentUser = email;
            await vm.Settings.SaveSettingsAsync();
            vm.StudyPlan = vm.Settings.Settings.StudyPlan ?? vm.StudyPlan;

            // 翻译库 / 分组：把离线新增并回云端版本后写盘，并去抖回传合并结果
            await MergeTranslationFileAsync(offlineTranslation);
            await MergeWordGroupsFileAsync(offlineGroups);

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

        /// <summary>把离线期间的学习进度并入云端版：列表并集、计数取较大值，其余字段云端优先。</summary>
        private static void MergeStudyPlan(StudyPlanSettings target, StudyPlanSettings? offline)
        {
            if (offline == null) return;
            AppendMissing(target.LearnedWords, offline.LearnedWords);
            AppendMissing(target.MemorizedWords, offline.MemorizedWords);
            target.CompletedDays = Math.Max(target.CompletedDays, offline.CompletedDays);
            target.TotalLearnedWords = Math.Max(target.TotalLearnedWords, offline.TotalLearnedWords);
            if (offline.LastStudyDate > target.LastStudyDate)
            {
                target.LastStudyDate = offline.LastStudyDate;
            }
        }

        /// <summary>把 source 中 target 还没有的条目按原顺序追加进去。</summary>
        private static void AppendMissing(List<string> target, List<string> source)
        {
            if (source.Count == 0) return;
            var seen = new HashSet<string>(target);
            foreach (var item in source)
            {
                if (seen.Add(item))
                {
                    target.Add(item);
                }
            }
        }

        /// <summary>翻译库：云端文件已覆盖本地，把离线快照并回（同词冲突本地优先）后写盘并回传。</summary>
        private static async Task MergeTranslationFileAsync(string? offlineContent)
        {
            if (string.IsNullOrEmpty(offlineContent)) return;
            var path = CloudSyncService.GetTranslationFilePath();
            var merged = MergePipeLines(local: offlineContent, cloud: await ReadFileIfExistsAsync(path));
            await File.WriteAllTextAsync(path, merged);
            QuietCloudSync.QueueTranslationUpload();
        }

        /// <summary>合并 word|translation 行（同词本地优先），输出按键稳定排序。</summary>
        private static string MergePipeLines(string local, string? cloud)
        {
            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ParsePipeLines(cloud, merged);
            ParsePipeLines(local, merged); // 后入：同词覆盖云端，本地（最近使用）优先
            return string.Join(Environment.NewLine,
                merged.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => $"{kv.Key}|{kv.Value}"));
        }

        private static void ParsePipeLines(string? content, Dictionary<string, string> target)
        {
            if (string.IsNullOrEmpty(content)) return;
            foreach (var raw in content.Split('\n'))
            {
                var line = raw.Trim();
                var separatorIndex = line.IndexOf('|');
                if (separatorIndex <= 0 || separatorIndex >= line.Length - 1) continue;
                var word = line.Substring(0, separatorIndex).Trim();
                var translation = line.Substring(separatorIndex + 1).Trim();
                if (word.Length > 0 && translation.Length > 0)
                {
                    target[word] = translation;
                }
            }
        }

        /// <summary>分组：云端文件已覆盖本地，把离线新增的组/条目并回后写盘并回传。</summary>
        private static async Task MergeWordGroupsFileAsync(string? offlineContent)
        {
            if (string.IsNullOrEmpty(offlineContent)) return;
            var path = CloudSyncService.GetWordGroupsFilePath();
            var merged = MergeGroupIni(offline: offlineContent, cloud: await ReadFileIfExistsAsync(path));
            await File.WriteAllTextAsync(path, merged);
            QuietCloudSync.QueueWordGroupsUpload();
        }

        /// <summary>合并分组 INI：云端组在前、离线新增组追加；同名组内词库名并集。</summary>
        private static string MergeGroupIni(string offline, string? cloud)
        {
            var merged = ParseGroupIni(cloud);
            foreach (var group in ParseGroupIni(offline))
            {
                if (merged.TryGetValue(group.Key, out var cloudNames))
                {
                    AppendMissing(cloudNames, group.Value);
                }
                else
                {
                    merged[group.Key] = group.Value;
                }
            }

            var lines = new List<string>();
            foreach (var group in merged)
            {
                lines.Add($"[{group.Key}]");
                lines.AddRange(group.Value);
                lines.Add("");
            }
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>解析 [组名] + 词库名 行的 INI（与 SettingsService.LoadWordlistGroupsAsync 同规则）。</summary>
        private static Dictionary<string, List<string>> ParseGroupIni(string? content)
        {
            var groups = new Dictionary<string, List<string>>();
            if (string.IsNullOrEmpty(content)) return groups;
            List<string>? current = null;
            foreach (var raw in content.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    var name = line.Trim('[', ']');
                    if (!groups.TryGetValue(name, out current))
                    {
                        current = new List<string>();
                        groups[name] = current;
                    }
                }
                else if (current != null && !current.Contains(line))
                {
                    current.Add(line);
                }
            }
            return groups;
        }

        private static async Task<string?> ReadFileIfExistsAsync(string path)
        {
            return File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
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
