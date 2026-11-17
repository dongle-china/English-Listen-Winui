using System;
using System.Collections.Generic;

namespace English_Listen_WinUI.Models
{
    public enum MediaSourceType
    {
        LocalFile,
        Link
    }

    public enum MediaStatus
    {
        /// <summary>已导入待转写（或转写被取消/模型缺失）</summary>
        NeedsTranscribe,
        /// <summary>转写中</summary>
        Transcribing,
        /// <summary>就绪，可开始精听</summary>
        Ready,
        /// <summary>导入/转写失败</summary>
        Failed
    }

    /// <summary>
    /// 精听素材：一个本地音视频文件及其转写结果。
    /// 持久化于 {用户目录}/media_library.json。
    /// </summary>
    public class MediaSource
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = string.Empty;
        public MediaSourceType SourceType { get; set; } = MediaSourceType.LocalFile;
        /// <summary>本地媒体文件绝对路径（Link 类型为下载后的本地路径）</summary>
        public string FilePath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public MediaStatus Status { get; set; } = MediaStatus.NeedsTranscribe;
        public string? ErrorMessage { get; set; }
        public List<Sentence> Sentences { get; set; } = new();
    }

    /// <summary>
    /// 切分后的单句，包含媒体源 ID、起止时间、原文（字幕/歌词，可为空）、转写文本与切句序号。
    /// </summary>
    public class Sentence
    {
        public string MediaSourceId { get; set; } = string.Empty;
        public int Index { get; set; }
        public double StartTimeMs { get; set; }
        public double EndTimeMs { get; set; }
        /// <summary>用户提供的原文（字幕/歌词），暂可为空</summary>
        public string OriginalText { get; set; } = string.Empty;
        /// <summary>Whisper 转写文本</summary>
        public string TranscriptText { get; set; } = string.Empty;
    }

    /// <summary>逐词校验结果，随 TestResult (RecordType="sentence") 写入 test_history.json</summary>
    public class SentenceWordAttempt
    {
        public string Expected { get; set; } = string.Empty;
        public string Input { get; set; } = string.Empty;
        public bool Correct { get; set; }
    }
}
