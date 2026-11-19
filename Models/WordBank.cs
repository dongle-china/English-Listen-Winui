using System;
using System.Text.Json.Serialization;

namespace English_Listen_WinUI.Models
{
    /// <summary>
    /// 词库云端条目（KV word_banks，单个 key 存整个 List&lt;WordBank&gt;）。
    /// Name 即本地文件名，Content 为词库文本（inline 存储）。
    /// </summary>
    public class WordBank
    {
        public string Name { get; set; } = string.Empty;

        public long FileSize { get; set; }

        public int WordCount { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        public string Description { get; set; } = string.Empty;

        public string ContentHash { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        [JsonIgnore]
        public string WordCountText => WordCount + " 词";
    }
}
