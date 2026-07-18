using System.Text;
using System.Text.Json;
using AkashaAutomation.Worker.Bridge;
using Microsoft.Extensions.Logging;

namespace AkashaAutomation.Worker.Logging;

public sealed record JsonRollingFileLoggerOptions(
    string FilePath,
    long MaximumFileBytes = 2 * 1024 * 1024,
    int RetainedFileCount = 5)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(FilePath);
        if (MaximumFileBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumFileBytes));
        }

        if (RetainedFileCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RetainedFileCount));
        }
    }
}

public sealed class JsonRollingFileLoggerProvider : ILoggerProvider
{
    private readonly object _writeGate = new();
    private readonly JsonRollingFileLoggerOptions _options;

    public JsonRollingFileLoggerProvider(JsonRollingFileLoggerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();

        var directory = Path.GetDirectoryName(Path.GetFullPath(_options.FilePath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public ILogger CreateLogger(string categoryName) =>
        new JsonRollingFileLogger(categoryName, Write);

    public void Dispose()
    {
    }

    private void Write(string line)
    {
        lock (_writeGate)
        {
            try
            {
                RotateIfRequired(Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length);
                File.AppendAllText(
                    _options.FilePath,
                    line + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void RotateIfRequired(int incomingBytes)
    {
        var file = new FileInfo(_options.FilePath);
        if (!file.Exists || file.Length + incomingBytes <= _options.MaximumFileBytes)
        {
            return;
        }

        if (_options.RetainedFileCount == 0)
        {
            File.Delete(_options.FilePath);
            return;
        }

        for (var index = _options.RetainedFileCount; index >= 1; index--)
        {
            var destination = GetArchivePath(index);
            if (index == _options.RetainedFileCount && File.Exists(destination))
            {
                File.Delete(destination);
            }

            var source = index == 1 ? _options.FilePath : GetArchivePath(index - 1);
            if (File.Exists(source))
            {
                File.Move(source, destination);
            }
        }
    }

    private string GetArchivePath(int index)
    {
        var directory = Path.GetDirectoryName(_options.FilePath);
        var fileName = Path.GetFileNameWithoutExtension(_options.FilePath);
        var extension = Path.GetExtension(_options.FilePath);
        return Path.Combine(directory ?? string.Empty, $"{fileName}.{index}{extension}");
    }

    private sealed class JsonRollingFileLogger(
        string categoryName,
        Action<string> write) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            try
            {
                ArgumentNullException.ThrowIfNull(formatter);
                var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
                if (state is IEnumerable<KeyValuePair<string, object?>> values)
                {
                    foreach (var value in values)
                    {
                        if (!value.Key.Equals("{OriginalFormat}", StringComparison.Ordinal))
                        {
                            properties[value.Key] = value.Value;
                        }
                    }
                }

                var entry = new
                {
                    timestampUtc = DateTimeOffset.UtcNow,
                    level = logLevel.ToString(),
                    category = categoryName,
                    eventId = eventId.Id,
                    message = formatter(state, exception),
                    exception = exception?.ToString(),
                    properties,
                };
                write(JsonSerializer.Serialize(entry, CompanionProtocol.JsonOptions));
            }
            catch (Exception serializationException)
            {
                TryWriteSerializationFallback(logLevel, eventId, serializationException);
            }
        }

        private void TryWriteSerializationFallback(
            LogLevel logLevel,
            EventId eventId,
            Exception serializationException)
        {
            try
            {
                write(
                    JsonSerializer.Serialize(
                        new
                        {
                            timestampUtc = DateTimeOffset.UtcNow,
                            level = logLevel.ToString(),
                            category = categoryName,
                            eventId = eventId.Id,
                            message = "A log entry could not be serialized.",
                            loggingError = serializationException.GetType().Name,
                        },
                        CompanionProtocol.JsonOptions));
            }
            catch
            {
                // Logging must never terminate the Worker.
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
