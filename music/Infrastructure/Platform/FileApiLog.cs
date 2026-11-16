using System;
using System.IO;
using Windows.Storage;
using music.Core.Abstractions;

namespace music.Infrastructure.Platform
{
    public sealed class FileApiLog : IApiLog
    {
        public void Write(string message)
        {
            try
            {
                var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "api_debug.log");
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}\r\n");
                System.Diagnostics.Debug.WriteLine($"[API] {message}");
            }
            catch
            {
                // 日志失败不影响主流程
            }
        }
    }
}
