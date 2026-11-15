using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using English_Listen_WinUI.Models;

namespace English_Listen_WinUI.Services
{
    public class CloudSyncService
    {
        private readonly SupabaseService _supabase = SupabaseService.Instance;

        // ── Low-level CRUD ──────────────────────────────────────

        public async Task UploadAsync(string dataType, string json)
        {
            var user = _supabase.CurrentUser;
            if (user == null) throw new UnauthorizedAccessException("请先登录");

            var userId = Guid.Parse(user.Id!);

            var existing = await _supabase.Client.From<CloudSyncData>()
                .Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, userId.ToString())
                .Filter("data_type", Supabase.Postgrest.Constants.Operator.Equals, dataType)
                .Get();

            var record = existing.Models?.FirstOrDefault();

            if (record != null)
            {
                record.DataJson = json;
                record.UpdatedAt = DateTime.UtcNow;
                await _supabase.Client.From<CloudSyncData>()
                    .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, record.Id.ToString())
                    .Set(r => r.DataJson, json)
                    .Set(r => r.UpdatedAt, record.UpdatedAt)
                    .Update();
            }
            else
            {
                var newRecord = new CloudSyncData
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    DataType = dataType,
                    DataJson = json,
                    UpdatedAt = DateTime.UtcNow
                };
                await _supabase.Client.From<CloudSyncData>().Insert(newRecord);
            }

            Debug.WriteLine($"[CloudSync] Uploaded {dataType} ({json.Length} bytes)");
        }

        public async Task<string?> DownloadAsync(string dataType)
        {
            var user = _supabase.CurrentUser;
            if (user == null) return null;

            var userId = Guid.Parse(user.Id!);
            var result = await _supabase.Client.From<CloudSyncData>()
                .Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, userId.ToString())
                .Filter("data_type", Supabase.Postgrest.Constants.Operator.Equals, dataType)
                .Get();

            var record = result.Models?.FirstOrDefault();
            if (record == null) return null;

            Debug.WriteLine($"[CloudSync] Downloaded {dataType} ({record.DataJson.Length} bytes)");
            return record.DataJson;
        }

        // ── 文件级上传/下载 ────────────────────────────────────

        private static string GetSettingsFilePath()
        {
            string appDataPath;
            try { appDataPath = Windows.Storage.ApplicationData.Current.LocalFolder.Path; }
            catch { appDataPath = AppContext.BaseDirectory; }
            return Path.Combine(appDataPath, "config", "settings.json");
        }

        private static string GetTranslationFilePath()
        {
            string appDataPath;
            try { appDataPath = Windows.Storage.ApplicationData.Current.LocalFolder.Path; }
            catch { appDataPath = AppContext.BaseDirectory; }
            return Path.Combine(appDataPath, "data", "translation_library.txt");
        }

        private static string GetWordGroupsFilePath()
        {
            string appDataPath;
            try { appDataPath = Windows.Storage.ApplicationData.Current.LocalFolder.Path; }
            catch { appDataPath = AppContext.BaseDirectory; }
            return Path.Combine(appDataPath, "config", "wordlist_groups.ini");
        }

        public async Task UploadSettingsFileAsync()
        {
            var path = GetSettingsFilePath();
            if (!File.Exists(path)) throw new FileNotFoundException("settings.json 不存在");
            var json = await File.ReadAllTextAsync(path);
            await UploadAsync("settings_file", json);
        }

        public async Task<bool> DownloadSettingsFileAsync()
        {
            var json = await DownloadAsync("settings_file");
            if (json == null) return false;
            var path = GetSettingsFilePath();
            var dir = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(path, json);
            return true;
        }

        public async Task UploadTranslationFileAsync()
        {
            var path = GetTranslationFilePath();
            if (!File.Exists(path)) return;
            var content = await File.ReadAllTextAsync(path);
            await UploadAsync("translation_file", content);
        }

        public async Task<bool> DownloadTranslationFileAsync()
        {
            var content = await DownloadAsync("translation_file");
            if (content == null) return false;
            var path = GetTranslationFilePath();
            var dir = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(path, content);
            return true;
        }

        public async Task UploadWordGroupsFileAsync()
        {
            var path = GetWordGroupsFilePath();
            if (!File.Exists(path)) return;
            var content = await File.ReadAllTextAsync(path);
            await UploadAsync("wordgroups_file", content);
        }

        public async Task<bool> DownloadWordGroupsFileAsync()
        {
            var content = await DownloadAsync("wordgroups_file");
            if (content == null) return false;
            var path = GetWordGroupsFilePath();
            var dir = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(path, content);
            return true;
        }
    }
}
