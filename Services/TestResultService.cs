using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using English_Listen_WinUI.Models;
using English_Listen_WinUI.ViewModels;

namespace English_Listen_WinUI.Services
{
    /// <summary>
    /// 测试记录云端同步：整表存为单个 KV（/api/data/test_history，值为 List&lt;TestResult&gt; JSON），
    /// 按 Timestamp 去重（与原 Supabase test_results 表的合并/去重算法一致）。
    /// </summary>
    public class TestResultService
    {
        private const string Key = "test_history";

        public async Task UploadResultAsync(TestResult result)
        {
            try
            {
                if (!WebApiService.Instance.IsLoggedIn) return;

                var cloud = await DownloadAllAsync();
                if (cloud.Any(r => r.Timestamp == result.Timestamp)) return;

                cloud.Add(result);
                await PutHistoryAsync(cloud);
                Debug.WriteLine($"[TestResult] Uploaded: {result.WordListName} @ {result.Timestamp}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TestResult] Upload error: {ex.Message}");
            }
        }

        public async Task<List<TestResult>> DownloadAllAsync()
        {
            try
            {
                if (!WebApiService.Instance.IsLoggedIn) return new List<TestResult>();

                var json = await WebApiService.Instance.GetJsonAsync(Key);
                if (string.IsNullOrEmpty(json)) return new List<TestResult>();

                return JsonSerializer.Deserialize<List<TestResult>>(json) ?? new List<TestResult>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TestResult] Download error: {ex.Message}");
                return new List<TestResult>();
            }
        }

        public async Task MergeWithLocalAsync(MainViewModel viewModel)
        {
            try
            {
                var cloudResults = await DownloadAllAsync();
                if (cloudResults.Count == 0) return;

                var local = viewModel.TestHistory;
                var localTimestamps = new HashSet<DateTime>(
                    local.Select(r => r.Timestamp));

                var newResults = cloudResults
                    .Where(r => !localTimestamps.Contains(r.Timestamp))
                    .ToList();

                if (newResults.Count == 0) return;

                local.AddRange(newResults);
                local.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));

                viewModel.TestHistoryViewModels.Clear();
                foreach (var r in local)
                    viewModel.TestHistoryViewModels.Add(new TestResultViewModel { Result = r });

                // persist merged list locally
                var username = viewModel.Settings.Settings.CurrentUser ?? "";
                if (!string.IsNullOrEmpty(username))
                    await viewModel.Settings.SaveTestHistoryAsync(username, local);

                Debug.WriteLine($"[TestResult] Merged {newResults.Count} cloud results into local");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TestResult] Merge error: {ex.Message}");
            }
        }

        /// <summary>把本地存在、云端缺失的测试记录全部上传（按时间戳去重），返回上传条数。</summary>
        public async Task<int> UploadLocalAsync(MainViewModel viewModel)
        {
            try
            {
                if (!WebApiService.Instance.IsLoggedIn) return 0;

                var cloud = await DownloadAllAsync();
                var cloudTimestamps = new HashSet<DateTime>(cloud.Select(r => r.Timestamp));

                var toUpload = (viewModel.TestHistory ?? new List<TestResult>())
                    .Where(r => !cloudTimestamps.Contains(r.Timestamp))
                    .ToList();

                if (toUpload.Count == 0) return 0;

                cloud.AddRange(toUpload);
                await PutHistoryAsync(cloud);

                Debug.WriteLine($"[TestResult] Uploaded {toUpload.Count} local-only results");
                return toUpload.Count;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TestResult] UploadLocal error: {ex.Message}");
                return 0;
            }
        }

        /// <summary>清空云端测试记录（本地"清空学习记录"时同步调用，防止下次登录合并时复活）。</summary>
        public async Task ClearCloudAsync()
        {
            try
            {
                if (!WebApiService.Instance.IsLoggedIn) return;
                await PutHistoryAsync(new List<TestResult>());
                Debug.WriteLine("[TestResult] Cleared cloud history");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[TestResult] ClearCloud error: " + ex.Message);
            }
        }

        private static async Task PutHistoryAsync(List<TestResult> history)
        {
            var error = await WebApiService.Instance.PutJsonAsync(Key, JsonSerializer.Serialize(history));
            if (error != null) throw new InvalidOperationException(error);
        }
    }
}
