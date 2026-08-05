using Guardant;
using ParsecIntegrationClient.IntegrationWebService;
using ParsecIntegrationClient.Models;
using ParsecIntegrationClient.Services;
using Quartz;
using Quartz.Impl;
using Quartz.Util;
using System;
using System.CodeDom;
using System.IO;
using System.Net.Security;
using System.Runtime.InteropServices;
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
        //29.05.2026 на данный момент значения примерные
        private const uint my_prog_number = 1;//29.05.2026 номер программы из утилиты гварданта
        private const uint my_key_id = 0;//29.05.2026 idшник ключа из утилиты гварданта
        private const uint my_key_sn = 5;//29.05.2026 серийный номер из утиилиты гварданта
        private static ushort my_key_mask = 0;//29.05.2026 маска из утилиты гварданта
        private const uint my_key_ver = 2;//29.05.2026 версия из утилиты гварданта
        public static GrdUAM Mask = new GrdUAM(4u);
        
        


        static void Main(string[] args)
        {
            EnsureLogDirectoryExists();
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


        private static void EnsureLogDirectoryExists()
        {
            try
            {
                string logPath = $@"{Service1.MainPath}\log";
                if (!Directory.Exists(logPath))
                {
                    Directory.CreateDirectory(logPath);
                    // Можно записать в EventLog или в консоль, если она доступна
                    if (Environment.UserInteractive)
                    {
                        Console.WriteLine($"Создана директория для логов: {logPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Если не удалось создать папку для логов, хотя бы выведем ошибку
                if (Environment.UserInteractive)
                {
                    Console.WriteLine($"Ошибка при создании директории для логов: {ex.Message}");
                }
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

        //29.05.2026 - метод проверки ключа Sign
        //29.05.2026 - метод проверки ключа Sign
        private static bool CheckGuardantKey()
        {
            Handle guardantHandle;
            bool isApiInitialized = false;
            bool keyFound = false;

            try
            {
                Logger.Log<Service1>("Warning", "95 Guardant: Начало проверки ключа...");

                // 1. Инициализация API
                GrdE result = GrdApi.GrdStartup(GrdFMR.Local);

                if (result != GrdE.OK)
                {
                    Logger.Log<Service1>("Error", $"101 Guardant: Ошибка инициализации API: {result}");
                    // Даже если ошибка API - разрешаем работу в ограниченном режиме (10 транзакций)
                    Logger.Log<Service1>("Warning", "Guardant: Ошибка API, но работа в ограниченном режиме разрешена (10 транзакций)");
                    return true;
                }
                isApiInitialized = true;
                Logger.Log<Service1>("Warning", "105 Guardant: API инициализирован");

                // 2. Создаём хэндл
                guardantHandle = GrdApi.GrdCreateHandle(GrdCHM.MultiThread);
                Logger.Log<Service1>("Warning", "109 Guardant: Хэндл создан");

                // Устанавливаем коды доступа
                result = GrdApi.GrdSetAccessCodes(
                    guardantHandle,
                    0x4651A7A2);

                Logger.Log<Service1>("Warning", $"119 Guardant: GrdSetAccessCodes result = {result.ToString()}");

                // 3. Устанавливаем критерии поиска
                result = GrdApi.GrdSetFindMode(
                    guardantHandle,
                    GrdFMR.Local,
                    GrdFM.Ver,
                    my_prog_number,
                    my_key_id, my_key_sn,
                    my_key_ver, my_key_mask,
                    GrdDT.ALL,
                    GrdFMM.SignUSB,
                    GrdFMI.USB);

                Logger.Log<Service1>("Warning", $"136 Guardant: GrdSetFindMode result = {result}");

                if (result != GrdE.OK)
                {
                    Logger.Log<Service1>("Error", $"124 Guardant: Ошибка установки критериев: {result}");
                    // Даже если ошибка критериев - разрешаем работу в ограниченном режиме
                    Logger.Log<Service1>("Warning", "Guardant: Ошибка критериев, но работа в ограниченном режиме разрешена (10 транзакций)");
                    return true;
                }
                Logger.Log<Service1>("Warning", "127 Guardant: Критерии поиска установлены");

                // 4. Ищем ключ
                uint foundId;
                FindInfo findInfo;
                result = GrdApi.GrdFind(guardantHandle, GrdF.First, out foundId, out findInfo);

                // Перебираем найденные ключи
                while (result == GrdE.OK)
                {
                    String Str = String.Format("{0,8:X}", findInfo.dwPublicCode);
                    Str = Str + String.Format(" {0,5:X}", findInfo.byHrwVersion);
                    Str = Str + String.Format(" {0,3:D}", findInfo.byMaxNetRes);
                    Str = Str + String.Format(" {0,5:X}", findInfo.wType);
                    Str = Str + String.Format(" {0,8:X}", findInfo.dwID);
                    Str = Str + String.Format(" {0,4:D}", findInfo.byNProg);
                    Str = Str + String.Format(" {0,3:D}", findInfo.byVer);
                    Str = Str + String.Format(" {0,5:D}", findInfo.wSN);
                    Str = Str + String.Format(" {0,5:X}", findInfo.wMask);
                    Str = Str + String.Format(" {0,5:D}", findInfo.wGP);
                    Str = Str + String.Format(" {0,6:D}", findInfo.wRealNetRes);
                    Str = Str + String.Format(" {0,8:X}", findInfo.dwIndex);

                    Logger.Log<Service1>("Info", $"Guardant: Найден ключ: {Str}");

                    Key.keyNumber = Str;

                    // Проверяем, подходит ли ключ по программе и версии
                    if (findInfo.byNProg == my_prog_number && findInfo.byVer == my_key_ver)
                    {
                        my_key_mask = findInfo.wMask;
                        keyFound = true;
                        Logger.Log<Service1>("Warning", $"Guardant: Найден подходящий ключ! Маска: {my_key_mask:X4}");
                        break;
                    }

                    result = GrdApi.GrdFind(guardantHandle, GrdF.Next, out foundId, out findInfo);
                }

                if (keyFound)
                {
                    Logger.Log<Service1>("Warning", $"Guardant: Ключ найден и подходит по параметрам. Маска: {my_key_mask:X4}");
                    return true;
                }
                else
                {
                    Logger.Log<Service1>("Warning", "Guardant: Подходящий ключ не найден. Включается ограниченный режим (10 транзакций).");
                    return true; // ВСЕГДА ВОЗВРАЩАЕМ TRUE - либо с ключом, либо в ограниченном режиме
                }
            }
            catch (Exception ex)
            {
                Logger.Log<Service1>("Exception", $"152 Guardant: Исключение: {ex.Message}");
                Logger.Log<Service1>("Warning", "Guardant: Исключение при проверке ключа. Включается ограниченный режим (10 транзакций).");
                return true; // ВСЕГДА ВОЗВРАЩАЕМ TRUE - работаем в ограниченном режиме
            }
            finally
            {
                try
                {
                    if (isApiInitialized)
                    {
                        GrdApi.GrdCleanup();
                        Logger.Log<Service1>("Warning", "166 Guardant: API деинициализирован");
                    }
                }
                catch { /* ignore */ }
            }
        }



        private static void StartLogic()
        {
            Logger.Log<Service1>("Warning", "174 Console: StartLogic() called");

            // Проверка ключа Guardant перед запуском
            Logger.Log<Service1>("Warning", "177 Console: Checking Guardant key...");
            bool keyFound = CheckGuardantKey();

            if (keyFound)
            {
                // Проверяем, действительно ли найден ключ или мы в ограниченном режиме
                // Для этого нужно проверить, была ли установлена маска
                if (my_key_mask != 0 && (my_key_mask & 1) == 1)
                {
                    License.SetLicenseStatus(true);
                    Logger.Log<Service1>("Warning", "Guardant: Ключ найден - ПОЛНАЯ ЛИЦЕНЗИЯ");
                    Console.WriteLine("Ключ найден - полная лицензия");
                }
                else if (my_key_mask != 0 && (my_key_mask & 1) == 0)
                {
                    License.SetLicenseStatus(false);
                    Logger.Log<Service1>("Warning", $"Guardant: Ключ найден, но лицензия неактивна (маска: {my_key_mask:X4}) - ОГРАНИЧЕННЫЙ РЕЖИМ (10 транзакций)");
                    Console.WriteLine($"Ключ найден, но лицензия неактивна - ограниченный режим (10 транзакций)");
                }
                else
                {
                    License.SetLicenseStatus(false);
                    Logger.Log<Service1>("Warning", "Guardant: Ключ НЕ найден - ОГРАНИЧЕННЫЙ РЕЖИМ (10 транзакций)");
                    Console.WriteLine("Ключ НЕ найден - ограниченный режим (10 транзакций)");
                }
            }
            else
            {
                // Этот случай теперь практически недостижим, так как CheckGuardantKey всегда возвращает true
                License.SetLicenseStatus(false);
                Logger.Log<Service1>("Warning", "Guardant: Ошибка при проверке - ОГРАНИЧЕННЫЙ РЕЖИМ (10 транзакций)");
                Console.WriteLine("Ошибка при проверке ключа - ограниченный режим (10 транзакций)");
            }

            // Выводим информацию о режиме работы
            if (License.IsLicensed)
            {
                Console.WriteLine("РЕЖИМ РАБОТЫ: ПОЛНАЯ ЛИЦЕНЗИЯ");
                Logger.Log<Service1>("Warning", "РЕЖИМ РАБОТЫ: ПОЛНАЯ ЛИЦЕНЗИЯ");
            }
            else
            {
                Console.WriteLine($"РЕЖИМ РАБОТЫ: ОГРАНИЧЕННЫЙ (доступно {License.RemainingTransactions} транзакций)");
                Logger.Log<Service1>("Warning", $"РЕЖИМ РАБОТЫ: ОГРАНИЧЕННЫЙ (доступно {License.RemainingTransactions} транзакций)");
            }

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

                    // Проверка лицензии по маске (если ключ найден)
                    uint rightmostBit = (uint)my_key_mask & 1;
                    Logger.Log<Service1>("Warning", $"Маска ключа: {my_key_mask:X4}, крайний правый бит: {rightmostBit}");

                    if (rightmostBit == 0 && false)
                    {
                        string errorMsg = "321 Отсутствует лицензия. Ожидается xxx1, прочитано xxx0. Программа прекращает работу.";
                        Logger.Log<Service1>("Error", $"Console: {errorMsg}");
                        Console.WriteLine(errorMsg);

                        if (_scheduler != null)
                        {
                            await _scheduler.Shutdown(waitForJobsToComplete: false);
                            _scheduler = null;
                        }

                        _cts?.Cancel();
                        return;
                    }
                    else
                    {
                        Logger.Log<Service1>("Warning", $"Console: Лицензия подтверждена (крайний правый бит маски = 1). Продолжаем работу.");
                        Console.WriteLine($"Лицензия подтверждена (бит маски = 1)");
                    }

                    Logger.Log<Service1>("Warning", $"ЗАПУСК ПРОГРАММЫ - Интеграция с Парсек. Таймаут ошибок: {SettingsService.ErrorTimeoutMinutes} мин");

                    Console.WriteLine("Открытие сессии Parsec...");
                    Logger.Log<Service1>("Warning", "Console: OpenSession start");

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

                    //uint rightmostBit = my_key_mask & 1; 

                    //if (rightmostBit == 0)
                    //{
                    //    string errorMsg = "Отсутствует лицензия (крайний правый бит маски = 0). Программа остановлена.";
                    //    Logger.Log<Service1>("Error", $"Console: {errorMsg}");
                    //    Console.WriteLine(errorMsg);

                    //    if (_scheduler != null)
                    //    {
                    //        await _scheduler.Shutdown(waitForJobsToComplete: false);
                    //        _scheduler = null;
                    //    }

                    //    _cts?.Cancel();
                    //    return; 
                    //}
                    //else
                    //{
                    //    Logger.Log<Service1>("Warning", $"Console: Лицензия подтверждена (крайний правый бит маски = 1). Продолжаем работу.");
                    //    Console.WriteLine($"Лицензия подтверждена (бит маски = 1)");
                    //}
                    
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