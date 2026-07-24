using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace ParsecIntegrationClient
{
    public class FileLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new FileLogger(categoryName);
        }

        public void Dispose()
        {
        }
    }

    public class FileLogger : ILogger
    {
        private readonly string _categoryName;

        public FileLogger(string categoryName)
        {
            _categoryName = categoryName;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var now = DateTime.Now;
            var level = logLevel.ToString().ToUpper();
            var message = formatter(state, exception);
            
            if (exception != null)
            {
                message += $"\nException: {exception}";
            }

            var logLine = $"{now:dd.MM.yyyy HH:mm:ss} LOG: {level}-{_categoryName} Message: {message}";

            try
            {
                var logPath = $@"{ServiceConfig.MainPath}\log\Log_At_{now.Year}_{now.Month}_{now.Day}.txt";
                var dir = Path.GetDirectoryName(logPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.AppendAllText(logPath, logLine + Environment.NewLine);
            }
            catch
            {
                // Игнорируем ошибки записи в файл
            }
        }
    }
}