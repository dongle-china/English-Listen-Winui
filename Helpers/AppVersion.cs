using System;
using Windows.ApplicationModel;

namespace English_Listen_WinUI.Helpers
{
    /// <summary>
    /// 应用版本号统一入口。打包运行取 Package.appxmanifest；
    /// 非打包调试运行（无包标识，Package.Current 抛 0x80073D54）回退程序集版本。
    /// </summary>
    public static class AppVersion
    {
        public static string Current
        {
            get
            {
                try
                {
                    var v = Package.Current.Id.Version;
                    return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
                }
                catch (InvalidOperationException)
                {
                    var asm = typeof(AppVersion).Assembly.GetName().Version;
                    return $"{asm?.Major ?? 0}.{asm?.Minor ?? 0}.{asm?.Build ?? 0} (unpackaged)";
                }
            }
        }
    }
}
