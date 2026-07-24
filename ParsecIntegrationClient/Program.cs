using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ParsecIntegrationClient.Services;
using ParsecIntegrationClient.Models;  // ← ДОБАВИТЬ ЭТО

namespace ParsecIntegrationClient
{
    public static class Program
    {
        private static IHost _host;
        private static CancellationTokenSource _cts;

        public static void Main(string[] args)
        {
            var isConsole = args.Length > 0 && 
                (args[0].Equals("/console", StringComparison.OrdinalIgnoreCase) ||
                 args[0].Equals("--console", StringComparison.OrdinalIgnoreCase));

            EnsureLogDirectoryExists();

            if (isConsole || Environment.UserInteractive)
            {
                RunConsoleAsync().GetAwaiter().GetResult();
            }
            else
            {
                RunServiceAsync().GetAwaiter().GetResult();
            }
        }

        private static void EnsureLogDirectoryExists()
        {
            try
            {
                string logPath = $@"{ServiceConfig.MainPath}\log";
                if (!Directory.Exists(logPath))
                {
                    Directory.CreateDirectory(logPath);
                }
            }
            catch
            {
                // Игнорируем
            }
        }

        private static async Task RunConsoleAsync()
        {
            Console.WriteLine("========================================");
            Console.WriteLine("Parsec Integration Client - Консольный режим");
            Console.WriteLine("========================================");
            Console.WriteLine("Нажмите Ctrl+C для остановки...");
            Console.WriteLine();

            _cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                Console.WriteLine("\nПолучен сигнал остановки...");
                e.Cancel = true;
                _cts.Cancel();
            };

            _host = CreateHostBuilder().Build();
            await _host.StartAsync(_cts.Token);

            try
            {
                // Ждем завершения
                await Task.Delay(-1, _cts.Token);
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Остановка по запросу пользователя...");
            }
            finally
            {
                await _host.StopAsync();
                _host.Dispose();
                Console.WriteLine("\nНажмите любую клавишу для выхода...");
                Console.ReadKey();
            }
        }

        private static async Task RunServiceAsync()
        {
            _host = CreateHostBuilder().UseWindowsService().Build();
            await _host.RunAsync();
        }

        private static IHostBuilder CreateHostBuilder()
        {
            return Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // Конфигурация
                    services.Configure<Settings>(context.Configuration.GetSection("Settings"));

                    // Сервисы
                    services.AddSingleton<SettingsService>();
                    services.AddSingleton<ParsecService>();
                    services.AddSingleton<DatabaseService>();
                    services.AddSingleton<StateService>();
                    services.AddSingleton<LicenseService>();
                    services.AddSingleton<GuardantService>();

                    // Workers (запускаются автоматически)
                    services.AddHostedService<MainWorker>();
                    services.AddHostedService<SessionKeepAliveService>();

                    // Web Service клиент
                    services.AddSingleton<IntegrationWebService.IntegrationService>();
                })
                .ConfigureLogging((context, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    
                    // Добавляем кастомный логгер в файл
                    logging.AddProvider(new FileLoggerProvider());
                });
        }
    }
}