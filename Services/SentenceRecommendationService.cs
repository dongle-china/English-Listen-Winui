using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using English_Listen_WinUI.Models;

namespace English_Listen_WinUI.Services
{
    /// <summary>一条句子推荐（含命中原因，可解释）</summary>
    public class RecommendedSentence
    {
        public string MediaSourceId { get; set; } = string.Empty;
        public string MediaTitle { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        /// <summary>句子切句序号（Sentence.Index）</summary>
        public int SentenceIndex { get; set; }
        /// <summary>在素材句列表中的位置（0 起，用于跳转定位）</summary>
        public int ListPosition { get; set; }
        public string Text { get; set; } = string.Empty;
        /// <summary>命中原因（中文，已拼接）</summary>
        public string Reason { get; set; } = string.Empty;
        public int Score { get; set; }
    }

    /// <summary>
    /// 句子推荐：基于错误画像的加权评分（3×高频错词命中 + 2×薄弱标签命中 + 1×话题相似），
    /// 排除已掌握句。纯本地规则，无 LLM。
    /// </summary>
    public class SentenceRecommendationService
    {
        public const int FrequentWordScore = 3;
        public const int WeakTagScore = 2;
        public const int TopicScore = 1;
        /// <summary>话题相似判定：与最近错句词集的 Dice 重叠超过此值</summary>
        public const double TopicDiceThreshold = 0.3;
        public const int MaxResults = 10;

        private readonly SettingsService _settingsService;

        public SentenceRecommendationService(SettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        /// <summary>生成推荐句列表（按评分降序，最多 MaxResults 条；无命中返回空列表）。</summary>
        public async Task<List<RecommendedSentence>> RecommendAsync(string? username)
        {
            var mediaService = new MediaLibraryService(_settingsService);
            var patternService = new ErrorPatternService(_settingsService, mediaService);
            var sessionService = new TrainingSessionService(_settingsService);
            await sessionService.LoadAsync(username);

            // 已掌握句排除表
            var mastered = sessionService.Sessions
                .SelectMany(s => s.Progress.Where(p => p.IsMastered)
                    .Select(p => (s.MediaSourceId, p.SentenceIndex)))
                .ToHashSet();

            var profile = await patternService.BuildProfileAsync(username);
            var frequent = profile.FrequentWrongWords.ToHashSet();
            var weakTags = profile.WeakTags;

            var library = await mediaService.LoadAsync(username);
            var results = new List<RecommendedSentence>();

            foreach (var media in library.Where(m => m.Status == MediaStatus.Ready))
            {
                for (var pos = 0; pos < media.Sentences.Count; pos++)
                {
                    var s = media.Sentences[pos];
                    if (mastered.Contains((media.Id, s.Index))) continue;
                    var words = SplitWords(s.TranscriptText);
                    if (words.Count == 0) continue;

                    var score = 0;
                    var reasons = new List<string>();

                    // 3× 高频错词命中
                    var hits = words.Where(frequent.Contains).Distinct().ToList();
                    if (hits.Count > 0)
                    {
                        score += FrequentWordScore * hits.Count;
                        reasons.Add($"含高频错词 {string.Join("、", hits.Take(3))}");
                    }

                    // 2× 薄弱标签命中（仅当用户存在该薄弱标签）
                    if (weakTags.Contains(ErrorTagKind.Speed) && ErrorPatternService.IsFastSentence(s, words.Count))
                    {
                        score += WeakTagScore;
                        reasons.Add("语速快");
                    }
                    if (weakTags.Contains(ErrorTagKind.WeakForm)
                        && words.Where(WordErrorTagger.IsWeakForm).Distinct().Count() >= 2)
                    {
                        score += WeakTagScore;
                        reasons.Add("含多个弱读虚词");
                    }

                    // 1× 话题相似：与任一最近错句词集 Dice 重叠超阈值
                    if (profile.RecentErrorSentenceWords.Count > 0)
                    {
                        var wordSet = words.ToHashSet();
                        if (profile.RecentErrorSentenceWords.Any(prev => DiceOverlap(wordSet, prev) > TopicDiceThreshold))
                        {
                            score += TopicScore;
                            reasons.Add("与你最近出错的句子话题相近");
                        }
                    }

                    if (score > 0)
                    {
                        results.Add(new RecommendedSentence
                        {
                            MediaSourceId = media.Id,
                            MediaTitle = media.Title,
                            FilePath = media.FilePath,
                            SentenceIndex = s.Index,
                            ListPosition = pos,
                            Text = s.TranscriptText,
                            Reason = string.Join("；", reasons),
                            Score = score
                        });
                    }
                }
            }

            return results.OrderByDescending(r => r.Score).Take(MaxResults).ToList();
        }

        private static List<string> SplitWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            return text.ToLowerInvariant()
                .Split(new[] { ' ', ',', '.', '?', '!', ';', ':', '"', '\'', '(', ')', '[', ']', '{', '}', '-', '\n', '\r', '\t' },
                    StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

        private static double DiceOverlap(HashSet<string> a, HashSet<string> b)
        {
            if (a.Count == 0 || b.Count == 0) return 0;
            return 2.0 * a.Intersect(b).Count() / (a.Count + b.Count);
        }
    }
}
