using System;
using System.Threading;
using System.Threading.Tasks;

namespace English_Listen_WinUI.Services
{
    /// <summary>
    /// 静默去抖上云队列：数据落盘后延迟合并上传（读磁盘最新内容），
    /// 未登录跳过，失败静默，由云同步页"全部同步"兜底。
    /// </summary>
    public static class QuietCloudSync
    {
        private const int DebounceMs = 3000;

        private static readonly Gate SettingsGate = new();
        private static readonly Gate TranslationGate = new();
        private static readonly Gate WordGroupsGate = new();

        public static void QueueSettingsUpload() =>
            Queue(SettingsGate, () => new CloudSyncService().UploadSettingsFileAsync());

        public static void QueueTranslationUpload() =>
            Queue(TranslationGate, () => new CloudSyncService().UploadTranslationFileAsync());

        public static void QueueWordGroupsUpload() =>
            Queue(WordGroupsGate, () => new CloudSyncService().UploadWordGroupsFileAsync());

        private static void Queue(Gate gate, Func<Task> upload)
        {
            if (!WebApiService.Instance.IsLoggedIn) return;
            if (Interlocked.Exchange(ref gate.Pending, 1) == 1) return; // 已在排队，去抖合并

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(DebounceMs);
                    // 先放行再上传：上传期间的新写入会重新排队，上传本身读的是磁盘最新内容
                    Volatile.Write(ref gate.Pending, 0);
                    await upload();
                }
                catch
                {
                    // 可能撞上并发保存的原子写盘瞬间（文件短暂不存在），延迟重试一次
                    try
                    {
                        await Task.Delay(500);
                        await upload();
                    }
                    catch
                    {
                        // 静默：下次"全部同步"兜底
                    }
                }
            });
        }

        private sealed class Gate
        {
            public int Pending;
        }
    }
}
