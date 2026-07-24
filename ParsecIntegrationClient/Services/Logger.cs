using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace ParsecIntegrationClient.Services
{
    public class Logger
    {
        public static string LogLevel { get; set; } = "Warning";

        private static int GetLevel(string level)
        {
            var s = (level ?? string.Empty).Trim().ToLowerInvariant();
            switch (s)
            {
                case "info":
                case "debug":
                case "trace":
                    return 0;
                case "warning":
                case "warn":
                    return 1;
                case "error":
                    return 2;
                case "exception":
                    return 3;
                default:
                    return 0;
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static void Log<T>(string log, string message)
        {
            var now = DateTime.Now;
            var datePoint = $"{now.Day}.{now.Month}.{now.Year} {now.Hour}:{now.Minute}:{now.Second}";
            var logLine = $"{datePoint} LOG: {log}-{typeof(T).Name} Message: {message.PadRight(50)}";
            var logMessage = logLine + Environment.NewLine;

            var minLevel = GetLevel(SettingsService.LogLevel ?? "Warning");
            var curLevel = GetLevel(log);
            if (curLevel < minLevel)
            {
                return;
            }

            // ⬇️ ИЗМЕНЕНО: ServiceConfig.MainPath вместо Service1.MainPath ⬇️
            var logPath = $@"{ServiceConfig.MainPath}\log\Log_At_{now.Year}_{now.Month}_{now.Day}.txt";
            
            try
            {
                var dir = Path.GetDirectoryName(logPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.AppendAllText(logPath, logMessage);
                Console.WriteLine(logLine);
            }
            catch
            {
                // ignore
            }
        }
    }
}