using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using English_Listen_WinUI.Models;

namespace English_Listen_WinUI.Services
{
    /// <summary>
    /// 错误画像服务：从 test_history.json（RecordType="sentence"）+ media_library.json
    /// 实时派生用户错误标签分布、高频错词与最近错句词集。纯读取，标签不落盘。
    /// </summary>
    public class ErrorPatternService
    {
        /// <summary>句级 Speed 标签阈值：词密度超过此值（词/秒）视为语速快</summary>
        public const double FastSentenceDensity = 2.5;

        /// <summary>高频错词的最低累计错误次数</summary>
        public const int FrequentWordThreshold = 2;

        /// <summary>“最近错句”回溯条数（推荐话题相似度用）</summary>
        public const int RecentErrorSentences = 50;

        private readonly SettingsService _settingsService;
        private readonly MediaLibraryService _mediaLibraryService;

        public ErrorPatternService(SettingsService settingsService, MediaLibraryService mediaLibraryService)
        {
            _settingsService = settingsService;
            _mediaLibraryService = mediaLibraryService;
        }

        /// <summary>构建用户错误画像。无句子训练历史时返回空画像。</summary>
        public async Task<ErrorProfile> BuildProfileAsync(string? username)
        {
            var history = await _settingsService.LoadTestHistoryAsync(username ?? "");
            var records = history
                .Where(r => r.RecordType == "sentence" && r.SentenceWords is { Count: > 0 })
                .OrderByDescending(r => r.Timestamp)
                .ToList();

            var profile = new ErrorProfile();
            if (records.Count == 0) return profile;

            // 句表（查句时长，Speed 标签用）
            var library = await _mediaLibraryService.LoadAsync(username);
            var sentences = library
                .SelectMany(m => m.Sentences)
                .GroupBy(s => s.MediaSourceId + "#" + s.Index)
                .ToDictionary(g => g.Key, g => g.First());

            // pass1：全历史规范化错词计数
            var wrongCounts = new Dictionary<string, int>();
            foreach (var r in records)
            {
                foreach (var w in r.SentenceWords!)
                {
                    if (w.Correct) continue;
                    profile.TotalWordErrors++;
                    var key = WordErrorTagger.Normalize(w.Expected);
                    if (key.Length > 0)
                        wrongCounts[key] = wrongCounts.GetValueOrDefault(key) + 1;
                }
            }

            // pass2：逐错词打标聚合 + 句级 Speed + 最近错句词集
            var tagCounts = new Dictionary<ErrorTagKind, int>();
            foreach (var r in records)
            {
                var words = r.SentenceWords!;
                var hasError = false;
                for (var i = 0; i < words.Count; i++)
                {
                    if (words[i].Correct) continue;
                    hasError = true;
                    var key = WordErrorTagger.Normalize(words[i].Expected);
                    var nextExpected = i + 1 < words.Count ? words[i + 1].Expected : null;
                    foreach (var tag in WordErrorTagger.Tag(words[i], nextExpected, wrongCounts.GetValueOrDefault(key)))
                        tagCounts[tag] = tagCounts.GetValueOrDefault(tag) + 1;
                }

                // 句级 Speed：本次出错的句子词密度过高
                if (hasError && r.MediaSourceId != null && r.SentenceIndex.HasValue
                    && sentences.TryGetValue(r.MediaSourceId + "#" + r.SentenceIndex.Value, out var s)
                    && IsFastSentence(s, words.Count))
                {
                    tagCounts[ErrorTagKind.Speed] = tagCounts.GetValueOrDefault(ErrorTagKind.Speed) + 1;
                }

                if (hasError && profile.RecentErrorSentenceWords.Count < RecentErrorSentences)
                {
                    profile.RecentErrorSentenceWords.Add(words
                        .Select(w => WordErrorTagger.Normalize(w.Expected))
                        .Where(x => x.Length > 0)
                        .ToHashSet());
                }
            }

            profile.TagStats = tagCounts
                .Select(kv => new ErrorTagStats { Tag = kv.Key, Count = kv.Value })
                .OrderByDescending(t => t.Count)
                .ToList();
            profile.FrequentWrongWords = wrongCounts
                .Where(kv => kv.Value >= FrequentWordThreshold)
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(20)
                .Select(kv => kv.Key)
                .ToList();
            return profile;
        }

        /// <summary>句子词密度是否超过语速快阈值（词/秒）。</summary>
        public static bool IsFastSentence(Sentence s, int wordCount)
        {
            var seconds = (s.EndTimeMs - s.StartTimeMs) / 1000.0;
            return seconds > 0 && wordCount / seconds > FastSentenceDensity;
        }
    }
}
