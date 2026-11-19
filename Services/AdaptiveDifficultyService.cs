using System;
using System.Diagnostics;
using English_Listen_WinUI.Models;

namespace English_Listen_WinUI.Services
{
    /// <summary>一次精听/复习提交的上下文（挣扎/干净判定用）</summary>
    public class AttemptContext
    {
        public int TotalWords { get; set; }
        public int CorrectWords { get; set; }
        public int HintCount { get; set; }
        /// <summary>本句提交前的重播次数</summary>
        public int ReplayCount { get; set; }
    }

    /// <summary>规则引擎输出：应用后的难度参数快照 + 可解释中文说明</summary>
    public class AdaptiveDecision
    {
        public double PlaybackRate { get; set; }
        public bool ShowKeywordsAhead { get; set; }
        public int AutoHintLevel { get; set; }
        public int LoopGapMs { get; set; }
        /// <summary>本次调整说明；空 = 本次未调整</summary>
        public string Explanation { get; set; } = string.Empty;
    }

    /// <summary>
    /// 自适应难度规则引擎：纯本地、可解释、无 LLM。
    /// 挣扎（错误率&gt;0.5，或有错且 提示+重播≥3）连续 2 次 → 降档；
    /// 干净（全对且无提示）连续 3 次 → 升档；语速达 1.0 时撤除全部辅助。
    /// </summary>
    public class AdaptiveDifficultyService
    {
        public const double MinRate = 0.7;
        public const double MaxRate = 1.0;
        public const double RateStep = 0.1;
        public const int StruggleThreshold = 2;
        public const int CleanStreakThreshold = 3;
        public const double StruggleErrorRate = 0.5;
        public const int StruggleAssistBudget = 3;
        public const int StruggleLoopGapMs = 600;

        public AdaptiveDifficultyService()
        {
            SelfCheck();
        }

        /// <summary>评估一次提交，就地更新自适应状态，返回应用后的参数快照。</summary>
        public AdaptiveDecision Evaluate(AdaptiveState state, AttemptContext ctx) => EvaluateCore(state, ctx);

        private static AdaptiveDecision EvaluateCore(AdaptiveState state, AttemptContext ctx)
        {
            var errorRate = ctx.TotalWords == 0 ? 0 : (double)(ctx.TotalWords - ctx.CorrectWords) / ctx.TotalWords;
            var hasError = ctx.CorrectWords < ctx.TotalWords;
            var struggle = errorRate > StruggleErrorRate
                || (hasError && ctx.HintCount + ctx.ReplayCount >= StruggleAssistBudget);
            var clean = errorRate == 0 && ctx.HintCount == 0;

            if (struggle)
            {
                state.ConsecutiveStruggle++;
                state.ConsecutiveClean = 0;
            }
            else if (clean)
            {
                state.ConsecutiveClean++;
                state.ConsecutiveStruggle = 0;
            }
            else
            {
                // 中间态：两种连续都被打断
                state.ConsecutiveStruggle = 0;
                state.ConsecutiveClean = 0;
            }

            if (state.ConsecutiveStruggle >= StruggleThreshold)
            {
                state.ConsecutiveStruggle = 0;
                state.PlaybackRate = Math.Max(MinRate, state.PlaybackRate - RateStep);
                state.ShowKeywordsAhead = true;
                state.AutoHintLevel = 1;
                state.LoopGapMs = StruggleLoopGapMs;
                return Snapshot(state, state.PlaybackRate > MinRate
                    ? $"连续{StruggleThreshold}次听写吃力：语速降至 {state.PlaybackRate:F1}x，已开启首字母提示"
                    : $"连续{StruggleThreshold}次听写吃力：语速已至最低 {MinRate:F1}x，已开启首字母提示");
            }

            if (state.ConsecutiveClean >= CleanStreakThreshold)
            {
                state.ConsecutiveClean = 0;
                state.PlaybackRate = Math.Min(MaxRate, state.PlaybackRate + RateStep);
                if (state.PlaybackRate >= MaxRate)
                {
                    state.ShowKeywordsAhead = false;
                    state.AutoHintLevel = 0;
                    state.LoopGapMs = 0;
                    return Snapshot(state, $"连续{CleanStreakThreshold}次全对无提示：语速恢复 1.0x，已撤除全部辅助");
                }
                return Snapshot(state, $"连续{CleanStreakThreshold}次全对无提示：语速提升至 {state.PlaybackRate:F1}x");
            }

            return Snapshot(state, string.Empty);
        }

