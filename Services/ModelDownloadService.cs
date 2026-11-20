using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace English_Listen_WinUI.Services
{
    /// <summary>可下载的 Whisper ggml 模型定义</summary>
    public class WhisperModelInfo
    {
        public string FileName { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string SizeText { get; init; } = string.Empty;
    }

    /// <summary>下载进度（字节、总大小、速度、剩余时间）</summary>
    public class ModelDownloadProgress
    {
        public long BytesReceived { get; init; }
        public long TotalBytes { get; init; }
        public double SpeedBytesPerSec { get; init; }
        public TimeSpan Remaining { get; init; }
    }

    /// <summary>
    /// 从魔搭社区 (modelscope.cn) 下载 Whisper ggml 模型。
    /// 仓库：lihuoo/whisper.cpp-asr-model-collection（MIT License），已验证与 Whisper.net 1.9 兼容。
    /// </summary>
    public static class ModelDownloadService
    {
        private const string BaseUrl =
            "https://modelscope.cn/models/lihuoo/whisper.cpp-asr-model-collection/resolve/master/";

        /// <summary>标准多语言模型，按精度从低到高排列（下标与设置页下拉框一致）</summary>
        public static readonly WhisperModelInfo[] Models =
        {
            new() { FileName = "tiny.bin",   DisplayName = "tiny · 最快，精度较低",          SizeText = "78 MB"  },
            new() { FileName = "base.bin",   DisplayName = "base · 速度与精度均衡（推荐）",  SizeText = "148 MB" },
            new() { FileName = "small.bin",  DisplayName = "small · 更高精度",               SizeText = "488 MB" },
            new() { FileName = "medium.bin", DisplayName = "medium · 最高精度，速度较慢",    SizeText = "1.5 GB" },
        };

        public static async Task DownloadAsync(
            WhisperModelInfo model,
            string destPath,
            IProgress<ModelDownloadProgress> progress,
            CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            var tmpPath = destPath + ".downloading";

            using var http = new HttpClient { Timeout = TimeSpan.FromHours(1) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("English-Listen-WinUI");

            try
            {
                using var resp = await http.GetAsync(
                    BaseUrl + model.FileName, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? 0;

                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(
                    tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);

                var sw = Stopwatch.StartNew();
                long received = 0;
                var lastReport = -1.0; // 上次上报时间（秒），首次读数据即上报
                var buf = new byte[1 << 16];
                int n;
                while ((n = await src.ReadAsync(buf.AsMemory(0, buf.Length), ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    received += n;

                    var elapsed = sw.Elapsed.TotalSeconds;
                    if (elapsed - lastReport >= 0.3)
                    {
                        lastReport = elapsed;
                        progress.Report(BuildProgress(received, total, elapsed));
                    }
                }
                progress.Report(BuildProgress(received, total, sw.Elapsed.TotalSeconds));

                // 校验 ggml 魔数（"lmgg"），防止把错误页/损坏文件当作模型
                using (var check = File.OpenRead(tmpPath))
                {
                    var magic = new byte[4];
                    await check.ReadExactlyAsync(magic, ct);
                    if (Encoding.ASCII.GetString(magic) != "lmgg")
                    {
                        throw new InvalidDataException("下载的文件不是有效的 Whisper ggml 模型，请重试。");
                    }
                }

                File.Move(tmpPath, destPath, overwrite: true);
            }
            catch
            {
                try { File.Delete(tmpPath); } catch { /* 清理失败可忽略 */ }
                throw;
            }
        }

        private static ModelDownloadProgress BuildProgress(long received, long total, double elapsedSeconds)
        {
            var speed = elapsedSeconds > 0 ? received / elapsedSeconds : 0;
            var remaining = speed > 0 && total > received
                ? TimeSpan.FromSeconds((total - received) / speed)
                : TimeSpan.Zero;
            return new ModelDownloadProgress
            {
                BytesReceived = received,
                TotalBytes = total,
                SpeedBytesPerSec = speed,
                Remaining = remaining,
            };
        }
    }
}
