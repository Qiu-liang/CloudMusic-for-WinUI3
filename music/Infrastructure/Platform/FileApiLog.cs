using System;
using System.IO;
using Windows.Storage;
using music.Core.Abstractions;
using music.Core.Services;

namespace music.Infrastructure.Platform
{
    public sealed class FileApiLog : IApiLog, IDisposable
    {
        private readonly AsyncRotatingFileLog? _inner;

        public FileApiLog()
        {
            try
            {
                var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "api_debug.log");
                _inner = new AsyncRotatingFileLog(path);
            }
            catch
            {
                // 日志初始化失败不影响主流程
            }
        }

        public void Write(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[API] {message}");
            _inner?.Write(message);
        }

        public void Dispose() => _inner?.Dispose();
    }
}
