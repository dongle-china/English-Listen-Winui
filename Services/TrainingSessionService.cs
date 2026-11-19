using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using English_Listen_WinUI.Models;

namespace English_Listen_WinUI.Services
{
    /// <summary>
    /// 训练会话状态机 + 规则引擎：泛听 → 精听 → 复习。
    /// 会话持久化于 {用户目录}/training_sessions.json（一个素材一条会话记录）。
    /// 不依赖 LLM，掌握判定/复习队列全部为规则判定。
    /// </summary>
    public class TrainingSessionService
    {
        /// <summary>复习阶段连续全对 N 次判“已掌握”</summary>
        public const int MasteryConsecutiveCorrect = 2;

        /// <summary>复习队列纳入阈值：精听错误率（含）超过此值的句子进入复习</summary>
        public const double ReviewErrorRateThreshold = 0;

        private readonly SettingsService _settingsService;
        private readonly SemaphoreSlim _fileLock = new(1, 1);
        private Dictionary<string, TrainingSession> _sessions = new();

        public TrainingSessionService(SettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        /// <summary>已加载的全部会话（统计页用）。</summary>
        public IReadOnlyCollection<TrainingSession> Sessions => _sessions.Values;

        private string GetPath(string username) =>
            Path.Combine(_settingsService.GetUserDataPath(username), "training_sessions.json");

        public async Task LoadAsync(string? username)
        {
            try
            {
                if (string.IsNullOrEmpty(username))
                {
                    _sessions = new Dictionary<string, TrainingSession>();
                    return;
                }

                var path = GetPath(username);
                if (!File.Exists(path))
                {
                    _sessions = new Dictionary<string, TrainingSession>();
                    return;
                }

                var json = await File.ReadAllTextAsync(path);
                var list = JsonSerializer.Deserialize<List<TrainingSession>>(json) ?? new List<TrainingSession>();
                _sessions = list.ToDictionary(s => s.MediaSourceId);
            }
            catch
            {
                _sessions = new Dictionary<string, TrainingSession>();
            }
        }

        public async Task SaveAsync(string? username)
        {
            if (string.IsNullOrEmpty(username)) return;
            await _fileLock.WaitAsync();
            try
            {
                var dir = _settingsService.GetUserDataPath(username);
                Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(_sessions.Values.ToList(),
                    new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(GetPath(username), json);
            }
            finally
            {
                _fileLock.Release();
            }
        }

        /// <summary>取会话；无则新建（默认泛听）。素材重新转写导致句子增删时补齐进度。</summary>
        public TrainingSession GetOrCreate(MediaSource media)
        {
            if (_sessions.TryGetValue(media.Id, out var session))
            {
                var known = session.Progress.Select(p => p.SentenceIndex).ToHashSet();
                foreach (var s in media.Sentences)
                {
                    if (!known.Contains(s.Index))
                        session.Progress.Add(new SentenceProgress { SentenceIndex = s.Index });
                }
                return session;
            }

            session = new TrainingSession
            {
                MediaSourceId = media.Id,
                Title = media.Title,
                Progress = media.Sentences
                    .Select(s => new SentenceProgress { SentenceIndex = s.Index })
                    .ToList()
            };
            _sessions[media.Id] = session;
            return session;
        }

        public SentenceProgress GetProgress(TrainingSession session, int sentenceIndex)
        {
            var p = session.Progress.FirstOrDefault(x => x.SentenceIndex == sentenceIndex);
            if (p == null)
            {
                p = new SentenceProgress { SentenceIndex = sentenceIndex };
                session.Progress.Add(p);
            }
            return p;
        }

        /// <summary>精听提交：记录尝试/错误/提示，更新最近错误率。</summary>
        public void RecordIntensiveAttempt(TrainingSession session, int sentenceIndex,
            int totalWords, int correctWords, int hintCount)
        {
            var p = GetProgress(session, sentenceIndex);
            p.Attempts++;
            p.ErrorCount += totalWords - correctWords;
            p.HintCount += hintCount;
            p.LastErrorRate = totalWords == 0 ? 0 : (double)(totalWords - correctWords) / totalWords;
            session.UpdatedAt = DateTime.Now;
        }

        /// <summary>
        /// 复习提交：全对则连对计数 +1，达 <see cref="MasteryConsecutiveCorrect"/> 判掌握。
        /// 返回 true 表示本次提交新达成“已掌握”。
        /// </summary>
        public bool RecordReviewAttempt(TrainingSession session, int sentenceIndex,
            int totalWords, int correctWords, int hintCount)
        {
            var p = GetProgress(session, sentenceIndex);
            p.Attempts++;
            p.HintCount += hintCount;
            var allCorrect = totalWords > 0 && correctWords == totalWords;
            p.ConsecutiveCorrect = allCorrect ? p.ConsecutiveCorrect + 1 : 0;
            if (!allCorrect)
            {
                p.ErrorCount += totalWords - correctWords;
                p.LastErrorRate = totalWords == 0 ? 0 : (double)(totalWords - correctWords) / totalWords;
            }
            session.UpdatedAt = DateTime.Now;

            if (allCorrect && !p.IsMastered && p.ConsecutiveCorrect >= MasteryConsecutiveCorrect)
            {
                p.IsMastered = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 复习队列：精听提交过、且（错误率超阈值 或 被标记难点）、且未掌握的句子序号，按素材顺序。
        /// </summary>
        public List<int> BuildReviewQueue(TrainingSession session, MediaSource media)
        {
            return media.Sentences
                .Select(s => s.Index)
                .Where(i =>
                {
                    var p = GetProgress(session, i);
                    if (p.IsMastered || p.Attempts == 0) return false;
                    return p.LastErrorRate > ReviewErrorRateThreshold || p.IsMarkedDifficult;
                })
                .ToList();
        }

        /// <summary>泛听标记/取消难点句。</summary>
        public void ToggleDifficultMark(TrainingSession session, int sentenceIndex, bool marked)
        {
            GetProgress(session, sentenceIndex).IsMarkedDifficult = marked;
            session.UpdatedAt = DateTime.Now;
        }

        /// <summary>分级提示文本：1=首字母，2=首字母+长度点占位（音节/长度提示），3=整词。</summary>
        public static string GetHint(string word, int level)
        {
            if (string.IsNullOrEmpty(word)) return string.Empty;
            return level switch
            {
                1 => word[0] + "…",
                2 => word[0] + new string('·', Math.Min(word.Length - 1, 15)),
                _ => word
            };
        }
    }
}
