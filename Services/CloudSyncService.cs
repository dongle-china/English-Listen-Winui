using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace English_Listen_WinUI.Services
{
    /// <summary>
    /// 云同步（jetcpp.ccwu.cc KV 端点 /api/data/{key}）。
    /// settings.json / 翻译库 / 分组 各占一个 key，整文件上传下载。
    /// </summary>
    public class CloudSyncService
    {
        // ── Low-level CRUD ──────────────────────────────────────

        public async Task UploadAsync(string dataType, string json)
        {
            if (!WebApiService.Instance.IsLoggedIn) throw new UnauthorizedAccessException("请先登录");

            var error = await WebApiService.Instance.PutJsonAsync(dataType, json);
            if (error != null) throw new InvalidOperationException(error);

            Debug.WriteLine($"[CloudSync] Uploaded {dataType} ({json.Length} bytes)");
        }

        public async Task<string?> DownloadAsync(string dataType)
        {
            if (!WebApiService.Instance.IsLoggedIn) return null;

            var json = await WebApiService.Instance.GetJsonAsync(dataType);
            if (json != null)
                Debug.WriteLine($"[CloudSync] Downloaded {dataType} ({json.Length} bytes)");
            return json;
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
