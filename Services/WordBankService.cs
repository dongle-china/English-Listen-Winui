using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using English_Listen_WinUI.Models;

namespace English_Listen_WinUI.Services
{
    public class WordBankService
    {
        private readonly SupabaseService _supabase = SupabaseService.Instance;

        private const string BUCKET_NAME = "wordbanks";

        private static string ComputeHash(string content)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
            return Convert.ToHexString(bytes);
        }

        public static string ComputeFileHash(string filePath)
        {
            var content = File.ReadAllText(filePath);
            return ComputeHash(content);
        }

        public async Task<WordBank?> UploadAsync(string filePath, string? description = null, Action<float>? onProgress = null)
        {
            try
            {
                var user = _supabase.CurrentUser;
                if (user == null) throw new UnauthorizedAccessException("请先登录");

                var userId = Guid.Parse(user.Id!);
                var fileName = Path.GetFileName(filePath);
                var extension = Path.GetExtension(filePath);
                var storagePath = $"{user.Id}/{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{extension}";
                var fileBytes = await File.ReadAllBytesAsync(filePath);

                await EnsureBucketExistsAsync();

                var bucket = _supabase.Client.Storage.From(BUCKET_NAME);

                Debug.WriteLine($"[WordBank] Uploading '{filePath}' -> '{storagePath}' ({fileBytes.Length} bytes)");

                await bucket.Upload(fileBytes, storagePath, new Supabase.Storage.FileOptions { ContentType = "text/plain" });

                Debug.WriteLine("[WordBank] Storage upload OK: " + storagePath);

                var content = await File.ReadAllTextAsync(filePath);
                var wordCount = CountWords(content);
                var hash = ComputeHash(content);

                var wordBank = new WordBank
                {
                    UserId = userId,
                    Name = fileName,
                    FileUrl = storagePath,
                    FileSize = fileBytes.Length,
                    WordCount = wordCount,
                    UploadedAt = DateTime.UtcNow,
                    Description = description ?? string.Empty,
                    ContentHash = hash
                };

                var result = await _supabase.Client.From<WordBank>()
                    .Insert(wordBank);

                var inserted = result.Models?.FirstOrDefault();
                Debug.WriteLine("[WordBank] Upload success: " + fileName + ", " + wordCount + " words, DB id=" + inserted?.Id);
                return inserted;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[WordBank] Upload error: " + ex);
                return null;
            }
        }

