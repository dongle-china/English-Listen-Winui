using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using English_Listen_WinUI.Models;
using NAudio.Wave;

namespace English_Listen_WinUI.Services
{
    /// <summary>
    /// 导入管线：扩展名校验 → NAudio 解码/重采样 16kHz 单声道 WAV → ITranscriptionService 转写 → 切句。
    /// 解码与转写均为 CPU 密集操作，调用方须放后台线程（Task.Run）并通过 IProgress 上报。
    /// </summary>
    public class MediaImportService
    {
        /// <summary>MediaFoundation 可解码的常见格式（系统内置解码器，离线可用）</summary>
        private static readonly string[] SupportedExtensions =
            { ".mp3", ".wav", ".mp4", ".m4a", ".wma", ".aac", ".flac" };

        private readonly ITranscriptionService _transcriptionService;

        public MediaImportService(ITranscriptionService transcriptionService)
        {
            _transcriptionService = transcriptionService;
        }

        public static bool IsSupportedFile(string path)
        {
            return SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
        }

        public static string SupportedExtensionsText => string.Join(" / ", SupportedExtensions);

        /// <summary>
        /// 就地转写并填充 media.Sentences。状态/错误写入 media.Status / media.ErrorMessage。
        /// </summary>
        public async Task<MediaSource> ImportAsync(
            MediaSource media,
            TranscriptionOptions options,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            if (!IsSupportedFile(media.FilePath))
            {
                media.Status = MediaStatus.Failed;
                media.ErrorMessage = $"不支持的文件格式（支持 {SupportedExtensionsText}）";
                return media;
            }

            var tempWav = Path.Combine(Path.GetTempPath(), $"elw_{media.Id}.wav");
            try
            {
                progress?.Report("正在解码音频...");
                await Task.Run(() => ConvertToWav16kMono(media.FilePath, tempWav), ct);

                var segments = await _transcriptionService.TranscribeAsync(tempWav, options, progress, ct);

                progress?.Report("正在切分句子...");
                media.Sentences = BuildSentences(media.Id, segments);
                media.Status = MediaStatus.Ready;
                media.ErrorMessage = null;
                progress?.Report($"导入完成：{media.Title}（{media.Sentences.Count} 句）");
            }
            catch (OperationCanceledException)
            {
                media.Status = MediaStatus.NeedsTranscribe;
                media.ErrorMessage = "导入已取消";
                throw;
            }
            catch (TranscriptionModelNotFoundException)
            {
                media.Status = MediaStatus.NeedsTranscribe;
                media.ErrorMessage = "未配置转写模型";
                throw;
            }
            catch (Exception ex)
            {
                media.Status = MediaStatus.Failed;
                media.ErrorMessage = $"导入失败：{ex.Message}";
                throw;
            }
            finally
            {
                try { if (File.Exists(tempWav)) File.Delete(tempWav); } catch { }
            }

            return media;
        }

        /// <summary>任意解码器 → MediaFoundationResampler → 16kHz/16bit/单声道 WAV（Whisper 输入格式）</summary>
        private static void ConvertToWav16kMono(string inputPath, string outputWavPath)
        {
            using var reader = new MediaFoundationReader(inputPath);
            var targetFormat = new WaveFormat(16000, 16, 1);
            using var resampler = new MediaFoundationResampler(reader, targetFormat)
            {
                ResamplerQuality = 60
            };
            using var writer = new WaveFileWriter(outputWavPath, targetFormat);
            var buffer = new byte[16384];
            int read;
            while ((read = resampler.Read(buffer, 0, buffer.Length)) > 0)
            {
                writer.Write(buffer, 0, read);
            }
        }

        /// <summary>
        /// 把转写分段合并切分为句子：句末标点 + 时长下限收割；时长/字数上限强制收割。
        /// </summary>
        internal static List<Sentence> BuildSentences(string mediaId, List<TranscriptionSegment> segments)
        {
            var sentences = new List<Sentence>();
            var hasCurrent = false;
            var startMs = 0.0;
            var endMs = 0.0;
            var text = string.Empty;

            foreach (var seg in segments)
            {
                if (string.IsNullOrWhiteSpace(seg.Text)) continue;
                if (!hasCurrent)
                {
                    startMs = seg.StartTimeMs;
                    text = string.Empty;
                    hasCurrent = true;
                }
                text = string.IsNullOrEmpty(text) ? seg.Text : $"{text} {seg.Text}";
                endMs = seg.EndTimeMs;

                var duration = endMs - startMs;
                if ((EndsSentence(text) && duration >= 1500) || duration > 15000 || text.Length > 300)
                {
                    AddSentence(sentences, mediaId, startMs, endMs, text);
                    hasCurrent = false;
                }
            }

            if (hasCurrent)
            {
                AddSentence(sentences, mediaId, startMs, endMs, text);
            }

            return sentences;
        }

        private static void AddSentence(List<Sentence> sentences, string mediaId, double startMs, double endMs, string text)
        {
            sentences.Add(new Sentence
            {
                MediaSourceId = mediaId,
                Index = sentences.Count,
                StartTimeMs = Math.Max(0, startMs - 100), // 句首留 100ms 起播余量
                EndTimeMs = endMs + 200,                  // 句尾留 200ms 收音余量
                TranscriptText = text
            });
        }

        private static bool EndsSentence(string text)
        {
            return text.Length > 0 && text[^1] is '.' or '!' or '?' or '。' or '！' or '？';
        }
    }
}
