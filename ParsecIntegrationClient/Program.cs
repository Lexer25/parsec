using ParsecIntegrationClient.IntegrationWebService;
using ParsecIntegrationClient.Models;
using ParsecIntegrationClient.Services;
using Quartz;
using Quartz.Impl;
using System;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace ParsecIntegrationClient
{
    internal static class Program
    {
        private static CancellationTokenSource _cts;
        private static Task _mainTask;
        private static IScheduler _scheduler;
        private static ManualResetEvent _stopEvent = new ManualResetEvent(false);

        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0].Equals("/console", StringComparison.OrdinalIgnoreCase))
            {
                // Консольный режим
                RunAsConsole();
            }
            else if (Environment.UserInteractive)
            {
                // Интерактивный режим (консоль)
                Console.WriteLine("Запуск в консольном режиме...");
                Console.WriteLine("Для запуска как служба используйте: /console");
                RunAsConsole();
            }
            else
            {
                // Режим службы
                RunAsService();
            }
        }

        private static void RunAsService()
        {
            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
                new Service1()
            };
            ServiceBase.Run(ServicesToRun);
        }

        private static void RunAsConsole()
        {
            Console.WriteLine("========================================");
            Console.WriteLine("Parsec Integration Client - Консольный режим");
            Console.WriteLine("========================================");
            Console.WriteLine("Нажмите Ctrl+C для остановки...");
            Console.WriteLine();

            // Настраиваем обработку остановки
            Console.CancelKeyPress += (sender, e) =>
            {
                Console.WriteLine("\nПолучен сигнал остановки...");
                e.Cancel = true;
                _stopEvent.Set();
            };

            // Запускаем основную логику
            StartLogic();

            // Ждем завершения
            _stopEvent.WaitOne();

            // Останавливаем логику
            StopLogic();

            Console.WriteLine("\nНажмите любую клавишу для выхода...");
            Console.ReadKey();
        }

        private static void StartLogic()
        {
            Logger.Log<Service1>("Warning", "Console: StartLogic() called");
            _cts = new CancellationTokenSource();

            _mainTask = Task.Run(async () =>
            {
                try
                {
                    Logger.Log<Service1>("Warning", "Console: Inside Task.Run, creating log directory...");
                    if (!Directory.Exists($@"{Service1.MainPath}\log"))
                        Directory.CreateDirectory($@"{Service1.MainPath}\log");

                    SettingsService.Update();
                    Console.WriteLine($"Таймаут ошибок: {SettingsService.ErrorTimeoutMinutes} минут");
                    Console.WriteLine($"Интервал выполнения: {SettingsService.DatabaseJobTimeout} секунд");

                    Logger.Log<Service1>("Warning", $"ЗАПУСК ПРОГРАММЫ - Интеграция с Парсек. Таймаут ошибок: {SettingsService.ErrorTimeoutMinutes} мин");

                    Console.WriteLine("Открытие сессии Parsec...");
                    Logger.Log<Service1>("Warning", "Console: OpenSession start");

                    // Логин/пароль как в Service1
                    string name = "parsec";
                    string password = "parsec";
                    string domain = "SYSTEM";

                    Logger.Log<Service1>("Warning", "Console: Creating IntegrationService...");
                    var igServ = new IntegrationService();
                    Logger.Log<Service1>("Warning", "Console: Calling OpenSession...");
                    var result = igServ.OpenSession(domain, name, password);
                    Logger.Log<Service1>("Warning", $"Console: OpenSession result = {result.Result}");
                    if (result.Result != ClientState.Result_Success)
                    {
                        Logger.Log<Service1>("Error", $"Console: Authorization error | {result.ErrorMessage}");
                        Console.WriteLine($"Авторизация не удалась: {result.ErrorMessage}");
                        Logger.Log<Service1>("Error", $"КРИТИЧЕСКАЯ ОШИБКА Авторизация не удалась: {result.ErrorMessage}");
                        return;
                    }

                    ClientState.SetSession(result.Value, domain, name);
                    Console.WriteLine("Авторизация успешна!");
                    Logger.Log<Service1>("Warning", "Авторизация успешна");

                    Console.WriteLine("Запуск Quartz...");
                    Logger.Log<Service1>("Warning", "Console: Creating StdSchedulerFactory...");
                    var factory = new StdSchedulerFactory();
                    Logger.Log<Service1>("Warning", "Console: Getting scheduler...");
                    _scheduler = await factory.GetScheduler();
                    Logger.Log<Service1>("Warning", "Console: Starting scheduler...");
                    await _scheduler.Start();
                    Logger.Log<Service1>("Warning", "Console: Scheduler started");

                    Logger.Log<Service1>("Warning", "Console: Creating MainJob...");
                    var databaseJob = JobBuilder.Create<MainJob>().Build();
                    Logger.Log<Service1>("Warning", "Console: Creating trigger for MainJob...");
                    var triggerPing = TriggerBuilder.Create()
                        .StartNow()
                        .WithSimpleSchedule(x => x
                            .WithIntervalInSeconds(SettingsService.DatabaseJobTimeout)
                            .RepeatForever())
                        .Build();

                    Logger.Log<Service1>("Warning", "Console: Creating ContinueSessionJob...");
                    var continueSessionJob = JobBuilder.Create<ContinueSessionJob>().Build();
                    Logger.Log<Service1>("Warning", "Console: Creating trigger for ContinueSessionJob...");
                    var continueSessionPing = TriggerBuilder.Create()
                        .WithSimpleSchedule(x => x
                            .WithIntervalInSeconds(4 * 60)
                            .RepeatForever())
                        .Build();

                    Logger.Log<Service1>("Warning", "Console: Scheduling MainJob...");
                    await _scheduler.ScheduleJob(databaseJob, triggerPing);
                    Logger.Log<Service1>("Warning", "Console: MainJob scheduled");

                    Logger.Log<Service1>("Warning", "Console: Scheduling ContinueSessionJob...");
                    await _scheduler.ScheduleJob(continueSessionJob, continueSessionPing);
                    Logger.Log<Service1>("Warning", "Console: ContinueSessionJob scheduled");

                    Console.WriteLine("Задачи запущены");
                    Logger.Log<Service1>("Warning", "=== CONSOLE MODE STARTED SUCCESSFULLY ===");

                    // Ждем отмены (Ctrl+C)
                    _cts.Token.WaitHandle.WaitOne();
                }
                catch (Exception ex)
                {
                    Logger.Log<Service1>("Exception", $"Console: {ex.Message}");
                    Console.WriteLine($"Ошибка: {ex.Message}");
                    Logger.Log<Service1>("Error", $"КРИТИЧЕСКАЯ ОШИБКА {ex.Message}");
                }
                finally
                {
                    Console.WriteLine("Программа остановлена");
                    Logger.Log<Service1>("Info", "Программа остановлена");
                }
            }, _cts.Token);
        }

        private static void StopLogic()
        {
            if (_cts != null)
            {
                _cts.Cancel();

                try
                {
                    if (_scheduler != null)
                    {
                        // Ждем завершения job-ов, чтобы не оставлять незакрытые сессии/ресурсы.
                        var shutdownTask = _scheduler.Shutdown(waitForJobsToComplete: true);
                        shutdownTask?.GetAwaiter().GetResult();
                        _scheduler = null;
                    }
                }
                catch
                {
                    // Игнорируем ошибки shutdown
                }

                try
                {
                    _mainTask?.Wait(TimeSpan.FromSeconds(5));
                }
                catch
                {
                    // Игнорируем ошибки ожидания
                }

                _cts.Dispose();
            }
        }
    }
}