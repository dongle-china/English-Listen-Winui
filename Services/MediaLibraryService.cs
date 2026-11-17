using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using English_Listen_WinUI.Models;

namespace English_Listen_WinUI.Services
{
    /// <summary>
    /// 素材库持久化：{用户目录}/media_library.json（List&lt;MediaSource&gt;，内嵌句子）。
    /// 与 test_history.json 同目录、同读写模式。
    /// </summary>
    public class MediaLibraryService
    {
        private readonly SettingsService _settingsService;
        private readonly SemaphoreSlim _fileLock = new(1, 1);

        public MediaLibraryService(SettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        private string GetLibraryPath(string username)
        {
            return Path.Combine(_settingsService.GetUserDataPath(username), "media_library.json");
        }

        public async Task<List<MediaSource>> LoadAsync(string? username)
        {
            try
            {
                if (string.IsNullOrEmpty(username)) return new List<MediaSource>();
                var path = GetLibraryPath(username);
                if (!File.Exists(path)) return new List<MediaSource>();

                var json = await File.ReadAllTextAsync(path);
                return JsonSerializer.Deserialize<List<MediaSource>>(json) ?? new List<MediaSource>();
            }
            catch
            {
                return new List<MediaSource>();
            }
        }

        public async Task SaveAsync(string? username, List<MediaSource> library)
        {
            if (string.IsNullOrEmpty(username)) return;
            await _fileLock.WaitAsync();
            try
            {
                var dir = _settingsService.GetUserDataPath(username);
                Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(library, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(GetLibraryPath(username), json);
            }
            finally
            {
                _fileLock.Release();
            }
        }
    }
}
