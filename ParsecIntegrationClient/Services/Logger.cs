using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace ParsecIntegrationClient.Services
{
    public class Logger
    {


        [MethodImpl(MethodImplOptions.Synchronized)]
        public static void Log<T>(string log, string message)
        {
            var now = DateTime.Now;
            var datePoint = $"{now.Day}.{now.Month}.{now.Year} {now.Hour}:{now.Minute}:{now.Second}";
            var logMessage = $"{datePoint} LOG: {log}-{typeof(T).Name} Message: {message.PadRight(50)}\n";
            File.AppendAllText(
                $@"{Service1.MainPath}\log\LogAt{now.Day}_{now.Month}_{now.Year}.txt",
                logMessage);
        }
    }
}
