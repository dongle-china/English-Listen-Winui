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
    public class TestResultService
    {
        private readonly SupabaseService _supabase = SupabaseService.Instance;

        public async Task UploadResultAsync(TestResult result)
        {
            try
            {
                var user = _supabase.CurrentUser;
                if (user == null) return;

                var cloudResult = new CloudTestResult
                {
                    UserId = Guid.Parse(user.Id!),
                    Timestamp = result.Timestamp,
                    TotalWords = result.TotalWords,
                    CorrectCount = result.CorrectCount,
                    Accuracy = result.Accuracy,
                    WordListName = result.WordListName,
                    RecordType = result.RecordType ?? "dictation",
                    WordDetailsJson = result.Words != null
                        ? JsonSerializer.Serialize(result.Words)
                        : "[]"
                };

                await _supabase.Client.From<CloudTestResult>().Insert(cloudResult);
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
                var user = _supabase.CurrentUser;
                if (user == null) return new List<TestResult>();

                var userId = Guid.Parse(user.Id!);
                var result = await _supabase.Client.From<CloudTestResult>()
                    .Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, userId.ToString())
                    .Order(c => c.Timestamp, Supabase.Postgrest.Constants.Ordering.Descending)
                    .Get();

                return (result.Models ?? new List<CloudTestResult>())
                    .Select(ToTestResult)
                    .ToList();
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

        private static TestResult ToTestResult(CloudTestResult c)
        {
            var words = new List<WordTranslationPair>();
            try
            {
                if (!string.IsNullOrEmpty(c.WordDetailsJson))
                    words = JsonSerializer.Deserialize<List<WordTranslationPair>>(c.WordDetailsJson) ?? new();
            }
            catch { }

            return new TestResult
            {
                Timestamp = c.Timestamp,
                TotalWords = c.TotalWords,
                CorrectCount = c.CorrectCount,
                Accuracy = c.Accuracy,
                WordListName = c.WordListName,
                RecordType = c.RecordType,
                Words = words.Count > 0 ? words : null
            };
        }
    }
}
