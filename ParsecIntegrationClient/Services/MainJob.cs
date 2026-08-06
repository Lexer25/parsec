using ParsecIntegrationClient.Models;
using ParsecIntegrationClient.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Quartz;

namespace ParsecIntegrationClient.Services
{
    public class MainJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            await Task.Run(() => Execute());
        }

        public static void Execute()
        {
            Logger.Log<MainJob>("Warning", "21 ЗАПУСК ОБРАБОТКИ ЗАДАЧ");
            Logger.Log<MainJob>("Warning", $"22 ГЛОБАЛЬНЫЙ ТАЙМАУТ {SettingsService.DatabaseJobTimeout} секунд");
            var state = new State();
            // Проверка лицензии перед началом обработки
            if (!License.CanPerformOperation())
            {
                Logger.Log<MainJob>("Error", "ЛИЦЕНЗИЯ ОТСУТСТВУЕТ: Программа заблокирована. Обработка задач прекращена.");


                state.Status = "ERR";
                state.desc = "ЛИЦЕНЗИЯ ОТСУТСТВУЕТ: Превышен лимит бесплатных транзакций";
                state.ErrorMessage = "Лимит бесплатных транзакций (10) исчерпан";
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                state.keyNum = Key.keyNumber;
                
                StateService.SaveState(state);

                Logger.Log<MainJob>("Warning", $"Ожидание {SettingsService.ErrorTimeoutMinutes} минут перед следующей попыткой проверки лицензии");
              //  System.Threading.Thread.Sleep(SettingsService.ErrorTimeoutMinutes * 60 * 1000);
                return;
            }

            var rows = DatabaseService.GetList<DbModelRowIDInDev>(SettingsService.QuerySelectIdDevCardString).OrderBy(x => Convert.ToInt32(x.ID)).ToArray();

            if (rows == null || rows.Length == 0)
            {
                state.ErrorMessage = null;
                state.desc = "Задачи для обработки отсутствуют";
                state.Status = null;
                state.Attempts = null;
                state.IdCardindev = null;
                state.Timestamp = DateTime.Now;
                state.Operation = null;
                state.OperationCode = null;
                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                state.keyNum = Key.keyNumber;
                StateService.SaveState(state);
                Logger.Log<MainJob>("Warning", "Задач для обработки не найдено");
                Logger.Log<MainJob>("Warning", $"Ожидание {SettingsService.ErrorTimeoutMinutes} минут перед следующей попыткой");
            //    System.Threading.Thread.Sleep(SettingsService.ErrorTimeoutMinutes * 60 * 1000);
                return;
            }

            Logger.Log<MainJob>("Warning", $"64 Получено {rows.Length} записей для выполнения");

            // Проверяем глобальный таймаут ошибок перед началом обработки
            var stateFilePath = StateService.DefaultStateFilePath;
            if (File.Exists(stateFilePath))
            {
                var stateJson = File.ReadAllText(stateFilePath);
                if (!string.IsNullOrWhiteSpace(stateJson) && stateJson.Contains("\"status\":\"ERR\""))
                {
                    var timestampMatch = System.Text.RegularExpressions.Regex.Match(stateJson, "\"timestamp\":\"([^\"]+)\"");
                    if (timestampMatch.Success && DateTime.TryParse(timestampMatch.Groups[1].Value, out DateTime lastErrorTime))
                    {
                        var errorTimeout = lastErrorTime.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                        if (DateTime.Now < errorTimeout)
                        {
                            var remainingMinutes = (errorTimeout - DateTime.Now).TotalMinutes.ToString("F1");
                            Logger.Log<MainJob>("Warning", $"Обработка остановлена. Осталось ждать: {remainingMinutes} минут");
                            return;
                        }
                        else
                        {
                            Logger.Log<MainJob>("Info", "Таймаут ошибок истек, возобновляем обработку");
                        }
                    }
                } else {
                    Logger.Log<MainJob>("Warning", $"89 отладка");
                }
                Logger.Log<MainJob>("Warning", $"91 отладка");
            }
            Logger.Log<MainJob>("Warning", $"90 Продолжаю работу с {rows.Length} записями.");
            // Далее после команды обновляем state-файл, читая только "новые" строки.
            var logFilePath = StateService.GetLogFilePath(DateTime.Now);
            var prevLogPosition = StateService.GetLogFileLength(logFilePath);

            int i = 1;
            foreach (var row in rows)
            {
                // Проверка лицензии перед каждой транзакцией
                if (!License.CanPerformOperation())
                {
                    Logger.Log<MainJob>("Error", $"ЛИЦЕНЗИЯ ОТСУТСТВУЕТ: Превышен лимит. Обработка задачи {row.ID} прекращена.");

                    var errorState = new State
                    {
                        IdCardindev = row.ID,
                        Status = "ERR",
                        desc = "ЛИЦЕНЗИЯ ОТСУТСТВУЕТ: Превышен лимит бесплатных транзакций",
                        ErrorMessage = "Лимит бесплатных транзакций исчерпан",
                        Timestamp = DateTime.Now,
                        Operation = StateService.GetOperationName(row.OPERATION),
                        OperationCode = row.OPERATION,
                        Attempts = row.ATTEMPS,
                        NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes),
                        keyNum = Key.keyNumber
                    };
                    StateService.SaveState(errorState);

                    // Удаляем задачу, чтобы не зацикливаться
                    DatabaseService.DeleteIdInDevById(row.ID);
                    Logger.Log<MainJob>("Warning", $"Задача {row.ID} удалена из-за отсутствия лицензии");
                    continue; // Продолжаем со следующей задачей
                }

                Logger.Log<MainJob>("Info", $"ЗАДАЧА {i}/{rows.Length} Начало обработки cardindev {row.ID}. Осталось транзакций: {License.RemainingTransactions}");

                // Предварительные проверки до выполнения операции.
                int operationPrecheck;
                if (!int.TryParse(row.OPERATION, out operationPrecheck))
                {
                    var desc = $"MainJob precheck: некорректный тип операции '{row.OPERATION}' для cardindev={row.ID}";
                    var errorMessage = $"Ошибка валидации: некорректный код операции '{row.OPERATION}', ожидается число от 1 до 10";
                    Logger.Log<MainJob>("Error", desc);
                    state.desc = desc;
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "ERR";
                    state.ErrorMessage = errorMessage;
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.keyNum = Key.keyNumber;
                    StateService.SaveState(state);
                    Logger.Log<MainJob>("Warning", $"ГЛОБАЛЬНАЯ ОСТАНОВКА Некорректная операция {row.OPERATION} для задачи {row.ID}");
                    return;
                }

                // Для некоторых операций ID_CARD обязан быть GUID (иначе операция заведомо некорректна).
                if ((operationPrecheck == 4 || operationPrecheck == 6) && !Guid.TryParse(row.ID_CARD, out _))
                {
                    var desc = $"MainJob precheck: для операции {operationPrecheck} ожидается GUID в ID_CARD, получено '{row.ID_CARD}' (cardindev={row.ID})";
                    var errorMessage = $"Ошибка валидации: для операции {operationPrecheck} ожидается GUID в поле ID_CARD, получено '{row.ID_CARD}'";
                    Logger.Log<MainJob>("Error", desc);
                    state.desc = desc;
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "ERR";
                    state.ErrorMessage = errorMessage;
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.keyNum = Key.keyNumber;
                    StateService.SaveState(state);
                    Logger.Log<MainJob>("Warning", $"ГЛОБАЛЬНАЯ ОСТАНОВКА Некорректный GUID в ID_CARD для задачи {row.ID}");
                    return;
                }

                int operation = 0;
                MOperation operationName = MOperation.Добавление_карточки;
                string komuName = null;
                string accessName = null;

                try
                {
                    operation = Convert.ToInt32(row.OPERATION);
                    operationName = (MOperation)Enum.GetValues(typeof(MOperation)).GetValue(operation - 1);

                    // Для операций, где нужен человек (3/4/7/8), выводим читаемые названия.
                    if (operation == 3 || operation == 4 || operation == 7 || operation == 8)
                    {
                        komuName = DatabaseService.GetString(
                            $"select coalesce(p.surname,'') || ' ' || coalesce(p.name,'') || ' ' || coalesce(p.patronymic,'') from people p where p.id_pep = {row.ID_PEP}");

                        // Для операций добавления/удаления категории доступа (7/8) выводим и категорию.
                        if (operation == 7 || operation == 8)
                        {
                            accessName = DatabaseService.GetString(
                                $"select an.name from accessname an where an.id_accessname = {row.ID_CARD}");
                        }
                    }
                }
                catch (Exception)
                {
                    // Игнорируем ошибки при получении доп. данных
                }

                // Выполняем команду.
                Exception lastException = null;
                string operationResult = "OK";
                State result = new State();
                try
                {
                    switch (row.OPERATION)
                    {
                        case "1": // Добавление карточки
                            {
                                //result = ParsecService.AddCardForPeople(row);
                                break;
                            }
                        case "2": // Удалить карточку
                            {
                                //result = ParsecService.RemoveCardForPeople(row);
                                break;
                            }
                        case "3": //Добавление человека
                            {
                                result = ParsecService.AddPeople(row);
                                break;
                            }
                        case "4": //Удаление человека
                            {
                                result = ParsecService.RemovePeople(row);
                                break;
                            }
                        case "5": //Добавление организации
                            {
                                result = ParsecService.AddOrg(row);
                                break;
                            }
                        case "6": //Удаление организации
                            {
                                result = ParsecService.RemoveOrg(row);
                                break;
                            }
                        case "7": //Добавление категории доступа
                            {
                                result = ParsecService.AddAccGroupPeople(row);
                                break;
                            }
                        case "8": //Удаление категории доступа
                            {
                                result = ParsecService.RemoveIdentifierPeople(row);
                                break;
                            }
                        case "9"://Добавление карты человеку
                            {
                                //result = ParsecService.AddCardForPeople(row);
                                result = ParsecService.AddCardPeople(row);
                                break;
                            }
                        case "10"://Удаление карты у человека
                            {
                                result = ParsecService.RemoveCardPeople(row);
                                break;
                            }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log<MainJob>("Warning", $"Command failed: {ex.Message}");
                    operationResult = "ERR";
                    lastException = ex;
                }

                Logger.Log<MainJob>("Debug", result.ToString());
                StateService.SaveState(result);

                if (result.Status == "ERR")
                {
                    if (SettingsService.SkipErrCode != null && SettingsService.SkipErrCode.Contains(result.ErrorCode))
                    {
                        Logger.Log<MainJob>("Info", $"ErrorCode ({result.ErrorCode}) находится в списке _skipErrCode в appsettings.json. Задача {row.ID} была удалена автоматически.");
                        DatabaseService.DeleteIdInDevById(row.ID);
                    }
                    else
                    {
                        DatabaseService.IncrementAttemp(row);
                        Logger.Log<MainJob>("Warning", $"ГЛОБАЛЬНАЯ ОСТАНОВКА Обработка прекращена: {result.ErrorMessage}");
                        Logger.Log<MainJob>("Warning", $"ГЛОБАЛЬНЫЙ ТАЙМАУТ Следующая попытка через {SettingsService.ErrorTimeoutMinutes} минут");
                        return;
                    }
                }
                else if (result.Status == "OK")
                {
                    Logger.Log<MainJob>("Info", $"Операция выполнена успешно УДАЛЕНИЕ ЗАДАЧИ {row.ID}");
                    DatabaseService.DeleteIdInDevById(row.ID);
                }
                else if (result.Status == "SKIP")
                {
                    Logger.Log<MainJob>("Info", $"Команда не реализована или пропущена");
                    DatabaseService.DeleteIdInDevById(row.ID);
                }

                if (result == null)
                {
                    Logger.Log<MainJob>("Warning", $"ГЛОБАЛЬНАЯ ОСТАНОВКА Обработка прекращена: result is null");
                    Logger.Log<MainJob>("Warning", $"ГЛОБАЛЬНЫЙ ТАЙМАУТ Следующая попытка через {SettingsService.ErrorTimeoutMinutes} минут");
                    return;
                }

                i++;
            }

            Logger.Log<MainJob>("Warning", "Все задачи успешно обработаны");

            // Проверяем, остались ли еще задачи
            var remainingRows = DatabaseService.GetList<DbModelRowIDInDev>(SettingsService.QuerySelectIdDevCardString).ToArray();
            if (remainingRows == null || remainingRows.Length == 0)
            {
                Logger.Log<MainJob>("Warning", "Нет новых задач для обработки");
                Logger.Log<MainJob>("Warning", $"Ожидание {SettingsService.ErrorTimeoutMinutes} минут перед следующей попыткой");
             //   System.Threading.Thread.Sleep(SettingsService.ErrorTimeoutMinutes * 60 * 1000);
            }
        }
    }
}