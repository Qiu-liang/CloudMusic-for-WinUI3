using System;
using System.IO;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using music.Core.Abstractions;

namespace music.Core.Services
{
    /// <summary>
    /// Non-blocking bounded-channel file logger with size-based single-backup rotation.
    /// </summary>
    public sealed class AsyncRotatingFileLog : IApiLog, IDisposable
    {
        private readonly string _logFilePath;
        private readonly string _archiveFilePath;
        private readonly long _maxFileBytes;
        private readonly Channel<string> _channel;
        private readonly Task _writerTask;
        private int _disposed;

        public AsyncRotatingFileLog(
            string logFilePath,
            long maxFileBytes = 2 * 1024 * 1024,
            int capacity = 2048)
        {
            if (string.IsNullOrWhiteSpace(logFilePath))
                throw new ArgumentException("Log file path cannot be empty.", nameof(logFilePath));
            if (maxFileBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            _logFilePath = logFilePath;
            _archiveFilePath = logFilePath + ".1";
            _maxFileBytes = maxFileBytes;

            _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });

            _writerTask = Task.Run(ProcessQueueAsync);
        }

        public void Write(string message)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
            _channel.Writer.TryWrite(line);
        }

        private async Task ProcessQueueAsync()
        {
            var batch = new System.Collections.Generic.List<string>();
            try
            {
                while (await _channel.Reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    batch.Clear();
                    var charCount = 0;
                    while (_channel.Reader.TryRead(out var line))
                    {
                        batch.Add(line);
                        charCount += line.Length + 2;
                        if (charCount >= 16 * 1024) break;
                    }

                    if (batch.Count > 0)
                    {
                        AppendBatchSafe(batch);
                    }
                }
            }
            catch
            {
                // 日志失败不影响主流程
            }
        }

        private void AppendBatchSafe(System.Collections.Generic.IReadOnlyList<string> lines)
        {
            try
            {
                var directory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var currentLength = File.Exists(_logFilePath) ? new FileInfo(_logFilePath).Length : 0L;
                var chunk = new StringBuilder();

                foreach (var line in lines)
                {
                    var entry = line + Environment.NewLine;
                    var entryBytes = Encoding.UTF8.GetByteCount(entry);
                    if (currentLength > 0 && currentLength + entryBytes > _maxFileBytes)
                    {
                        if (chunk.Length > 0)
                        {
                            File.AppendAllText(_logFilePath, chunk.ToString(), Encoding.UTF8);
                            chunk.Clear();
                        }

                        if (File.Exists(_logFilePath))
                        {
                            File.Move(_logFilePath, _archiveFilePath, overwrite: true);
                        }

                        currentLength = 0;
                    }

                    chunk.Append(entry);
                    currentLength += entryBytes;
                }

                if (chunk.Length > 0)
                {
                    File.AppendAllText(_logFilePath, chunk.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // 日志失败不影响主流程
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _channel.Writer.TryComplete();
            try
            {
                _writerTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Ignore timeout/errors during shutdown flush
            }
        }
    }
}
