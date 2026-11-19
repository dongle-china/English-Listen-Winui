using System;
using System.Collections.Generic;

namespace English_Listen_WinUI.Models
{
    /// <summary>训练阶段：泛听 → 精听 → 复习。</summary>
    public enum TrainingStage
    {
        /// <summary>泛听：整段连续播放，不要求逐词输入</summary>
        Extensive,
        /// <summary>精听：逐句播放，逐词输入校验</summary>
        Intensive,
        /// <summary>复习：只练未掌握句子，连续正确达标判掌握</summary>
        Review
    }

    /// <summary>单句训练进度：精听尝试/错误/提示与复习掌握状态。</summary>
    public class SentenceProgress
    {
        /// <summary>句子切句序号（Sentence.Index）</summary>
        public int SentenceIndex { get; set; }

        /// <summary>精听/复习提交次数</summary>
        public int Attempts { get; set; }

        /// <summary>累计错误词数（每次提交的错误词数累加）</summary>
        public int ErrorCount { get; set; }

        /// <summary>最近一次提交的错误率（0~1），复习队列阈值判定用</summary>
        public double LastErrorRate { get; set; }

        /// <summary>提示使用总次数</summary>
        public int HintCount { get; set; }

        /// <summary>复习阶段连续全对次数</summary>
        public int ConsecutiveCorrect { get; set; }

        /// <summary>已掌握（复习连续正确达阈值）</summary>
        public bool IsMastered { get; set; }

        /// <summary>泛听阶段用户标记的难点句</summary>
        public bool IsMarkedDifficult { get; set; }
    }

    /// <summary>
    /// 素材训练会话：三阶段状态与每句进度。
    /// 持久化于 {用户目录}/training_sessions.json（List&lt;TrainingSession&gt;，按 MediaSourceId 一条）。
    /// </summary>
    public class TrainingSession
    {
        public string MediaSourceId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public TrainingStage Stage { get; set; } = TrainingStage.Extensive;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>泛听已完整播放的句子数（完成度分子）</summary>
        public int ExtensivePlayedCount { get; set; }

        public List<SentenceProgress> Progress { get; set; } = new();
    }
}