        public async Task<List<WordBank>> GetUserWordBanksAsync()
        {
            try
            {
                var user = _supabase.CurrentUser;
                if (user == null) return new List<WordBank>();

                var userId = Guid.Parse(user.Id!);
                var result = await _supabase.Client.From<WordBank>()
                    .Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, userId.ToString())
                    .Order(w => w.UploadedAt, Supabase.Postgrest.Constants.Ordering.Descending)
                    .Get();

                return result.Models ?? new List<WordBank>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[WordBank] GetUserWordBanks error: " + ex.Message);
                return new List<WordBank>();
            }
        }

        public async Task<bool> DeleteAsync(Guid wordBankId)
        {
            try
            {
                var user = _supabase.CurrentUser;
                if (user == null) return false;

                var userId = Guid.Parse(user.Id!);
                var record = await _supabase.Client.From<WordBank>()
                    .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, wordBankId.ToString())
                    .Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, userId.ToString())
                    .Get();

                var wordBank = record.Models?.FirstOrDefault();
                if (wordBank == null) return false;

                var bucket = _supabase.Client.Storage.From(BUCKET_NAME);
                await bucket.Remove(new List<string> { wordBank.FileUrl });

                await _supabase.Client.From<WordBank>()
                    .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, wordBankId.ToString())
                    .Delete();

                Debug.WriteLine("[WordBank] Delete success: " + wordBank.Name);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[WordBank] Delete error: " + ex.Message);
                return false;
            }
        }

        public async Task<string?> DownloadAsync(Guid wordBankId)
        {
            try
            {
                var user = _supabase.CurrentUser;
                if (user == null) return null;

                var userId = Guid.Parse(user.Id!);
                var record = await _supabase.Client.From<WordBank>()
                    .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, wordBankId.ToString())
                    .Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, userId.ToString())
                    .Get();

                var wordBank = record.Models?.FirstOrDefault();
                if (wordBank == null) return null;

                var bucket = _supabase.Client.Storage.From(BUCKET_NAME);
                var bytes = await bucket.Download(wordBank.FileUrl, (EventHandler<float>?)null);

                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[WordBank] Download error: " + ex.Message);
                return null;
            }
        }

        private static int CountWords(string content)
        {
            return content.Split(new[] { '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private bool _bucketEnsured;
        private readonly object _bucketLock = new();

        private async Task EnsureBucketExistsAsync()
        {
            if (_bucketEnsured) return;
            lock (_bucketLock)
            {
                if (_bucketEnsured) return;
            }

            try
            {
                var buckets = await _supabase.Client.Storage.ListBuckets();
                var exists = buckets?.Any(b => b.Name == BUCKET_NAME) ?? false;
                if (!exists)
                {
                    Debug.WriteLine($"[WordBank] Bucket '{BUCKET_NAME}' not found, creating...");
                    await _supabase.Client.Storage.CreateBucket(BUCKET_NAME);
                    Debug.WriteLine($"[WordBank] Bucket '{BUCKET_NAME}' created");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[WordBank] EnsureBucket error: " + ex.Message);
            }

            lock (_bucketLock) { _bucketEnsured = true; }
        }

        public async Task<int> AutoUploadLocalWordBanksAsync(Action<int, int, string>? onProgress = null)
        {
            int uploaded = 0;
            try
            {
                var user = _supabase.CurrentUser;
                if (user == null) return 0;

                var existingBanks = await GetUserWordBanksAsync();
                var existingNames = new HashSet<string>(existingBanks?.Select(b => b.Name) ?? Enumerable.Empty<string>());

                var wordlistDir = Path.Combine(
                    Windows.Storage.ApplicationData.Current.LocalFolder.Path, "wordlist");
                if (!Directory.Exists(wordlistDir)) return 0;

                var files = Directory.GetFiles(wordlistDir, "*.txt").ToArray();
                var newFiles = files.Where(f => !existingNames.Contains(Path.GetFileName(f))).ToArray();

                Debug.WriteLine($"[WordBank] AutoUpload: {files.Length} total, {newFiles.Length} new, {existingNames.Count} in cloud");

                int total = newFiles.Length;
                if (total == 0) return 0;

                int current = 0;
                foreach (var file in newFiles)
                {
                    current++;
                    var fileName = Path.GetFileName(file);
                    onProgress?.Invoke(current, total, fileName);

                    var result = await UploadAsync(file);
                    if (result != null)
                        uploaded++;
                    else
                        Debug.WriteLine($"[WordBank] AutoUpload failed for: {fileName}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[WordBank] AutoUpload error: " + ex.Message);
            }
            return uploaded;
        }

        public async Task<int> AutoDownloadWordBanksAsync(Action<int, int, string>? onProgress = null)
        {
            int downloaded = 0;
            try
            {
                var user = _supabase.CurrentUser;
                if (user == null) return 0;

                var cloudBanks = await GetUserWordBanksAsync();
                if (cloudBanks.Count == 0) return 0;

                var wordlistDir = Path.Combine(
                    Windows.Storage.ApplicationData.Current.LocalFolder.Path, "wordlist");
                Directory.CreateDirectory(wordlistDir);

                int total = cloudBanks.Count;
                int current = 0;

                foreach (var bank in cloudBanks)
                {
                    current++;
                    onProgress?.Invoke(current, total, bank.Name);

                    var localPath = Path.Combine(wordlistDir, bank.Name);

                    // hash check: skip if same
                    if (File.Exists(localPath) && !string.IsNullOrEmpty(bank.ContentHash))
                    {
                        var localHash = ComputeFileHash(localPath);
                        if (localHash == bank.ContentHash)
                        {
                            Debug.WriteLine("[WordBank] Skip (hash match): " + bank.Name);
                            continue;
                        }
                    }

                    var content = await DownloadAsync(bank.Id);
                    if (content == null) continue;

                    await File.WriteAllTextAsync(localPath, content);

                    // update hash if empty
                    if (string.IsNullOrEmpty(bank.ContentHash))
                    {
                        bank.ContentHash = ComputeHash(content);
                        try
                        {
                            await _supabase.Client.From<WordBank>()
                                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, bank.Id.ToString())
                                .Set(w => w.ContentHash, bank.ContentHash)
                                .Update();
                        }
                        catch { }
                    }

                    downloaded++;
                    Debug.WriteLine("[WordBank] Downloaded: " + bank.Name);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[WordBank] AutoDownload error: " + ex.Message);
            }
            return downloaded;
        }

        public int GetLocalWordBankCount()
        {
            var wordlistDir = Path.Combine(
                Windows.Storage.ApplicationData.Current.LocalFolder.Path, "wordlist");
            if (!Directory.Exists(wordlistDir)) return 0;
            return Directory.GetFiles(wordlistDir, "*.txt").Length;
        }

        public async Task<int> GetCloudWordBankCountAsync()
        {
            try
            {
                var banks = await GetUserWordBanksAsync();
                return banks.Count;
            }
            catch { return 0; }
        }
    }
}
