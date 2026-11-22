using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using English_Listen_WinUI.Models;

namespace English_Listen_WinUI.Services
{
    /// <summary>
    /// 词库云端同步：全部词库存为单个 KV（/api/data/word_banks，List&lt;WordBank&gt; JSON，内容 inline）。
    /// 按文件名去重，ContentHash 判断是否需要下载。
    // shortcut: 词库总量超 10MB 时 PUT 会被服务端拒绝（值上限），届时需拆分为逐库键
    /// </summary>
    public class WordBankService
    {
        private const string Key = "word_banks";

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

        private static int CountWords(string content)
        {
            return content.Split(new[] { '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        public async Task<List<WordBank>> GetUserWordBanksAsync()
        {
            try
            {
                if (!WebApiService.Instance.IsLoggedIn) return new List<WordBank>();

                var json = await WebApiService.Instance.GetJsonAsync(Key);
                if (string.IsNullOrEmpty(json)) return new List<WordBank>();

                return JsonSerializer.Deserialize<List<WordBank>>(json) ?? new List<WordBank>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[WordBank] GetUserWordBanks error: " + ex.Message);
                return new List<WordBank>();
            }
        }

        public async Task<int> AutoUploadLocalWordBanksAsync(Action<int, int, string>? onProgress = null)
        {
            int uploaded = 0;
            try
            {
                if (!WebApiService.Instance.IsLoggedIn) return 0;

                var banks = await GetUserWordBanksAsync();
                var existingNames = new HashSet<string>(banks.Select(b => b.Name));

                var wordlistDir = Path.Combine(
                    Windows.Storage.ApplicationData.Current.LocalFolder.Path, "wordlist");
                if (!Directory.Exists(wordlistDir)) return 0;

                var files = Directory.GetFiles(wordlistDir, "*.txt");
                var newFiles = files.Where(f => !existingNames.Contains(Path.GetFileName(f))).ToList();

                Debug.WriteLine($"[WordBank] AutoUpload: {files.Length} total, {newFiles.Count} new, {existingNames.Count} in cloud");

                int total = newFiles.Count;
                int current = 0;
                foreach (var file in newFiles)
                {
                    current++;
                    var fileName = Path.GetFileName(file);
                    onProgress?.Invoke(current, total, fileName);

                    var content = await File.ReadAllTextAsync(file);
                    banks.Add(new WordBank
                    {
                        Name = fileName,
                        FileSize = new FileInfo(file).Length,
                        WordCount = CountWords(content),
                        UploadedAt = DateTime.UtcNow,
                        ContentHash = ComputeHash(content),
                        Content = content,
                    });
                    uploaded++;
                }

                if (uploaded > 0)
                    await PutBanksAsync(banks);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[WordBank] AutoUpload error: " + ex.Message);
            }
            return uploaded;
        }

        /// <summary>
        /// 上传/更新单个本地词库：同名云端条目按 ContentHash 判断是否需要更新内容
        /// （AutoUpload 只按文件名新增，同名文件改内容后必须走这里重传）。
        /// </summary>
        public async Task<bool> UploadSingleWordBankAsync(string fileName)
        {
            try
            {
                if (!WebApiService.Instance.IsLoggedIn) return false;

                var localPath = Path.Combine(
                    Windows.Storage.ApplicationData.Current.LocalFolder.Path, "wordlist", fileName);
                if (!File.Exists(localPath)) return false;

                var content = await File.ReadAllTextAsync(localPath);
                var hash = ComputeHash(content);

                var banks = await GetUserWordBanksAsync();
                var bank = banks.FirstOrDefault(b => b.Name == fileName);
                if (bank != null && bank.ContentHash == hash) return false; // 云端已是最新

                if (bank == null)
                {
                    bank = new WordBank { Name = fileName };
                    banks.Add(bank);
                }
                bank.FileSize = new FileInfo(localPath).Length;
                bank.WordCount = CountWords(content);
                bank.UploadedAt = DateTime.UtcNow;
                bank.ContentHash = hash;
                bank.Content = content;

                await PutBanksAsync(banks);
                Debug.WriteLine($"[WordBank] Uploaded single: {fileName}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[WordBank] UploadSingle error: " + ex.Message);
                return false;
            }
        }

        public async Task<int> AutoDownloadWordBanksAsync(Action<int, int, string>? onProgress = null)
        {
            int downloaded = 0;
            try
            {
                if (!WebApiService.Instance.IsLoggedIn) return 0;

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
                    if (string.IsNullOrEmpty(bank.Content)) continue;

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

                    await File.WriteAllTextAsync(localPath, bank.Content);
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

        private static async Task PutBanksAsync(List<WordBank> banks)
        {
            var error = await WebApiService.Instance.PutJsonAsync(Key, JsonSerializer.Serialize(banks));
            if (error != null) throw new InvalidOperationException(error);
        }
    }
}