        private static AdaptiveDecision Snapshot(AdaptiveState state, string explanation) => new()
        {
            PlaybackRate = state.PlaybackRate,
            ShowKeywordsAhead = state.ShowKeywordsAhead,
            AutoHintLevel = state.AutoHintLevel,
            LoopGapMs = state.LoopGapMs,
            Explanation = explanation
        };

        /// <summary>规则引擎自检（仅 DEBUG 生效）。</summary>
        [Conditional("DEBUG")]
        private static void SelfCheck()
        {
            // 用例1：连续两次挣扎 → 降速到 0.9 并开辅助
            var s = new AdaptiveState();
            var struggle = new AttemptContext { TotalWords = 10, CorrectWords = 3, HintCount = 0, ReplayCount = 0 };
            EvaluateCore(s, struggle);
            var d = EvaluateCore(s, struggle);
            Debug.Assert(Math.Abs(d.PlaybackRate - 0.9) < 1e-9 && d.ShowKeywordsAhead && d.AutoHintLevel == 1
                && d.LoopGapMs == StruggleLoopGapMs && s.ConsecutiveStruggle == 0, "两次挣扎应降档并开辅助");

            // 用例2：从 0.9 连续三次全对无提示 → 恢复 1.0 并撤辅助
            var clean = new AttemptContext { TotalWords = 10, CorrectWords = 10, HintCount = 0, ReplayCount = 2 };
            EvaluateCore(s, clean);
            EvaluateCore(s, clean);
            d = EvaluateCore(s, clean);
            Debug.Assert(Math.Abs(d.PlaybackRate - 1.0) < 1e-9 && !d.ShowKeywordsAhead && d.AutoHintLevel == 0
                && d.LoopGapMs == 0 && s.ConsecutiveClean == 0, "三次干净应恢复 1.0 并撤辅助");

            // 用例3：中间态（少量错、未达挣扎）→ 两种连续清零、参数不动
            var mid = new AttemptContext { TotalWords = 10, CorrectWords = 9, HintCount = 0, ReplayCount = 0 };
            EvaluateCore(s, mid);
            EvaluateCore(s, new AttemptContext { TotalWords = 10, CorrectWords = 10, HintCount = 0, ReplayCount = 0 });
            d = EvaluateCore(s, mid);
            Debug.Assert(s.ConsecutiveStruggle == 0 && s.ConsecutiveClean == 0
                && Math.Abs(d.PlaybackRate - 1.0) < 1e-9 && d.Explanation.Length == 0, "中间态应打断连续且不调参");

            // 用例4：语速下限保护
            s = new AdaptiveState { PlaybackRate = MinRate };
            EvaluateCore(s, struggle);
            d = EvaluateCore(s, struggle);
            Debug.Assert(Math.Abs(d.PlaybackRate - MinRate) < 1e-9, "降速不得低于下限 0.7");

            // 用例5：打标器——空输入虚词=弱读、空输入实词=词汇、连读、编辑距离1=音素、错≥2次=附加词汇
            var tags = WordErrorTagger.Tag(new SentenceWordAttempt { Expected = "the", Input = "" }, null, 1);
            Debug.Assert(tags.Count == 1 && tags[0] == ErrorTagKind.WeakForm, "空输入虚词应标弱读");
            tags = WordErrorTagger.Tag(new SentenceWordAttempt { Expected = "because", Input = "" }, null, 1);
            Debug.Assert(tags.Count == 1 && tags[0] == ErrorTagKind.Vocabulary, "空输入实词应标词汇不熟");
            tags = WordErrorTagger.Tag(new SentenceWordAttempt { Expected = "an", Input = "anapple" }, "apple", 1);
            Debug.Assert(tags.Count == 1 && tags[0] == ErrorTagKind.Liaison, "输入吞并下词应标连读");
            tags = WordErrorTagger.Tag(new SentenceWordAttempt { Expected = "think", Input = "tink" }, null, 1);
            Debug.Assert(tags.Count == 1 && tags[0] == ErrorTagKind.Phonetic, "编辑距离1应标音素");
            tags = WordErrorTagger.Tag(new SentenceWordAttempt { Expected = "think", Input = "tink" }, null, 2);
            Debug.Assert(tags.Count == 2 && tags.Contains(ErrorTagKind.Phonetic) && tags.Contains(ErrorTagKind.Vocabulary),
                "累计错≥2次应附加词汇不熟");
            tags = WordErrorTagger.Tag(new SentenceWordAttempt { Expected = "beautiful", Input = "but" }, null, 1);
            Debug.Assert(tags.Count == 1 && tags[0] == ErrorTagKind.Vocabulary, "编辑距离≥3应标词汇不熟");
        }
    }
}
