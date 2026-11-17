using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace English_Listen_WinUI.Services
{
    /// <summary>带时间戳的转写分段</summary>
    public class TranscriptionSegment
    {
        public double StartTimeMs { get; set; }
        public double EndTimeMs { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    public class TranscriptionOptions
    {
        /// <summary>Whisper 语言代码，"auto" 自动检测</summary>
        public string Language { get; set; } = "auto";
        /// <summary>ggml 模型文件路径</summary>
        public string? ModelPath { get; set; }
    }

    /// <summary>模型缺失时抛出，UI 层据此展示引导而非崩溃。</summary>
    public class TranscriptionModelNotFoundException : Exception
    {
        public TranscriptionModelNotFoundException(string message) : base(message) { }
    }

    /// <summary>
    /// 本地转写服务接口。云 ASR 等可插拔增强通过实现本接口接入。
    /// </summary>
    public interface ITranscriptionService
    {
        /// <summary>模型文件是否可用</summary>
        bool IsModelAvailable(string? modelPath);

        /// <summary>
        /// 转写 16kHz/16bit/单声道 WAV 文件，返回带时间戳分段。
        /// 模型缺失抛 <see cref="TranscriptionModelNotFoundException"/>。
        /// 必须在后台线程调用，不得阻塞 UI。
        /// </summary>
        Task<List<TranscriptionSegment>> TranscribeAsync(
            string wav16kMonoPath,
            TranscriptionOptions options,
            IProgress<string>? progress,
            CancellationToken ct);
    }
}
