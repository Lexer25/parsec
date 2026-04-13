using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace ParsecIntegrationClient.Services
{
    public class Logger
    {
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
                    // Неизвестный уровень считаем информативным.
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

            // Логируем только если уровень вызова >= выбранного минимального уровня.
            var minLevel = GetLevel(SettingsService.LogLevel);
            var curLevel = GetLevel(log);
            if (curLevel < minLevel)
            {
                return;
            }

            // Файл (как раньше)
            File.AppendAllText(
                $@"{Service1.MainPath}\log\LogAt{now.Day}_{now.Month}_{now.Year}.txt",
                logMessage);

            // Дублирование в консоль (для консольного режима/отладки).
            // В Windows-сервисе консоль может отсутствовать — тогда просто проглатываем ошибку.
            try
            {
                Console.WriteLine(logLine);
            }
            catch
            {
                // ignore
            }
        }
    }
}
