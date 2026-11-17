using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;

namespace English_Listen_WinUI.Services
{
    /// <summary>
    /// 基于 Whisper.net (whisper.cpp) 的本地转写实现。模型由用户自备（ggml .bin），不随应用分发。
    /// </summary>
    public class WhisperNetTranscriptionService : ITranscriptionService
    {
        public bool IsModelAvailable(string? modelPath)
        {
            return !string.IsNullOrWhiteSpace(modelPath) && File.Exists(modelPath);
        }

        public async Task<List<TranscriptionSegment>> TranscribeAsync(
            string wav16kMonoPath,
            TranscriptionOptions options,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            if (!IsModelAvailable(options.ModelPath))
            {
                throw new TranscriptionModelNotFoundException(
                    "未找到转写模型文件。请在素材库页面点击\"选择模型\"指定 Whisper ggml 模型" +
                    "（如 ggml-tiny.bin / ggml-base.bin，可从 whisper.cpp 项目 models 目录说明中下载）。");
            }

            var results = new List<TranscriptionSegment>();
            progress?.Report("正在加载转写模型...");

            using var factory = WhisperFactory.FromPath(options.ModelPath!);
            using var processor = factory.CreateBuilder()
                .WithLanguage(string.IsNullOrWhiteSpace(options.Language) ? "auto" : options.Language)
                .Build();

            progress?.Report("正在转写音频（可能需要几分钟）...");

            await using var fileStream = File.OpenRead(wav16kMonoPath);
            await foreach (var segment in processor.ProcessAsync(fileStream, ct))
            {
                ct.ThrowIfCancellationRequested();
                var text = segment.Text?.Trim() ?? string.Empty;
                if (text.Length == 0) continue;

                results.Add(new TranscriptionSegment
                {
                    StartTimeMs = segment.Start.TotalMilliseconds,
                    EndTimeMs = segment.End.TotalMilliseconds,
                    Text = text
                });
            }

            progress?.Report($"转写完成，共 {results.Count} 段");
            return results;
        }
    }
}
