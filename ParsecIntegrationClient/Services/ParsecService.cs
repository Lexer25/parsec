using Newtonsoft.Json;
using ParsecIntegrationClient.IntegrationWebService;
using ParsecIntegrationClient.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;


namespace ParsecIntegrationClient.Services
{
    public class ParsecService
    {

        /// <summary>
        /// Поиск указанных категорий доступа в списке всех категорий доступа.
        /// ответ: accessGroup.ID найденной группы доступа
        /// </summary>
        /// <param name="InheritedAccessGroups"></param>
        /// <returns></returns>
        public static Guid CheckAccessGroups(List<Guid> InheritedAccessGroups)
        {
            var integServ = new IntegrationService();
            var accessGroups = integServ.GetAccessGroups(ClientState.SessionID);

            foreach (var accessGroup in accessGroups)
            {
                var list = integServ.GetInheritedAccessGroups(ClientState.SessionID, accessGroup.ID);

                bool isEqual = list.OrderBy(a => a).SequenceEqual(InheritedAccessGroups.OrderBy(a => a));

                if (isEqual)
                {
                    return accessGroup.ID;
                }
            }

            return Guid.Empty;
        }

        /** 22.03.2026 проверка наличия указанного GUID
         * 
         */
        public static bool CheckGuidePresent(Guid guid)
        {
            try
            {
                var integServ = new IntegrationService();
                var result = integServ.GetObjectName(ClientState.SessionID, guid);

                // Проверяем, что результат не null и Value не пустой
                if (result != null && result.Value != null && !string.IsNullOrEmpty(result.Value.ToString()))
                {
                    // Logger.Log<ParsecService>("Info", $"47 CheckGuidePresent: GUID {guid} найден, Value: {result.Value}");
                    return true;
                }

                //Logger.Log<ParsecService>("Info", $"51 CheckGuidePresent: GUID {guid} не найден, Value пуст");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Log<ParsecService>("Error", $"56 CheckGuidePresent: Ошибка при проверке GUID {guid}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// проверка наличия категории доступа
        /// если не найдена, то вернет nuu
        /// если найдена, то вернет список категорий доступа, куда входит искомая категория
        /// </summary>
        /// <param name="idGroup"></param>
        /// <returns></returns>
        public static AccessGroup GetAccessGroups(Guid idGroup)
        {
            try
            {
                var integServ = new IntegrationService();
                var accessGroupsResult = integServ.GetAccessGroups(ClientState.SessionID);

                if (accessGroupsResult == null)
                {
                    Logger.Log<ParsecService>("Warning", $"99 GetAccessGroups: Результат null для SessionID: {ClientState.SessionID}");
                    return null;
                }

                var accesGroups = accessGroupsResult.ToList();

                foreach (var item in accesGroups)
                {
                    if (item != null && item.ID.Equals(idGroup))
                        return item;
                }

                Logger.Log<ParsecService>("Warning", $"111 GetAccessGroups: Группа с GUID {idGroup} не найдена");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Log<ParsecService>("Error", $"116 GetAccessGroups: Ошибка при получении групп доступа: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Главный метод-координатор для добавления идентификатора сотруднику. ОБработка команды 7
        /// на вхогде - строка из таблицы cardindev
        /// </summary>
        public static State AddAccGroupPeople(DbModelRowIDInDev row)
        {
            Logger.Log<ParsecService>("Info", $"102 Start AddIdentifierPeople {row.ID}");

            try
            {
                // 1. Формирование SQL-запроса для выборки guid'ов пипла и категории доступа
                var query = BuildAddAccessCatQuery(row);

                // 2. Получение модели категории доступа из БД через Get. модель содержит в себе guid пипла и категории доступа.
                var model = DatabaseService.Get<DbModelAddAccessCategory>(query);

                if (model == null)
                {
                    Logger.Log<ParsecService>("Warning", "114 No models found");
                    return CreateErrorState(row, "Модели данных не найдены", 1);
                }

                Logger.Log<ParsecService>("Info", $"118 Found GUID_ACCGROUP {model.GUID_ACCGROUP} models to process");

                // 3. Обработка модели 
                var successCount = 0;
                var errorCount = 0;
                var errorMessages = new List<string>();
                State lastState = null;
                int index = 0;

                    Logger.Log<ParsecService>("Warning", $"152 отладка");
                    
                    // Основная бизнес-логика
                    //
                    lastState = ProcessAccessCatAddition(row, model);
                    Logger.Log<ParsecService>("Warning", $"155 отладка");
                    if (lastState.Status == "OK")
                    {
                        successCount++;
                        Logger.Log<ParsecService>("Info", $"159 Model {index} processed successfully");
                    }
                    else
                    {
                        errorCount++;
                        errorMessages.Add($"Модель {index}: {lastState.ErrorMessage}");
                        Logger.Log<ParsecService>("Error", $"165 Model {index} failed: {lastState.ErrorMessage}");

                        // Критическая ошибка - прерываем обработку
                        if (lastState.ErrorCode >= 7)
                        {
                            return lastState;
                        }
                    }

                    Logger.Log<ParsecService>("Warning", $"176 отладка");


                // 4. Формируем итоговый результат
                Logger.Log<ParsecService>("Info", $"176 Processing completed: Total=, Success={successCount}, Errors={errorCount}");

                if (successCount > 0 && errorCount == 0)
                {
                    return CreateSuccessState(row, $"180 Все {successCount} операций выполнены успешно");
                }
                else if (successCount > 0 && errorCount > 0)
                {
                    var summary = $"184 Выполнено {successCount} операций, {errorCount} с ошибками. Детали: {string.Join("; ", errorMessages)}";
                    return CreatePartialSuccessState(row, summary);
                }
                else if (successCount == 0 && errorCount > 0)
                {
                    var summary = $"189 Все {errorCount} операций завершились с ошибками: {string.Join("; ", errorMessages)}";
                    return CreateErrorState(row, summary, 99);
                }
                else
                {
                    return CreateErrorState(row, "194 Неизвестная ошибка при обработке", 99);
                }
            }
            catch (Exception ex)
            {
                Logger.Log<ParsecService>("Error", $"199 Critical error in AddIdentifierPeople: {ex.Message}");
                DatabaseService.IncrementAttemp(row);
                return CreateErrorState(row, $"201 Критическая ошибка: {ex.Message}", 9);
            }
        }

        public static State AddIdentifierPeople(DbModelRowIDInDev row)
        {
            Logger.Log<ParsecService>("Info", $"102 Start AddIdentifierPeople {row.ID}");

            try
            {
                // 1. Формирование SQL-запроса
                var query = BuildIdentifierQuery(row);

                // 2. Получение ВСЕХ моделей из БД через GetList
                var models = DatabaseService.GetList<DbModelAddIdentifier>(query);

                if (models == null || models.Count == 0)
                {
                    Logger.Log<ParsecService>("Warning", "114 No models found");
                    return CreateErrorState(row, "Модели данных не найдены", 1);
                }

                Logger.Log<ParsecService>("Info", $"118 Found {models.Count} models to process");

                // 3. Обработка КАЖДОЙ модели в цикле
                var successCount = 0;
                var errorCount = 0;
                var errorMessages = new List<string>();
                State lastState = null;
                int index = 0;

                foreach (var model in models)
                {
                    index++;

                    Logger.Log<ParsecService>("Info", $"131 Processing model {index}/{models.Count}: Card={model.CODE}");

                    // Проверка типа карты
                    if (!int.TryParse(model.CARDTYPE, out int cardType) || cardType != 1)
                    {
                        errorCount++;
                        var errorMsg = $"137 Интегратор не обрабатывает идентификаторы с типом {model.CARDTYPE}";
                        errorMessages.Add($"138 Модель {index}: {errorMsg}");
                        Logger.Log<ParsecService>("Warning", $"139 Skipped model {index}: {errorMsg}");
                        continue;
                    }
                    Logger.Log<ParsecService>("Warning", $"142 отладка");
                    
                    // Валидация модели
                    var validationState = ValidateModel(model, row);
                    if (validationState != null)
                    {
                        errorCount++;
                        errorMessages.Add($"148 Модель {index}: {validationState.ErrorMessage}");
                        Logger.Log<ParsecService>("Warning", $"149 Validation failed for model {index}");
                        continue;
                    }
                    Logger.Log<ParsecService>("Warning", $"152 отладка");
                    
                    // Основная бизнес-логика
                    lastState = ProcessIdentifierAddition(row, model);
                    Logger.Log<ParsecService>("Warning", $"155 отладка");
                    if (lastState.Status == "OK")
                    {
                        successCount++;
                        Logger.Log<ParsecService>("Info", $"159 Model {index} processed successfully");
                    }
                    else
                    {
                        errorCount++;
                        errorMessages.Add($"Модель {index}: {lastState.ErrorMessage}");
                        Logger.Log<ParsecService>("Error", $"165 Model {index} failed: {lastState.ErrorMessage}");

                        // Критическая ошибка - прерываем обработку
                        if (lastState.ErrorCode >= 7)
                        {
                            return lastState;
                        }
                    }

                    Logger.Log<ParsecService>("Warning", $"176 отладка");
                }

                // 4. Формируем итоговый результат
                Logger.Log<ParsecService>("Info", $"176 Processing completed: Total={models.Count}, Success={successCount}, Errors={errorCount}");

                if (successCount > 0 && errorCount == 0)
                {
                    return CreateSuccessState(row, $"180 Все {successCount} операций выполнены успешно");
                }
                else if (successCount > 0 && errorCount > 0)
                {
                    var summary = $"184 Выполнено {successCount} операций, {errorCount} с ошибками. Детали: {string.Join("; ", errorMessages)}";
                    return CreatePartialSuccessState(row, summary);
                }
                else if (successCount == 0 && errorCount > 0)
                {
                    var summary = $"189 Все {errorCount} операций завершились с ошибками: {string.Join("; ", errorMessages)}";
                    return CreateErrorState(row, summary, 99);
                }
                else
                {
                    return CreateErrorState(row, "194 Неизвестная ошибка при обработке", 99);
                }
            }
            catch (Exception ex)
            {
                Logger.Log<ParsecService>("Error", $"199 Critical error in AddIdentifierPeople: {ex.Message}");
                DatabaseService.IncrementAttemp(row);
                return CreateErrorState(row, $"201 Критическая ошибка: {ex.Message}", 9);
            }
        }

        /// <summary>
        /// Формирование SQL-запроса для получения моделей идентификаторов
        /// </summary>
        private static string BuildAddAccessCatQuery(DbModelRowIDInDev row)
        {
             var query = "select  p.guid, an.guid  from accessname an , people p " +
                        $"where p.id_pep = {row.ID_PEP} " +
                        $"and an.id_accessname = {row.ID_CARD}";



            Logger.Log<ParsecService>("Info", $"331 Executing query for models addAccessCat with ID_PEP={row.ID_PEP}, ID_accessname={row.ID_CARD}");
            Logger.Log<ParsecService>("Debug", $"332 Query: {query}");

            return query;
        }

        /// <summary>
        /// Формирование SQL-запроса для получения моделей идентификаторов
        /// </summary>
        private static string BuildIdentifierQuery(DbModelRowIDInDev row)
        {
            var query = "select c.id_card, an.guid, p.guid as people_guid, " +
                        "p.tabnum, p.name, p.patronymic, p.surname, c.id_cardtype " +
                        "from card c, " +
                        "accessname an join people p on p.id_pep = c.id_pep " +
                        $"where c.id_pep = {row.ID_PEP} " +
                        $"and an.id_accessname = {row.ID_CARD}";

            Logger.Log<ParsecService>("Info", $"217 Executing query for models with ID_PEP={row.ID_PEP}, ID_CARD={row.ID_CARD}");
            Logger.Log<ParsecService>("Debug", $"218 Query: {query}");

            return query;
        }

        /// <summary>
        /// Валидация модели перед обработкой
        /// </summary>
        private static State ValidateModel(DbModelAddIdentifier model, DbModelRowIDInDev row)
        {
            // 1. Проверка наличия номера карты
            if (string.IsNullOrEmpty(model.CODE))
            {
                var errorMsg = $"Отсутствует номер карты (CODE=null) для контакта ID={row.ID_PEP}";
                return CreateErrorState(row, errorMsg, 4);
            }

            // 2. Проверка GUID сотрудника в Артонит
            if (string.IsNullOrEmpty(model.GUID_PEP))
            {
                var errorMsg = $"Поле GUID_PEP не заполнено для сотрудника {model.SURNAME} {model.NAME}";
                return CreateErrorState(row, errorMsg, 5);
            }

            // 3. Проверка наличия сотрудника в Parsec
            if (!CheckGuidePresent(new Guid(model.GUID_PEP)))
            {
                var errorMsg = $"Сотрудник не синхронизирован в Parsec (GUID_PEP: {model.GUID_PEP})";
                return CreateErrorState(row, errorMsg, 5);
            }

            // 4. Проверка GUID группы доступа в Артонит
            if (string.IsNullOrEmpty(model.GUID_ACCESS_GROUP))
            {
                var errorMsg = $"Поле GUID_ACCESS_GROUP не заполнено для группы доступа";
                return CreateErrorState(row, errorMsg, 5);
            }

            // 5. Проверка наличия группы доступа в Parsec
            var accessGroupGuid = new Guid(model.GUID_ACCESS_GROUP);
            if (!CheckGuidePresent(accessGroupGuid))
            {
                var errorMsg = $"Группа доступа не синхронизирована в Parsec (GUID: {model.GUID_ACCESS_GROUP})";
                return CreateErrorState(row, errorMsg, 6);
            }

            return null; // Валидация пройдена
        }

        /// <summary>
        /// !!! Основная логика добавления категории доступа идентификаторам
        /// </summary>
        private static State ProcessAccessCatAddition(DbModelRowIDInDev row, DbModelAddAccessCategory model)
        {
            var integServ = new IntegrationService();
            var accessGroupGuid = new Guid(model.GUID_ACCGROUP);
            var accesGroup = GetAccessGroups(accessGroupGuid);
            var person = integServ.GetPerson(ClientState.SessionID, new Guid(model.GUID_PEP));

            try
            {
                // Открываю сессию редактирования                 
                var res = integServ.OpenPersonEditingSession(ClientState.SessionID, new Guid(model.GUID_PEP));

                if (res.Result != ClientState.Result_Success)
                {
                    Logger.Log<ParsecService>("Error", $"123 Ошибка открытия сессии для редактирования пользователя. Ошибка {res.ErrorMessage}");
                    var desc = $"123 Ошибка открытия сессии: {res.ErrorMessage}";
                    return CreateErrorState(row, desc, 7);
                }

                var _editSessionID = res.Value;
                Logger.Log<ParsecService>("Warning", $"130 Вызываю метод GetAccessGroups с параметром model.GUID_ACCESS_GROUP GUID: {model.GUID_ACCGROUP}");

                // Получение списка идентификаторов для пипла
                var identifiers = integServ.GetPersonIdentifiers(ClientState.SessionID, new Guid(model.GUID_PEP));

                if (identifiers == null || identifiers.Length == 0)
                {
                    Logger.Log<ParsecService>("Warning", $"Для {person?.FIRST_NAME} GUID_PEP {model.GUID_PEP} идентификаторы не найдены");
                    return CreateSuccessState(row, $"Идентификаторы не найдены для пользователя {person?.FIRST_NAME}");
                }

                Logger.Log<ParsecService>("Warning", $"437 для {person?.FIRST_NAME} GUID_PEP {model.GUID_PEP} найдено {identifiers.Length} идентификаторов.");

                // Для каждого идентификатора из списка
                foreach (var identifier in identifiers)
                {
                    Logger.Log<ParsecService>("Warning", $"433 начал обработку карты {identifier.CODE}.");

                    var creatingItem = identifier; // копирую текущие значения

                    // Если не было категорий доступа, то добавляю ее                    
                    if (identifier.ACCGROUP_ID == Guid.Empty)
                    {
                        Logger.Log<ParsecService>("Warning", $"223 у карты {identifier.CODE} категории доступа не было, поэтому присваиваю identifier.ACCGROUP_ID --> {model.GUID_ACCGROUP}");
                        creatingItem.ACCGROUP_ID = accessGroupGuid;
                    }
                    else
                    {
                        Logger.Log<ParsecService>("Warning", $"291 у карты {identifier.CODE} уже была категория доступа identifier.ACCGROUP_ID --> {identifier.ACCGROUP_ID}. Значит, проверяю ее состав.");

                        // Получаю наследников
                        var arrayInheritedAccessGroups = integServ.GetInheritedAccessGroups(ClientState.SessionID, identifier.ACCGROUP_ID);
                        var inheritedAccessGroups = (arrayInheritedAccessGroups == null
                            ? new List<Guid>()
                            : arrayInheritedAccessGroups.ToList());

                        if (inheritedAccessGroups.Count == 0)
                            inheritedAccessGroups.Add(identifier.ACCGROUP_ID);

                        if (accesGroup != null && accesGroup.ID != Guid.Empty)
                            inheritedAccessGroups.Add(accesGroup.ID);

                        inheritedAccessGroups = inheritedAccessGroups.Distinct().ToList();

                        Logger.Log<ParsecService>("Warning", $"471 Найдено уникальных групп доступа (количество) {inheritedAccessGroups.Count}");

                        // Поиск, если группы доступа есть в списке постоянных
                        var resCheckAccessGroups = CheckAccessGroups(inheritedAccessGroups);

                        Logger.Log<ParsecService>("Warning", $"173 Результат поиска группы доступа с такими же вложенными группами доступа {resCheckAccessGroups}");

                        if (resCheckAccessGroups != Guid.Empty)
                        {
                            creatingItem.ACCGROUP_ID = resCheckAccessGroups;
                        }
                        else
                        {
                            var schedules = integServ.GetAccessSchedules(ClientState.SessionID);

                            if (schedules == null || schedules.Length == 0)
                            {
                                var errorDesc = "Нет доступных расписаний для создания группы доступа";
                                Logger.Log<ParsecService>("Error", errorDesc);
                                return CreateErrorState(row, errorDesc, 8);
                            }

                            var newNameAccessGroup = string.Empty;
                            inheritedAccessGroups.ForEach(x =>
                            {
                                var ag = GetAccessGroups(x);
                                if (ag != null && !string.IsNullOrEmpty(ag.NAME))
                                    newNameAccessGroup += $"{ag.NAME} ";
                            });

                            if (string.IsNullOrWhiteSpace(newNameAccessGroup))
                                newNameAccessGroup = "(Особая) Artsec";

                            // Ограничиваем длину имени
                            if (newNameAccessGroup.Length > 255)
                                newNameAccessGroup = newNameAccessGroup.Substring(0, 252) + "...";

                            var resCreateAccessGroup = integServ.CreateAccessGroup(
                                ClientState.SessionID,
                                newNameAccessGroup,
                                schedules[0].ID,
                                null);

                            if (resCreateAccessGroup.Result != ClientState.Result_Success)
                            {
                                var errorDesc = $"292 Ошибка CreateAccessGroup: {resCreateAccessGroup.ErrorMessage}";
                                Logger.Log<ParsecService>("Error", errorDesc);
                                return CreateErrorState(row, errorDesc, 8);
                            }

                            var rGuid = resCreateAccessGroup.Value;
                            integServ.SetInheritedAccessGroups(ClientState.SessionID, rGuid, inheritedAccessGroups.ToArray());
                            creatingItem.ACCGROUP_ID = rGuid;
                        }
                    }

                    // Обновляю идентификатор
                    Logger.Log<ParsecService>("Error", $"246 вызываю метод AddPersonIdentifier с параметрами: " +
                        $"ACCGROUP_ID = {creatingItem.ACCGROUP_ID} " +
                        $"PRIVILEGE_MASK = {creatingItem.PRIVILEGE_MASK} " +
                        $"IDENTIFTYPE = {creatingItem.IDENTIFTYPE} " +
                        $"NAME = {creatingItem.NAME} " +
                        $"CODE = {creatingItem.CODE} " +
                        $"PERSON_ID = {creatingItem.PERSON_ID} " +
                        $"IS_PRIMARY = {creatingItem.IS_PRIMARY}");

                    var resAddPersonIdentifier = integServ.AddPersonIdentifier(_editSessionID, creatingItem);
                    Logger.Log<ParsecService>("Error", $"252 Результат выполнения AddPersonIdentifier: {resAddPersonIdentifier.Result}.");

                    if (resAddPersonIdentifier.Result != ClientState.Result_Success)
                    {
                        var errorDesc = $"232 Ошибка при добавлении группы доступа пользователю. Ошибка: {resAddPersonIdentifier.ErrorMessage}";
                        Logger.Log<ParsecService>("Error", errorDesc);
                        return CreateErrorState(row, errorDesc, 2);
                    }

                    Logger.Log<ParsecService>("Warning", $"238 Группа доступа успешно добавлена | " +
                        $"code: {row.ID_CARD} (hex: {creatingItem.CODE}) " +
                        $"Пользователю ФИО (parsec): {person?.FIRST_NAME} {person?.MIDDLE_NAME} {person?.LAST_NAME}");
                }

                // Закрываем сессию редактирования
                try
                {
                    integServ.ClosePersonEditingSession(_editSessionID);
                    Logger.Log<ParsecService>("Info", $"Сессия редактирования {_editSessionID} закрыта");
                }
                catch (Exception ex)
                {
                    Logger.Log<ParsecService>("Warning", $"Ошибка при закрытии сессии: {ex.Message}");
                }

                return CreateSuccessState(row, "Операция выполнена успешно");
            }
            catch (Exception ex)
            {
                DatabaseService.IncrementAttemp(row);
                var errorDesc = $"587 Ошибка в ProcessAccessCatAddition: {ex.Message}";
                Logger.Log<ParsecService>("Warning", $"{errorDesc} | {ex.Source} | {ex.StackTrace}");
                return CreateErrorState(row, errorDesc, 9);
            }
        }
        /// <summary>
        /// Основная логика добавления идентификатора
        /// </summary>
        private static State ProcessIdentifierAddition(DbModelRowIDInDev row, DbModelAddIdentifier model)
        {
            var integServ = new IntegrationService();
            var accessGroupGuid = new Guid(model.GUID_ACCESS_GROUP);
            var hexValue = Convert.ToInt64(model.CODE).ToString("X8");
            var accessName = GetAccessGroupName(int.Parse(row.ID_CARD));

            // 1. Получение сотрудника из Parsec
            var person = integServ.GetPerson(ClientState.SessionID, new Guid(model.GUID_PEP));
            if (person == null)
            {
                Logger.Log<ParsecService>("Warning", $"Пользователь с GUID: {model.GUID_PEP} не найден в Parsec");
                return CreateSuccessState(row, "Пользователь не найден в Parsec, задача удалена");
            }

            // 2. Открытие сессии редактирования
            var sessionResult = integServ.OpenPersonEditingSession(ClientState.SessionID, person.ID);
            if (sessionResult.Result != ClientState.Result_Success)
            {
                var errorMsg = $"Ошибка открытия сессии редактирования: {sessionResult.ErrorMessage}";
                Logger.Log<ParsecService>("Error", errorMsg);
                return CreateErrorState(row, errorMsg, 7);
            }

            var editSessionID = sessionResult.Value;

            try
            {
                // 3. Получение существующих идентификаторов
                var identifiers = integServ.GetPersonIdentifiers(ClientState.SessionID, new Guid(model.GUID_PEP));
                var existingIdentifier = identifiers?.FirstOrDefault(x => x.CODE == hexValue);

                // 4. Создание или обновление идентификатора
                var creatingItem = BuildIdentifier(model, hexValue, accessGroupGuid, existingIdentifier, integServ);

                // 5. Логирование операции
                LogIdentifierAddition(model, hexValue, accessName, existingIdentifier);

                // 6. Добавление идентификатора
                var addResult = integServ.AddPersonIdentifier(editSessionID, creatingItem);
                if (addResult.Result != ClientState.Result_Success)
                {
                    var errorMsg = $"Ошибка добавления идентификатора: {addResult.ErrorMessage}";
                    Logger.Log<ParsecService>("Error", errorMsg);
                    return CreateErrorState(row, errorMsg, 2);
                }

                // 7. Успешное завершение
                Logger.Log<ParsecService>("Info",
                    $"319 Идентификатор успешно добавлен | Сотрудник: {person.FIRST_NAME} {person.LAST_NAME} | Карта: {hexValue}");

                return CreateSuccessState(row, "Операция выполнена успешно");
            }
            finally
            {
                // 8. Гарантированное закрытие сессии
                try
                {
                    integServ.ClosePersonEditingSession(editSessionID);
                    Logger.Log<ParsecService>("Info", $"Сессия редактирования {editSessionID} закрыта");
                }
                catch (Exception ex)
                {
                    Logger.Log<ParsecService>("Warning", $"Ошибка при закрытии сессии: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Создание State с частичным успехом
        /// </summary>
        private static State CreatePartialSuccessState(DbModelRowIDInDev row, string description)
        {
            var state = CreateBaseState(row);
            state.Status = "WARN";
            state.desc = description;
            state.ErrorMessage = description;
            state.ErrorCode = 0;
            state.NextStart = DateTime.Now;

            Logger.Log<ParsecService>("Warning", $"Partial success: {description}");
            return state;
        }

        /// <summary>
        /// Создание объекта идентификатора для добавления
        /// </summary>
        private static Identifier BuildIdentifier(
            DbModelAddIdentifier model,
            string hexValue,
            Guid accessGroupGuid,
            Identifier existingIdentifier,
            IntegrationService integServ)
        {
            var creatingItem = new Identifier
            {
                PERSON_ID = new Guid(model.GUID_PEP),
                CODE = hexValue,
                IS_PRIMARY = true,
                NAME = "",
                PRIVILEGE_MASK = 0,
                IDENTIFTYPE = 0
            };

            // Если идентификатор уже существует и у него есть группа доступа
            if (existingIdentifier != null && existingIdentifier.ACCGROUP_ID != Guid.Empty)
            {
                Logger.Log<ParsecService>("Info",
                    $"378 У карты {hexValue} уже есть группа доступа {existingIdentifier.ACCGROUP_ID}. " +
                    "Формируем inherited-цепочку");

                // Получаем цепочку наследования
                var inheritedChain = GetInheritedAccessGroupsChain(
                    existingIdentifier.ACCGROUP_ID,
                    accessGroupGuid,
                    integServ);

                // Получаем или создаем группу доступа с этой цепочкой
                creatingItem.ACCGROUP_ID = GetOrCreateAccessGroupWithInheritance(inheritedChain, integServ);
            }
            else
            {
                // Просто назначаем целевую группу доступа
                creatingItem.ACCGROUP_ID = accessGroupGuid;
                Logger.Log<ParsecService>("Info", $"394 Назначаем группу доступа {accessGroupGuid} для карты {hexValue}");
            }

            return creatingItem;
        }

        /// <summary>
        /// Получение цепочки наследования групп доступа
        /// </summary>
        private static List<Guid> GetInheritedAccessGroupsChain(
            Guid currentGroupId,
            Guid targetGroupId,
            IntegrationService integServ)
        {
            // Получаем существующую цепочку наследования
            var inheritedGroups = integServ.GetInheritedAccessGroups(ClientState.SessionID, currentGroupId)?.ToList()
                                  ?? new List<Guid>();

            // Если цепочка пуста, добавляем текущую группу как базовую
            if (!inheritedGroups.Any())
            {
                inheritedGroups.Add(currentGroupId);
                Logger.Log<ParsecService>("Info", $"416 Цепочка наследования пуста, добавляем базовую группу {currentGroupId}");
            }

            // Добавляем целевую группу
            inheritedGroups.Add(targetGroupId);

            // Удаляем дубликаты
            var distinctChain = inheritedGroups.Distinct().ToList();

            Logger.Log<ParsecService>("Info",
                $"426 Сформирована цепочка наследования из {distinctChain.Count} групп: " +
                string.Join(" -> ", distinctChain));

            return distinctChain;
        }

        /// <summary>
        /// Получение или создание группы доступа с указанной цепочкой наследования
        /// </summary>
        private static Guid GetOrCreateAccessGroupWithInheritance(
            List<Guid> inheritedChain,
            IntegrationService integServ)
        {
            // 1. Проверяем, существует ли уже группа с такой цепочкой
            var existingGroupId = CheckAccessGroups(inheritedChain);
            if (existingGroupId != Guid.Empty)
            {
                Logger.Log<ParsecService>("Info", $"447 Найдена существующая группа доступа {existingGroupId} с нужной цепочкой");
                return existingGroupId;
            }

            // 2. Создаем новую группу доступа
            var groupName = BuildAccessGroupName(inheritedChain, integServ);
            var schedules = integServ.GetAccessSchedules(ClientState.SessionID);
            var scheduleId = schedules?.FirstOrDefault()?.ID ?? Guid.Empty;

            Logger.Log<ParsecService>("Info", $"456 Создаем новую группу доступа '{groupName}' с расписанием {scheduleId}");

            var createResult = integServ.CreateAccessGroup(
                ClientState.SessionID,
                groupName,
                scheduleId,
                null);

            if (createResult.Result != ClientState.Result_Success)
            {
                throw new InvalidOperationException($"Не удалось создать группу доступа: {createResult.ErrorMessage}");
            }

            var newGroupId = createResult.Value;

            // 3. Устанавливаем цепочку наследования
            integServ.SetInheritedAccessGroups(ClientState.SessionID, newGroupId, inheritedChain.ToArray());

            Logger.Log<ParsecService>("Info", $"474 Создана группа доступа {newGroupId} с цепочкой наследования");

            return newGroupId;
        }

        /// <summary>
        /// Формирование имени группы доступа на основе цепочки наследования
        /// </summary>
        private static string BuildAccessGroupName(List<Guid> inheritedChain, IntegrationService integServ)
        {
            var names = new List<string>();

            foreach (var groupId in inheritedChain)
            {
                var group = GetAccessGroups(groupId);
                if (group != null && !string.IsNullOrEmpty(group.NAME))
                {
                    names.Add(group.NAME);
                }
            }

            var name = names.Any() ? string.Join(" + ", names) : "(Особая) Artsec";

            // Ограничиваем длину имени, если необходимо
            if (name.Length > 255)
            {
                name = name.Substring(0, 252) + "...";
            }

            return name;
        }

        /// <summary>
        /// Получение имени группы доступа по ID
        /// </summary>
        private static string GetAccessGroupName(int accessNameId)
        {
            try
            {
                return DatabaseService.GetString($"SELECT name FROM accessname WHERE id_accessname = {accessNameId}")
                       ?? $"Группа {accessNameId}";
            }
            catch (Exception ex)
            {
                Logger.Log<ParsecService>("Warning", $"Не удалось получить имя группы {accessNameId}: {ex.Message}");
                return $"Группа {accessNameId}";
            }
        }

        /// <summary>
        /// Логирование операции добавления идентификатора
        /// </summary>
        private static void LogIdentifierAddition(
            DbModelAddIdentifier model,
            string hexValue,
            string accessName,
            Identifier existingIdentifier)
        {
            var logMessage = new StringBuilder();
            logMessage.AppendLine($"Добавление идентификатора:");
            logMessage.AppendLine($"  - Сотрудник: {model.SURNAME} {model.NAME} {model.PATRONYMIC}");
            logMessage.AppendLine($"  - Табельный номер: {model.TAB_NUM_PEP}");
            logMessage.AppendLine($"  - Карта: {hexValue} (dec: {Convert.ToInt64(model.CODE)})");
            logMessage.AppendLine($"  - Группа доступа: {accessName}");
            logMessage.AppendLine($"  - GUID_PEP: {model.GUID_PEP}");

            if (existingIdentifier != null)
            {
                logMessage.AppendLine($"  - Существующая группа: {existingIdentifier.ACCGROUP_ID}");
            }

            Logger.Log<ParsecService>("Info", logMessage.ToString());
        }

        /// <summary>
        /// Создание State с ошибкой
        /// </summary>
        private static State CreateErrorState(DbModelRowIDInDev row, string errorMessage, int errorCode)
        {
            var state = CreateBaseState(row);
            state.Status = "ERR";
            state.ErrorMessage = errorMessage?.Replace("\r\n", " ") ?? errorMessage;
            state.ErrorCode = errorCode;
            state.desc = $"КОД ОШИБКИ: {errorCode}. {errorMessage}";
            state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);

            Logger.Log<ParsecService>("Error", $"State создан с ошибкой {errorCode}: {errorMessage}");
            return state;
        }

        /// <summary>
        /// Создание State с успешным статусом
        /// </summary>
        private static State CreateSuccessState(DbModelRowIDInDev row, string description)
        {
            var state = CreateBaseState(row);
            state.Status = "OK";
            state.desc = description;
            state.ErrorMessage = null;
            state.ErrorCode = 0;
            state.NextStart = DateTime.Now;

            return state;
        }

        /// <summary>
        /// Создание базового State с общими полями
        /// </summary>
        private static State CreateBaseState(DbModelRowIDInDev row)
        {
            return new State
            {
                IdCardindev = row.ID,
                Operation = StateService.GetOperationName(row.OPERATION),
                OperationCode = row.OPERATION,
                Attempts = row.ATTEMPS,
                Timestamp = DateTime.Now,
                keyNum = Key.keyNumber
            };
        }

        // ==================== ОСТАЛЬНЫЕ МЕТОДЫ (без изменений) ====================

        public static State _AddIdentifierPeople(DbModelRowIDInDev row)
        {
            // Оставляем как есть для обратной совместимости
            // или удаляем, если не используется
            var state = new State();
            Logger.Log<ParsecService>("Warning", $"62 Start AddIdentifierPeople {row.ID}");
            try
            {
                var integServ = new IntegrationService();

                var query = "select c.id_card, an.guid, p.guid as people_guid, " +
                    "p.tabnum, p.name, p.patronymic, p.surname, c.id_cardtype from card c, " +
                    "accessname an join people p on p.id_pep = c.id_pep " +
                    $"where c.id_pep = {row.ID_PEP} " +
                    $"and an.id_accessname = {row.ID_CARD}";

                Logger.Log<ParsecService>("Info", $"134 Запрос к базе {query} для model");
                var model = DatabaseService.Get<DbModelAddIdentifier>(query);
                Logger.Log<ParsecService>("Info", $"136 model {Newtonsoft.Json.JsonConvert.SerializeObject(model)}");


                if (int.Parse(model.CARDTYPE) != 1)
                {
                    var desc = $"142 КОД ОШИБКИ: {state.ErrorCode} Интегратор не обрабатывает идентификаторы с {model.CARDTYPE}. Обработка прерывается";
                    Logger.Log<ParsecService>("Error", desc);
                    state.desc = desc;
                    state.Status = "ERR";
                    state.ErrorMessage = desc;
                    state.IdCardindev = row.ID;
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                    state.ErrorCode = 3;
                    state.keyNum = Key.keyNumber;
                    Logger.Log<ParsecService>("Warning", $"700 Stop RemoveIdentifierPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                    return state;
                }

                string accessName = DatabaseService.GetString(
                    $"select an.name from accessname an where an.id_accessname = {row.ID_CARD}");

                if (model.CODE == null)
                {
                    state.ErrorCode = 4;
                    DatabaseService.IncrementAttemp(row);
                    var desc = $"77 КОД: {state.ErrorCode}. SQL нет номера карты в Артонит. Добавление категории доступа прервано.";
                    var errorMessage = $"Ошибка БД: отсутствует номер карты (CODE=null) для контакта ID={row.ID_PEP}";
                    Logger.Log<ParsecService>("Info", desc);
                    state.desc = desc;
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "ERR";
                    state.ErrorMessage = errorMessage;
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                    state.keyNum = Key.keyNumber;
                    Logger.Log<ParsecService>("Warning", $"188 Stop AddIdentifierPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                    return state;
                }

                try
                {
                    if (model.GUID_PEP == null || model.GUID_PEP == String.Empty)
                    {
                        state.ErrorCode = 5;
                        var desc = $"КОД: {state.ErrorCode}. Добавление категории | Сотрудник: {model.SURNAME} {model.NAME} {model.PATRONYMIC} | Группа: {accessName} | Ошибка: сотрудник не зарегистрирован в Артонит (GUID_PEP пустой)";
                        var errorMessage = $"Ошибка БД: поле GUID_PEP не заполнено для сотрудника {model.SURNAME} {model.NAME} {model.PATRONYMIC}";
                        Logger.Log<ParsecService>("Warning", desc);
                        state.desc = desc;
                        state.IdCardindev = row.ID;
                        state.Operation = StateService.GetOperationName(row.OPERATION);
                        state.OperationCode = row.OPERATION;
                        state.Status = "ERR";
                        state.ErrorMessage = errorMessage;
                        state.Attempts = row.ATTEMPS;
                        state.Timestamp = DateTime.Now;
                        state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                        state.keyNum = Key.keyNumber;
                        Logger.Log<ParsecService>("Warning", $"211 Stop AddIdentifierPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                        return state;
                    }
                    if (!CheckGuidePresent(new Guid(model.GUID_PEP)))
                    {
                        state.ErrorCode = 5;
                        var desc = $"КОД {state.ErrorCode}Добавление категории | Сотрудник: {model.SURNAME} {model.NAME} {model.PATRONYMIC} | Группа: {accessName} | Ошибка: сотрудник не найден в Парсек (GUID_PEP: {model.GUID_PEP})";
                        var errorMessage = $"Ошибка: сотрудник не синхронизирован в Parsec (GUID_PEP: {model.GUID_PEP})";
                        Logger.Log<ParsecService>("Warning", desc);
                        state.desc = desc;
                        state.IdCardindev = row.ID;
                        state.Operation = StateService.GetOperationName(row.OPERATION);
                        state.OperationCode = row.OPERATION;
                        state.Status = "ERR";
                        state.ErrorMessage = errorMessage;
                        state.Attempts = row.ATTEMPS;
                        state.Timestamp = DateTime.Now;
                        state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                        state.keyNum = Key.keyNumber;
                        Logger.Log<ParsecService>("Warning", $"230 Stop AddIdentifierPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                        return state;
                    }

                    if (model.GUID_ACCESS_GROUP == null || model.GUID_ACCESS_GROUP == String.Empty)
                    {
                        state.ErrorCode = 5;
                        var desc = $"Добавление категории | Сотрудник: {model.SURNAME} {model.NAME} {model.PATRONYMIC} | Группа: {accessName} | Ошибка: группа доступа не найдена в Артонит (GUID_ACCESS_GROUP пустой)";
                        var errorMessage = $"Ошибка БД: поле GUID_ACCESS_GROUP не заполнено для группы доступа {accessName}";
                        Logger.Log<ParsecService>("Warning", desc);
                        state.desc = desc;
                        state.IdCardindev = row.ID;
                        state.Operation = StateService.GetOperationName(row.OPERATION);
                        state.OperationCode = row.OPERATION;
                        state.Status = "ERR";
                        state.ErrorMessage = errorMessage;
                        state.Attempts = row.ATTEMPS;
                        state.Timestamp = DateTime.Now;
                        state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                        state.keyNum = Key.keyNumber;
                        Logger.Log<ParsecService>("Warning", $"252 Stop AddIdentifierPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                        return state;
                    }

                    var accessGroupGuid = new Guid(model.GUID_ACCESS_GROUP);
                    if (!CheckGuidePresent(accessGroupGuid))
                    {

                        var accessGroupNameLocal = DatabaseService.GetString(
                            $"select an.name from accessname an where an.id_accessname = {row.ID_CARD}");
                        var accessGroupNameResult = integServ.GetObjectName(ClientState.SessionID, accessGroupGuid);
                        var accessGroupName = (accessGroupNameResult != null && accessGroupNameResult.Value != null)
                            ? accessGroupNameResult.Value.ToString()
                            : string.Empty;

                        if (string.IsNullOrWhiteSpace(accessGroupName))
                            accessGroupName = accessGroupNameLocal;

                        Logger.Log<ParsecService>("Warning",
                            $"204 Группа доступа {accessGroupName} не найдена в Парсек. " +
                            "Операция добавления категории доступа прекращается.");
                        var desc = $"Добавление категории | Сотрудник: {model.SURNAME} {model.NAME} {model.PATRONYMIC} | Группа: {accessGroupName} | Ошибка: группа доступа не найдена в Парсек";
                        var errorMessage = $"Ошибка: группа доступа не синхронизирована в Parsec (GUID: {model.GUID_ACCESS_GROUP})";
                        state.desc = desc;
                        state.IdCardindev = row.ID;
                        state.Operation = StateService.GetOperationName(row.OPERATION);
                        state.OperationCode = row.OPERATION;
                        state.Status = "ERR";
                        state.ErrorMessage = errorMessage;
                        state.Attempts = row.ATTEMPS;
                        state.Timestamp = DateTime.Now;
                        state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                        state.ErrorCode = 6;
                        state.keyNum = Key.keyNumber;
                        Logger.Log<ParsecService>("Warning", $"285 Stop AddIdentifierPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                        return state;
                    }

                    long cardDec = Convert.ToInt64(model.CODE);
                    string hexValue = cardDec.ToString("X8");

                    var person = integServ.GetPerson(ClientState.SessionID, new Guid(model.GUID_PEP));

                    if (person != null)
                    {
                        var res = integServ.OpenPersonEditingSession(ClientState.SessionID, person.ID);

                        if (res.Result != ClientState.Result_Success)
                        {
                            Logger.Log<ParsecService>("Error", $"123 Ошибка открытия сессии для редактирования пользователя. " +
                                $"Ошибка {res.ErrorMessage}");
                            var desc = $"123 Ошибка открытия сессии: {res.ErrorMessage}";
                            state.desc = desc;
                            state.IdCardindev = row.ID;
                            state.Operation = StateService.GetOperationName(row.OPERATION);
                            state.OperationCode = row.OPERATION;
                            state.Status = "ERR";
                            state.ErrorMessage = res.ErrorMessage;
                            state.Attempts = row.ATTEMPS;
                            state.Timestamp = DateTime.Now;
                            state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                            state.ErrorCode = 7;
                            state.keyNum = Key.keyNumber;
                            return state;
                        }

                        var _editSessionID = res.Value;
                        Logger.Log<ParsecService>("Warning", $"130 Вызываю метод GetAccessGroups с параметром model.GUID_ACCESS_GROUP  GUID: {model.GUID_ACCESS_GROUP}");

                        var accesGroup = GetAccessGroups(new Guid(model.GUID_ACCESS_GROUP));

                        if (accesGroup == null)
                        {
                            var desc = $"179 Группа доступа {(!string.IsNullOrWhiteSpace(accessName) ? accessName : row.ID_CARD)} не найдена в парсек";
                            var errorMessage = $"Ошибка: группа доступа не найдена в Parsec (GUID: {model.GUID_ACCESS_GROUP})";
                            Logger.Log<ParsecService>("Warning", desc);
                            state.desc = desc;
                            state.IdCardindev = row.ID;
                            state.Operation = StateService.GetOperationName(row.OPERATION);
                            state.OperationCode = row.OPERATION;
                            state.Status = "ERR";
                            state.ErrorMessage = errorMessage;
                            state.Attempts = row.ATTEMPS;
                            state.Timestamp = DateTime.Now;
                            state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                            state.ErrorCode = 6;
                            state.keyNum = Key.keyNumber;
                            return state;
                        }

                        Logger.Log<ParsecService>("Warning", $"107 Добавление группы доступа пользователю |" +
                           $"Группа доступа: {accesGroup.NAME} | карта: hex {hexValue} (dec: {cardDec}) | GUID_PEP = {model.GUID_PEP} " +
                           $"| tab_num = {model.TAB_NUM_PEP} " +
                           $"| ФИО (artsec): {model.SURNAME} {model.NAME} {model.PATRONYMIC}");

                        Logger.Log<ParsecService>("Warning", $"137 вызываю метод GetPersonIdentifiers : {ClientState.SessionID} | GUID: {model.GUID_PEP} | new Guid {new Guid(model.GUID_PEP)}");

                        var identifiers = integServ.GetPersonIdentifiers(ClientState.SessionID, new Guid(model.GUID_PEP));
                        Logger.Log<ParsecService>("Warning", $"140 контрольная точка после вызова метода GetPersonIdentifiers : {ClientState.SessionID} | GUID: {model.GUID_PEP} | new Guid {new Guid(model.GUID_PEP)}");

                        var identifier = null as Identifier;
                        if (identifiers == null)
                        {
                            Logger.Log<ParsecService>("Warning", $"208 GetPersonIdentifiers вернул null для пользователя с GUID: {model.GUID_PEP}");
                            DatabaseService.IncrementAttemp(row);
                        }
                        else
                        {
                            identifier = identifiers.FirstOrDefault(x => x.CODE == hexValue);
                            Logger.Log<ParsecService>("Warning", $"216 Идентификатор с кодом:  {hexValue} | {identifier.CODE} действительно имеется у пользователя.");
                        }

                        var creatingItem = new Identifier();
                        creatingItem.PERSON_ID = new Guid(model.GUID_PEP);
                        creatingItem.NAME = "";

                        if (identifier != null)
                        {
                            Logger.Log<ParsecService>("Warning", $"148 карте {identifier.CODE} присвоена категория доступа identifier.ACCGROUP_ID --> {identifier.ACCGROUP_ID}");

                            if (identifier.ACCGROUP_ID == Guid.Empty)
                            {
                                Logger.Log<ParsecService>("Warning", $"223 у карты {identifier.CODE} категории доступа не было, поэтому присваиваю identifier.ACCGROUP_ID --> {model.GUID_ACCESS_GROUP}");
                                creatingItem.ACCGROUP_ID = new Guid(model.GUID_ACCESS_GROUP);
                                creatingItem.IS_PRIMARY = true;
                                creatingItem.CODE = hexValue;
                            }
                            else
                            {
                                Logger.Log<ParsecService>("Warning", $"291 у карты {identifier.CODE} уже была категории доступа identifier.ACCGROUP_ID --> {identifier.ACCGROUP_ID}");
                                Logger.Log<ParsecService>("Warning", $"292 Формирую промежуточную иерархию категорий доступа");

                                var arrayInheritedAccessGroups = integServ.GetInheritedAccessGroups(ClientState.SessionID, identifier.ACCGROUP_ID);
                                var inheritedAccessGroups = (arrayInheritedAccessGroups == null
                                    ? new List<Guid>()
                                    : arrayInheritedAccessGroups.ToList());

                                if (inheritedAccessGroups.Count == 0)
                                    inheritedAccessGroups.Add(identifier.ACCGROUP_ID);

                                if (accesGroup != null && accesGroup.ID != Guid.Empty)
                                    inheritedAccessGroups.Add(accesGroup.ID);

                                inheritedAccessGroups = inheritedAccessGroups.Distinct().ToList();

                                Logger.Log<ParsecService>("Warning", $"169 Добавлена новая группа доступа {inheritedAccessGroups.Count}");

                                var resCheckAccessGroups = CheckAccessGroups(inheritedAccessGroups);

                                Logger.Log<ParsecService>("Warning", $"173 Результат поиска группы доступа с такими же вложенными группами доступа {resCheckAccessGroups}");

                                if (resCheckAccessGroups != Guid.Empty)
                                {
                                    creatingItem.ACCGROUP_ID = resCheckAccessGroups;
                                }
                                else
                                {
                                    var schedules = integServ.GetAccessSchedules(ClientState.SessionID);

                                    var newNameAccessGroup = string.Empty;
                                    inheritedAccessGroups.ForEach(x =>
                                    {
                                        var ag = GetAccessGroups(x);
                                        if (ag != null && !string.IsNullOrEmpty(ag.NAME))
                                            newNameAccessGroup += $"{ag.NAME} ";
                                    });

                                    if (string.IsNullOrWhiteSpace(newNameAccessGroup))
                                        newNameAccessGroup = "(Особая) Artsec";

                                    var resCreateAccessGroup = integServ.CreateAccessGroup(
                                        ClientState.SessionID,
                                        newNameAccessGroup,
                                        schedules[0].ID,
                                        null);

                                    if (resCreateAccessGroup.Result != ClientState.Result_Success)
                                    {
                                        var errorDesc = $"292 Ошибка CreateAccessGroup: {resCreateAccessGroup.ErrorMessage}";
                                        Logger.Log<ParsecService>("Error", errorDesc);
                                        state.desc = errorDesc;
                                        state.IdCardindev = row.ID;
                                        state.Operation = StateService.GetOperationName(row.OPERATION);
                                        state.OperationCode = row.OPERATION;
                                        state.Status = "ERR";
                                        state.ErrorMessage = resCreateAccessGroup.ErrorMessage;
                                        state.Attempts = row.ATTEMPS;
                                        state.Timestamp = DateTime.Now;
                                        state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                                        state.ErrorCode = 8;
                                        state.keyNum = Key.keyNumber;
                                        return state;
                                    }

                                    var rGuid = resCreateAccessGroup.Value;
                                    integServ.SetInheritedAccessGroups(ClientState.SessionID, rGuid, inheritedAccessGroups.ToArray());
                                    creatingItem.ACCGROUP_ID = rGuid;
                                }

                                creatingItem.IS_PRIMARY = true;
                                creatingItem.CODE = hexValue;
                            }
                        }
                        else
                        {
                            if (!Guid.Empty.Equals(accesGroup.ID))
                                creatingItem.ACCGROUP_ID = accesGroup.ID;

                            creatingItem.IS_PRIMARY = true;
                            creatingItem.CODE = hexValue;
                        }

                        Logger.Log<ParsecService>("Error", $"246 вызывад метод AddPersonIdentifier с параметрами: " +
                            $"ACCGROUP_ID = {creatingItem.ACCGROUP_ID}" +
                            $"PRIVILEGE_MASK = {creatingItem.PRIVILEGE_MASK}" +
                            $"IDENTIFTYPE = {creatingItem.IDENTIFTYPE}" +
                            $"NAME = {creatingItem.NAME}" +
                            $"CODE = {creatingItem.CODE}" +
                            $"PERSON_ID = {creatingItem.PERSON_ID}" +
                            $"IS_PRIMARY = {creatingItem.IS_PRIMARY}"
                            );

                        var resAddPersonIdentifier = integServ.AddPersonIdentifier(_editSessionID, creatingItem);
                        Logger.Log<ParsecService>("Error", $"252 Результа выполнения AddPersonIdentifier: {resAddPersonIdentifier.Result}.");
                        if (resAddPersonIdentifier.Result != ClientState.Result_Success)
                        {
                            var errorDesc = $"232 Ошибка при добавлении группы доступа пользователю. Ошибка: {resAddPersonIdentifier.ErrorMessage}";
                            Logger.Log<ParsecService>("Error", errorDesc);
                            state.desc = errorDesc;
                            state.IdCardindev = row.ID;
                            state.Operation = StateService.GetOperationName(row.OPERATION);
                            state.OperationCode = row.OPERATION;
                            state.Status = "ERR";
                            state.ErrorMessage = resAddPersonIdentifier.ErrorMessage;
                            state.Attempts = row.ATTEMPS;
                            state.Timestamp = DateTime.Now;
                            state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                            state.ErrorCode = 2;
                            state.keyNum = Key.keyNumber;
                            return state;
                        }

                        Logger.Log<ParsecService>("Warning", $"238 Гурппа доступа успешно добавлена | " +
                           $"code: {row.ID_CARD} (hex: {hexValue}) " +
                           $"Пользователю ФИО (parsec): {person.FIRST_NAME} {person.MIDDLE_NAME} {person.LAST_NAME}");

                        state.desc = "Операция выполнена успешно";
                        state.IdCardindev = row.ID;
                        state.Operation = StateService.GetOperationName(row.OPERATION);
                        state.OperationCode = row.OPERATION;
                        state.Status = "OK";
                        state.Attempts = row.ATTEMPS;
                        state.Timestamp = DateTime.Now;
                        state.keyNum = Key.keyNumber;
                        return state;
                    }
                    else
                    {

                    }
                    {
                        state.desc = "Пользователь не найден в parsec, задача удалена";
                        state.IdCardindev = row.ID;
                        state.Operation = StateService.GetOperationName(row.OPERATION);
                        state.OperationCode = row.OPERATION;
                        state.Status = "OK";
                        state.Attempts = row.ATTEMPS;
                        state.Timestamp = DateTime.Now;
                        state.keyNum = Key.keyNumber;
                        Logger.Log<ParsecService>("Warning", $"248 Пользователь с GUID: {model.GUID_PEP} не найден в parsec. Задача {row.ID} удалена");
                        return state;
                    }
                }
                catch (Exception ex)
                {
                    DatabaseService.IncrementAttemp(row);
                    var errorDesc = $"255 Ошибка в AddIdentifierPeople (внутренний catch): {ex.Message}";
                    Logger.Log<ParsecService>("Warning", $"{errorDesc} | {ex.Source} | {ex.StackTrace} | {ex.Data}");
                    state.desc = errorDesc;
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "ERR";
                    state.ErrorMessage = ex.Message?.Replace("\r\n", " ") ?? "Unknown error";
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                    state.ErrorCode = 9;
                    state.keyNum = Key.keyNumber;
                    return state;
                }

            }
            catch (Exception ex)
            {
                var errorDesc = $"263 Ошибка в AddIdentifierPeople (внешний catch): {ex.Message}";
                DatabaseService.IncrementAttemp(row);
                Logger.Log<ParsecService>("Warning", $"{errorDesc} | {ex.Source} | {ex.StackTrace} | {ex.Data}");
                state.desc = errorDesc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = ex.Message?.Replace("\r\n", " ") ?? "Unknown error";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                state.ErrorCode = 9;
                state.keyNum = Key.keyNumber;
                return state;
            }
        }

        //Удаление идентификатора
        public static State RemoveIdentifierPeople(DbModelRowIDInDev row)
        {
            var state = new State();
            Logger.Log<ParsecService>("Warning", $"658 Start RemoveIdentifierPeople {row.ID}");
            var discBuilder = new StringBuilder();
            Action<string> addDisc = msg =>
            {
                if (string.IsNullOrWhiteSpace(msg))
                    return;
                if (discBuilder.Length > 0)
                    discBuilder.Append(" | ");
                discBuilder.Append(msg);
            };
            addDisc("type=RemoveIdentifierPeople");
            addDisc($"cardindev={row.ID}");
            addDisc($"operation={row.OPERATION}");
            var integServ = new IntegrationService();

            var query = "select c.id_card, an.guid, p.guid as people_guid, " +
                 "p.tabnum, p.name, p.patronymic, p.surname, c.id_cardtype from card c, " +
                 "accessname an join people p on p.id_pep = c.id_pep " +
                 $"where c.id_pep = {row.ID_PEP}" +
                 $"and an.id_accessname = {row.ID_CARD}";

            var model = DatabaseService.Get<DbModelAddIdentifier>(query);

            if (int.Parse(model.CARDTYPE) != 1)
            {
                var desc = $"688 Интегратор не обрабатывает идентификаторы с {model.CARDTYPE}. Обработка прерывается";
                addDisc(desc);
                Logger.Log<ParsecService>("Error", desc);
                state.desc = desc;
                state.Status = "ERR";
                state.ErrorMessage = desc;
                state.IdCardindev = row.ID;
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                state.ErrorCode = 3;
                state.keyNum = Key.keyNumber;
                Logger.Log<ParsecService>("Warning", $"700 Stop RemoveIdentifierPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                return state;
            }

            if (model == null)
            {
                var desc = "Контакт не найден в базе данных СКУД";
                var errorMessage = $"Ошибка БД: контакт с ID={row.ID_PEP} не найден в базе СКУД";
                addDisc(desc);
                Logger.Log<ParsecService>("Error", desc);
                state.desc = desc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = errorMessage;
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                state.ErrorCode = 10;
                state.keyNum = Key.keyNumber;
                Logger.Log<ParsecService>("Warning", $"701 Stop RemoveIdentifierPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                return state;
            }
            if (model.CODE == null)
            {
                var desc = "284 В результате запроса к базе данных не было получено данных";
                var errorMessage = $"707 Ошибка БД: отсутствует код карты (CODE=null) для контакта ID={row.ID_PEP}";
                addDisc(desc);
                Logger.Log<ParsecService>("Warning", desc);
                state.desc = desc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = errorMessage;
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                state.ErrorCode = 4;
                state.keyNum = Key.keyNumber;
                Logger.Log<ParsecService>("Warning", $"701 Stop RemoveIdentifierPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                return state;
            }

            if (model.GUID_PEP == null || model.GUID_PEP == String.Empty)
            {
                var desc = "290 GUID_PEP null or empty";
                var errorMessage = $"Ошибка БД: поле GUID_PEP не заполнено для контакта ID={row.ID_PEP}";
                addDisc(desc);
                Logger.Log<ParsecService>("Warning", desc);
                state.desc = desc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = errorMessage;
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                state.ErrorCode = 11;
                state.keyNum = Key.keyNumber;
                Logger.Log<ParsecService>("Warning", $"701 Stop RemoveIdentifierPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                return state;
            }

            string hexValue = Convert.ToInt64(model.CODE).ToString("X8");
            addDisc($"model_ok_guidPep={model.GUID_PEP}");
            addDisc($"card_hex={hexValue}");

            Logger.Log<ParsecService>("Warning",
               $"299 Удаление идентификатора | {model.CODE} ({hexValue})");

            var person = integServ.GetPerson(ClientState.SessionID, new Guid(model.GUID_PEP));
            addDisc("after_get_person");

            if (person == null)
            {
                var desc = $"590 RemoveIdentifierPeople: человека {model.SURNAME} {model.NAME} {model.PATRONYMIC} нет в базе данных Парсек";
                var errorMessage = $"Ошибка Parsec: пользователь с GUID {model.GUID_PEP} ({model.SURNAME} {model.NAME} {model.PATRONYMIC}) не найден в базе данных";
                state.desc = desc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.Status = "ERR";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.OperationCode = row.OPERATION;
                state.ErrorMessage = errorMessage;
                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                state.ErrorCode = 12;
                state.keyNum = Key.keyNumber;
                Logger.Log<ParsecService>("Error", $"DEBUG: state.desc = '{state.desc}'");
                Logger.Log<ParsecService>("Error", $"DEBUG: state.ErrorMessage = '{state.ErrorMessage}'");
                Logger.Log<ParsecService>("Error", $"DEBUG: state.ToString() = {state.ToString()}");
                addDisc(desc);
                Logger.Log<ParsecService>("Error", state.ToString());
                Logger.Log<ParsecService>("Warning", $"701 Stop RemoveIdentifierPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                return state;
            }

            var res = integServ.OpenPersonEditingSession(ClientState.SessionID, person.ID);

            if (res.Result != ClientState.Result_Success)
            {
                var desc = $"315 Ошибка открытия сессии для редактирования пользователя. Ошибка {res.ErrorMessage}";
                addDisc(desc);
                Logger.Log<ParsecService>("Error", desc);
                state.desc = desc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = res.ErrorMessage;
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                state.ErrorCode = 7;
                state.keyNum = Key.keyNumber;
                Logger.Log<ParsecService>("Warning", $"701 Stop RemoveIdentifierPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                return state;
            }

            var _editSessionID = res.Value;
            addDisc("editing_session_opened");

            var identifiers = integServ.GetPersonIdentifiers(ClientState.SessionID, new Guid(model.GUID_PEP));
            addDisc("after_get_identifiers");

            if (identifiers != null)
            {
                for (int i = 0; i < identifiers.Length; i++)
                {
                    Logger.Log<ParsecService>("Info", $"326 Идентификатор[{i}]: {Newtonsoft.Json.JsonConvert.SerializeObject(identifiers[i])}");
                }
            }

            if (identifiers == null)
            {
                var desc = $"У {model.SURNAME} {model.NAME} {model.PATRONYMIC} {model.TAB_NUM_PEP} карты нет. Удалить категорию доступа не могу. RemoveIdentifierPeople: GetPersonIdentifiers returned null for GUID_PEP={model.GUID_PEP}";
                var errorMessage = $"Ошибка API Parsec: GetPersonIdentifiers вернул null для GUID_PEP={model.GUID_PEP}";
                addDisc(desc);
                Logger.Log<ParsecService>("Error", desc);
                state.desc = desc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = errorMessage;
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                state.ErrorCode = 13;
                state.keyNum = Key.keyNumber;
                Logger.Log<ParsecService>("Warning", $"701 Stop RemoveIdentifierPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                return state;
            }

            Logger.Log<ParsecService>("Warning", $"327 Получены идентификаторы у человека в количестве {identifiers.Length}");

            var identifier = identifiers.FirstOrDefault(x => x.CODE == hexValue);

            if (identifier == null)
            {
                var desc = $"RemoveIdentifierPeople: Не найден идентификатор с кодом {hexValue} у пользователя {model.GUID_PEP}";
                var errorMessage = $"Ошибка: идентификатор с кодом {hexValue} не найден у пользователя GUID_PEP={model.GUID_PEP}";
                addDisc(desc);
                Logger.Log<ParsecService>("Error", desc);
                state.desc = desc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = errorMessage;
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                state.ErrorCode = 14;
                state.keyNum = Key.keyNumber;
                Logger.Log<ParsecService>("Warning", $"701 Stop RemoveIdentifierPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                return state;
            }

            Logger.Log<ParsecService>("Info", $"331 Найден идентификатор: {Newtonsoft.Json.JsonConvert.SerializeObject(identifier)}");
            addDisc($"identifier_found={identifier.CODE}");

            Logger.Log<ParsecService>("Warning", $"332 Идентификатор с указанным кодом:  {hexValue} | {identifier.CODE}");

            Logger.Log<ParsecService>("Info", $"484 RemoveeIdentifiersPeople identifier: {Newtonsoft.Json.JsonConvert.SerializeObject(identifier)}");
            Logger.Log<ParsecService>("Info", $"486 ACCGROUP_ID перед вызовом GetInheritedAccessGroups: {identifier.ACCGROUP_ID}");

            if (identifier.ACCGROUP_ID == Guid.Empty || identifier.ACCGROUP_ID.ToString() == "00000000-0000-0000-0000-000000000000")
            {
                var desc = $"412 У идентификатора {hexValue} нет привязанной группы доступа (ACCGROUP_ID пустой)";
                addDisc(desc);
                Logger.Log<ParsecService>("Warning", desc);
                var stateSkip = new State();
                stateSkip.desc = desc;
                stateSkip.IdCardindev = row.ID;
                stateSkip.Operation = StateService.GetOperationName(row.OPERATION);
                stateSkip.OperationCode = row.OPERATION;
                stateSkip.Status = "OK";
                stateSkip.Attempts = row.ATTEMPS;
                stateSkip.Timestamp = DateTime.Now;
                stateSkip.ErrorCode = 15;
                state.keyNum = Key.keyNumber;
                return stateSkip;
            }

            var arrayInheritedAccessGroups = integServ.GetInheritedAccessGroups(ClientState.SessionID, identifier.ACCGROUP_ID);

            Logger.Log<ParsecService>("Info", $"487 RemoveeIdentifiersPeople: {Newtonsoft.Json.JsonConvert.SerializeObject(arrayInheritedAccessGroups)}");

            try
            {
                if (arrayInheritedAccessGroups == null)
                {
                    var desc = $"RemoveIdentifierPeople: база данных вернула пустоту для ACCGROUP_ID={identifier.ACCGROUP_ID}";
                    var errorMessage = $"Ошибка API Parsec: GetInheritedAccessGroups вернул null для ACCGROUP_ID={identifier.ACCGROUP_ID}";
                    addDisc(desc);
                    Logger.Log<ParsecService>("Error", desc);
                    state.desc = desc;
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "ERR";
                    state.ErrorMessage = errorMessage;
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                    state.ErrorCode = 16;
                    state.keyNum = Key.keyNumber;
                    Logger.Log<ParsecService>("Warning", $"701 Stop RemoveIdentifierPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                    return state;
                }

                var inheritedAccessGroups = arrayInheritedAccessGroups.ToList();

                Logger.Log<ParsecService>("Info", $"492 RemoveeIdentifiersPeople: {Newtonsoft.Json.JsonConvert.SerializeObject(inheritedAccessGroups)}");

                if (identifier.ACCGROUP_ID == Guid.Empty || identifier.ACCGROUP_ID.ToString() == "00000000-0000-0000-0000-000000000000")
                {
                    var desc = $"412 У идентификатора {hexValue} нет привязанной группы доступа (ACCGROUP_ID пустой)";
                    addDisc(desc);
                    Logger.Log<ParsecService>("Warning", desc);
                    state.desc = desc;
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "OK";
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.keyNum = Key.keyNumber;
                    return state;
                }

                inheritedAccessGroups.Remove(new Guid(model.GUID_ACCESS_GROUP));

                Logger.Log<ParsecService>("Info", $"497 Идентификаторы после обновления: {Newtonsoft.Json.JsonConvert.SerializeObject(identifiers)}");

                var creatingItem = new Identifier();
                var personGuid = new Guid(model.GUID_PEP);

                if (inheritedAccessGroups.Count == 0)
                {
                    creatingItem = new Identifier()
                    {
                        ACCGROUP_ID = Guid.Empty,
                        IS_PRIMARY = true,
                        CODE = hexValue,
                        PERSON_ID = personGuid,
                    };
                }
                else if (inheritedAccessGroups.Count == 1)
                {
                    var accessGroupId = inheritedAccessGroups[0];

                    creatingItem = new Identifier()
                    {
                        ACCGROUP_ID = accessGroupId,
                        IS_PRIMARY = true,
                        CODE = hexValue,
                        PERSON_ID = personGuid,
                    };
                }
                else
                {
                    var resCheckAccessGroups = CheckAccessGroups(inheritedAccessGroups);

                    Logger.Log<ParsecService>("Warning", $"356 Результат поиска группы доступа с такими же вложенными группами доступа " +
                        $"{resCheckAccessGroups}");

                    if (resCheckAccessGroups != Guid.Empty)
                    {
                        creatingItem = new Identifier()
                        {
                            ACCGROUP_ID = resCheckAccessGroups,
                            IS_PRIMARY = true,
                            CODE = hexValue,
                            PERSON_ID = personGuid,
                        };
                    }
                    else
                    {
                        var schedules = integServ.GetAccessSchedules(ClientState.SessionID);

                        var resCreateAccessGroup = integServ.CreateAccessGroup(ClientState.SessionID,
                            "(Особая) Artsec", schedules[0].ID, null);

                        if (resCreateAccessGroup.Result != ClientState.Result_Success)
                        {
                            var errorDesc = $"292 Ошибка CreateAccessGroup: {resCreateAccessGroup.ErrorMessage}";
                            Console.WriteLine(resCreateAccessGroup.ErrorMessage);
                            Logger.Log<ParsecService>("Error", errorDesc);
                            state.desc = errorDesc;
                            state.IdCardindev = row.ID;
                            state.Operation = StateService.GetOperationName(row.OPERATION);
                            state.OperationCode = row.OPERATION;
                            state.Status = "ERR";
                            state.ErrorMessage = resCreateAccessGroup.ErrorMessage;
                            state.Attempts = row.ATTEMPS;
                            state.Timestamp = DateTime.Now;
                            state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                            state.ErrorCode = 8;
                            state.keyNum = Key.keyNumber;
                            return state;
                        }

                        var rGuid = resCreateAccessGroup.Value;

                        var resInerited = integServ.SetInheritedAccessGroups(ClientState.SessionID, rGuid, inheritedAccessGroups.ToArray());
                        creatingItem = new Identifier()
                        {
                            ACCGROUP_ID = rGuid,
                            IS_PRIMARY = true,
                            CODE = hexValue,
                            PERSON_ID = personGuid
                        };
                    }
                }

                Logger.Log<ParsecService>("Info", $"546 RemoveeIdentifiersPeople creatingItem: {Newtonsoft.Json.JsonConvert.SerializeObject(creatingItem)}");

                var resAddPersonIdentifier = integServ.AddPersonIdentifier(_editSessionID, creatingItem);
                if (resAddPersonIdentifier.Result != ClientState.Result_Success)
                {
                    var errorDesc = $"398 Ошибка при добавлении группы доступа пользователю. Ошибка: {resAddPersonIdentifier.ErrorMessage}";
                    Logger.Log<ParsecService>("Error", errorDesc);
                    state.desc = errorDesc;
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "ERR";
                    state.ErrorMessage = resAddPersonIdentifier.ErrorMessage;
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                    state.ErrorCode = 2;
                    state.keyNum = Key.keyNumber;
                    return state;
                }

                Logger.Log<ParsecService>("Warning", $"404 Гурппа доступа успешно добавлена | " +
                   $"code: {row.ID_CARD} (hex: {hexValue}) " +
                   $"Пользователю ФИО (parsec): {person.FIRST_NAME} {person.MIDDLE_NAME} {person.LAST_NAME}");

                state.desc = "Операция выполнена успешно";
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "OK";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.keyNum = Key.keyNumber;
                return state;
            }
            catch (Exception ex)
            {
                var errorDesc = $"413 Ошибка в RemoveIdentifierPeople: {ex.Message}";
                Logger.Log<ParsecService>("Warning", $"{errorDesc} | {ex.Source} | {ex.StackTrace} | {ex.Data}");
                state.desc = errorDesc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = ex.Message?.Replace("\r\n", " ") ?? "Unknown error";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                state.ErrorCode = 17;
                state.keyNum = Key.keyNumber;
                return state;
            }
        }

        public static State AddPeople(DbModelRowIDInDev row)
        {
            var state = new State();
            Logger.Log<ParsecService>("Error", $"537 start AddPeople {row.ID}");
            string komuName = null;
            string orgName = null;
            try
            {
                var query = "select p.id_pep, p.guid as guid_pep, " +
                    "o.guid as guid_org, p.name, p.surname, p.patronymic, p.tabnum, o.name as org_name " +
                    "from people p " +
                    "left join organization o on p.id_org=o.id_org " +
                    $"where p.id_pep={row.ID_PEP};";
                Logger.Log<ParsecService>("Error", $"528 {query}");

                var people = DatabaseService.Get<DbModelAddPeople>(query);
                komuName = DatabaseService.GetString(
    $"select coalesce(p.surname,'') || ' ' || coalesce(p.name,'') || ' ' || coalesce(p.patronymic,'') from people p where p.id_pep = {people.ID_PEP}");

                if (people != null)
                {
                    var person = new Person()
                    {
                        ID = new Guid(people.GUID_PEP),
                        FIRST_NAME = people.NAME,
                        LAST_NAME = people.SURNAME,
                        MIDDLE_NAME = people.PATRONYMIC,
                        TAB_NUM = people.TABNUM,
                        ORG_ID = new Guid(people.GUID_ORG),
                    };

                    Logger.Log<ParsecService>("Info", $"554 Добавляется сотрудник {Newtonsoft.Json.JsonConvert.SerializeObject(person)}");

                    if (CheckGuidePresent(person.ID))
                    {
                        state.ErrorCode = 18;
                        var desc = $"577 КОД ОШИБКИ: {state.ErrorCode}. Уже имеется сотрудник  с GUID {person.ID}. Работаю завершаю.";
                        var errorMessage = $" 1134 Ошибка: сотрудник с GUID {person.ID} уже существует в Parsec";
                        Logger.Log<ParsecService>("Warning", desc);
                        state.desc = desc;
                        state.IdCardindev = row.ID;
                        state.Operation = StateService.GetOperationName(row.OPERATION);
                        state.OperationCode = row.OPERATION;
                        state.Status = "ERR";
                        state.ErrorMessage = errorMessage;
                        state.Attempts = row.ATTEMPS;
                        state.Timestamp = DateTime.Now;
                        state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                        state.keyNum = Key.keyNumber;
                        Logger.Log<ParsecService>("Error", $"1148 stop AddPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                        return state;
                    }
                    Logger.Log<ParsecService>("Warning", $"583 Сотрудник  с GUID {person.ID} (ФИО: {komuName}) нет в Парсек. Продолжаю добавление сотрудника.");

                    if (!CheckGuidePresent(person.ORG_ID))
                    {
                        var desc = $"560 НЕ существует организация {people.ORG_NAME} с указанным GUID {person.ORG_ID} в Парсек. Работаю завершаю.";
                        var errorMessage = $"1158 Ошибка: организация не найдена в Parsec (GUID: {person.ORG_ID})";
                        Logger.Log<ParsecService>("Warning", desc);
                        state.desc = desc;
                        state.IdCardindev = row.ID;
                        state.Operation = StateService.GetOperationName(row.OPERATION);
                        state.OperationCode = row.OPERATION;
                        state.Status = "ERR";
                        state.ErrorMessage = errorMessage;
                        state.Attempts = row.ATTEMPS;
                        state.Timestamp = DateTime.Now;
                        state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                        state.ErrorCode = 19;
                        state.keyNum = Key.keyNumber;
                        Logger.Log<ParsecService>("Error", $"1172 stop AddPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                        return state;
                    }
                    Logger.Log<ParsecService>("Info", $"1176 Существует организаци с указанным  GUID {Newtonsoft.Json.JsonConvert.SerializeObject(person)}. Продолжаю добавление сотрудника.");

                    var integServ = new IntegrationService();
                    var res = integServ.CreatePerson(ClientState.SessionID, person);

                    if (res.Result != ClientState.Result_Success)
                    {
                        Logger.Log<ParsecService>("Error", $"605 не смог вставить сотрудника {Newtonsoft.Json.JsonConvert.SerializeObject(person)}");
                        Logger.Log<ParsecService>("Error", $"606 {res.ErrorMessage}");
                        state.desc = $"Ошибка при добавлении сотрудника: {res.ErrorMessage}";
                        state.IdCardindev = row.ID;
                        state.Operation = StateService.GetOperationName(row.OPERATION);
                        state.OperationCode = row.OPERATION;
                        state.Status = "ERR";
                        state.ErrorMessage = res.ErrorMessage;
                        state.Attempts = row.ATTEMPS;
                        state.Timestamp = DateTime.Now;
                        state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                        state.ErrorCode = 1;
                        state.keyNum = Key.keyNumber;
                        Logger.Log<ParsecService>("Error", $"1199 stop AddPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                        return state;
                    }

                    Logger.Log<ParsecService>("Info", $"466 Пользователь добавлен успешно {Newtonsoft.Json.JsonConvert.SerializeObject(person)}");
                    state.desc = "Пользователь добавлен успешно";
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "OK";
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.keyNum = Key.keyNumber;
                    Logger.Log<ParsecService>("Error", $"1215 stop AddPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                    return state;
                }

                var descNotFound = $"475 Пользователь с {row.ID_PEP} не найден в базе СКУД Артонит.";
                Logger.Log<ParsecService>("Warning", descNotFound);
                state.desc = descNotFound;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = $"Ошибка БД: пользователь с ID_PEP={row.ID_PEP} не найден в базе СКУД";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                state.ErrorCode = 10;
                state.keyNum = Key.keyNumber;
                Logger.Log<ParsecService>("Error", $"1232 stop AddPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                return state;
            }
            catch (Exception ex)
            {
                var errorDesc = $"1231 Пользователя ID_pep={row.ID_PEP} нет в базе данных СКУД";
                Logger.Log<ParsecService>("Warning", errorDesc);
                state.desc = errorDesc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                var cleanErrorMessage = string.Join(" ", ex.Message.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
                state.ErrorMessage = cleanErrorMessage;
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                state.ErrorCode = 10;
                state.keyNum = Key.keyNumber;
                Logger.Log<ParsecService>("Error", $"1252 stop AddPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                return state;
            }
        }

        public static State RemovePeople(DbModelRowIDInDev row)
        {
            var state = new State();
            Logger.Log<ParsecService>("Error", $"608 start RemovePeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
            DatabaseService.IncrementAttemp(row);
            try
            {
                if (!CheckGuidePresent(new Guid(row.ID_CARD)))
                {
                    var desc = $"629 сотрудник отсутвует в Парсек {Newtonsoft.Json.JsonConvert.SerializeObject(row)}. Команда по удалению выполнена успешно.";
                    Logger.Log<ParsecService>("Info", desc);
                    state.desc = desc;
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "OK";
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.keyNum = Key.keyNumber;
                    Logger.Log<ParsecService>("Error", $"1281 stop RemovePeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                    return state;
                }
                Logger.Log<ParsecService>("Info",
                       $"629 сотрудник {Newtonsoft.Json.JsonConvert.SerializeObject(row)} присутвует в Парсек, продолжаю удаление сотрудника.");

                var integServ = new IntegrationService();

                var res = integServ.DeletePerson(ClientState.SessionID, new Guid(row.ID_CARD));
                string json = JsonConvert.SerializeObject(res);
                Logger.Log<ParsecService>("Error", $"1358 РЕЗУЛЬТАТ УДАЛЕНИЯ {json}");

                if (res.Result == ClientState.Result_Success)
                {
                    Logger.Log<ParsecService>("Info",
                 $"508 пользователь успешно удален |" +
                 $"{Newtonsoft.Json.JsonConvert.SerializeObject(row)}");
                    var state2 = new State();
                    state2.desc = "Пользователь успешно удален";
                    state2.IdCardindev = row.ID;
                    state2.Operation = StateService.GetOperationName(row.OPERATION);
                    state2.OperationCode = row.OPERATION;
                    state2.Status = "OK";
                    state2.Attempts = row.ATTEMPS;
                    state2.Timestamp = DateTime.Now;
                    state.keyNum = Key.keyNumber;
                    Logger.Log<ParsecService>("Error", $"1328 stop RemovePeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                    return state2;
                }
                else
                {
                    if (res.ErrorMessage.Contains("Could not delete person. There is no person with id"))
                    {
                        var descForThis = "1311 удаление человека произошло успешно. Человека не было в Парсек";
                        Logger.Log<ParsecService>("Info", descForThis);

                        state.desc = descForThis;
                        state.IdCardindev = row.ID;
                        state.Operation = StateService.GetOperationName(row.OPERATION);
                        state.OperationCode = row.OPERATION;
                        state.Status = "OK";
                        state.ErrorMessage = null;
                        state.Attempts = row.ATTEMPS;
                        state.Timestamp = DateTime.Now;
                        state.keyNum = Key.keyNumber;
                        return state;
                    }
                    state.ErrorCode = 20;
                    Logger.Log<ParsecService>("Error", $"498 КОД ОШИБКИ: {state.ErrorCode} Ошибка при удалении пользователя. " +
                        $"Ошибка: {res.ErrorMessage}");
                    state.desc = $"1300 Ошибка при удалении пользователя: {res.ErrorMessage}";
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "ERR";
                    state.ErrorMessage = res.ErrorMessage;
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                    state.keyNum = Key.keyNumber;
                    Logger.Log<ParsecService>("Error", $"1690 stop RemovePeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                    return state;
                }
            }
            catch (Exception ex)
            {
                state.ErrorCode = 20;
                var errorDesc = $"552 КОД ОШИБКИ: {state.ErrorCode}. Ошибка в RemovePeople: {ex.Message}";
                Logger.Log<ParsecService>("Warning", errorDesc);
                state.desc = errorDesc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = ex.Message?.Replace("\r\n", " ") ?? "Unknown error";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                state.keyNum = Key.keyNumber;
                Logger.Log<ParsecService>("Error", $"1346 stop RemovePeople {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                throw;
            }
        }

        public static State AddOrg(DbModelRowIDInDev row)
        {
            var state = new State();
            Logger.Log<ParsecService>("Error", $"1353 start AddOrg {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
            try
            {
                var query = "select o.guid as guide_for_add, " +
                    "o2.guid as guid_for_parent, o.name, o.divcode, o.id_org from organization o " +
                    "join organization o2 on o2.id_org=o.id_parent " +
                    $"where o.guid='{row.ID_CARD}'";

                var model = DatabaseService.Get<DbModelAddOrg>(query);

                Logger.Log<ParsecService>("Warning", $"533 Добавление организации |" +
                    $" NAME: {model.NAME} DIVCODE: {model.DIVCODE}");

                if (model.NAME == null)
                {
                    state.ErrorCode = 21;
                    var desc = $"541 Организация {row.ID_CARD} не найдена";
                    var errorMessage = $"1376 КОД ОШИБКИ: {state.ErrorCode}. Ошибка БД: организация с GUID={row.ID_CARD} не найдена в базе СКУД";
                    Logger.Log<ParsecService>("Error", desc);
                    state.desc = desc;
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "ERR";
                    state.ErrorMessage = errorMessage;
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                    state.keyNum = Key.keyNumber;
                    Logger.Log<ParsecService>("Error", $"1388 stop AddOrg {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                    return state;
                }

                var integServ = new IntegrationService();

                var org = new OrgUnit()
                {
                    NAME = model.NAME,
                    ID = new Guid(model.GUID),
                    PARENT_ID = new Guid(model.GUID_PARENT),
                    DESC = ""
                };

                var result = integServ.CreateOrgUnit(ClientState.SessionID, org);
                if (result.Result != ClientState.Result_Success)
                {
                    state.ErrorCode = 22;
                    Logger.Log<ParsecService>("Error", $"1572 КОД ОШИБКИ: {state.ErrorCode}. {result.ErrorMessage}");
                    state.desc = $"1408 Ошибка при добавлении организации: {result.ErrorMessage}";
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "ERR";
                    state.ErrorMessage = result.ErrorMessage;
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                    state.keyNum = Key.keyNumber;
                    Logger.Log<ParsecService>("Error", $"1417 stop AddOrg {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                    return state;
                }

                Logger.Log<ParsecService>("Warning", $"564 Организация добавлена успешно: {org.NAME} " +
                    $"| ID: {org.ID} Parent ID: {org.PARENT_ID} " +
                    $"divcode: {model.DIVCODE} IdOrg: {model.ID_ORG}");
                state.desc = "1426 Организация добавлена успешно";
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "OK";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.keyNum = Key.keyNumber;
                Logger.Log<ParsecService>("Error", $"1434 stop AddOrg {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                return state;
            }
            catch (Exception ex)
            {
                state.ErrorCode = 22;
                var errorDesc = $"610 КОД ОШИБКИ: {state.ErrorCode}. Ошибка в AddOrg: {ex.Message}";
                Logger.Log<ParsecService>("Warning", errorDesc);
                state.desc = errorDesc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = ex.Message?.Replace("\r\n", " ") ?? "Unknown error";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                state.keyNum = Key.keyNumber;
                Logger.Log<ParsecService>("Error", $"1451 stop AddOrg {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                throw;
            }
        }

        public static State RemoveOrg(DbModelRowIDInDev row)
        {
            var state = new State();
            Logger.Log<ParsecService>("Error", $"1458 start RemoveOrg {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
            try
            {
                var integServ = new IntegrationService();

                Logger.Log<ParsecService>("Warning",
                   $"584 Удаление организации | {row.ID_CARD}");

                var res = integServ.DeleteOrgUnit(ClientState.SessionID, new Guid(row.ID_CARD));
                if (res.Result != ClientState.Result_Success)
                {
                    state.ErrorCode = 22;
                    Logger.Log<ParsecService>("Error", $"1642 КОД ОШИБКИ: {state.ErrorCode} res.ErrorMessage");
                    state.desc = $"1473 Ошибка при удалении организации: {res.ErrorMessage}";
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "ERR";
                    state.ErrorMessage = res.ErrorMessage;
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                    state.keyNum = Key.keyNumber;
                    Logger.Log<ParsecService>("Error", $"1481 stop RemoveOrg {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                    return state;
                }

                Logger.Log<ParsecService>("Warning",
                    $"595 Организация успешно удалена | {row.ID_CARD}");
                state.desc = "1490 Организация успешно удалена";
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "OK";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.keyNum = Key.keyNumber;
                Logger.Log<ParsecService>("Error", $"1496 stop RemoveOrg {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                return state;
            }
            catch (Exception ex)
            {
                state.ErrorCode = 22;
                var errorDesc = $"638 Ошибка в RemoveOrg: {ex.Message}";
                Logger.Log<ParsecService>("Warning", $"1677 КОД ОШИБКИ: {state.ErrorCode}. { errorDesc}");
                state.desc = errorDesc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = ex.Message?.Replace("\r\n", " ") ?? "Unknown error";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                state.keyNum = Key.keyNumber;
                Logger.Log<ParsecService>("Error", $"1515 stop RemoveOrg {Newtonsoft.Json.JsonConvert.SerializeObject(row.ID)}");
                throw;
            }
        }

        public static State AddCardPeople(DbModelRowIDInDev row)
        {
            var state = new State();
            Logger.Log<ParsecService>("Error", $"1434 stop AddCardPeople {Newtonsoft.Json.JsonConvert.SerializeObject(row)}");
            try
            {
                var query = "select p.guid, p.tabnum, p.name, p.patronymic, " +
                    $"p.surname from people p where p.id_pep = {row.ID_PEP}";

                var list = DatabaseService.GetList<DbModelAddCard>(query);

                foreach (var model in list)
                {
                    try
                    {
                        if (model.GUID_PEP == null || model.GUID_PEP == String.Empty)
                        {
                            state.ErrorCode = 23;
                            var errorDesc = $"621 КОД ОШИБКИ: {state.ErrorCode}. GUID_PEP null or empty для контакта ID={row.ID_PEP}";
                            Logger.Log<ParsecService>("Warning", errorDesc);
                            state.desc = errorDesc;
                            state.IdCardindev = row.ID;
                            state.Operation = StateService.GetOperationName(row.OPERATION);
                            state.OperationCode = row.OPERATION;
                            state.Status = "ERR";
                            state.ErrorMessage = $"Ошибка БД: поле GUID_PEP не заполнено для контакта ID={row.ID_PEP}";
                            state.Attempts = row.ATTEMPS;
                            state.Timestamp = DateTime.Now;
                            state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                            state.keyNum = Key.keyNumber;
                            StateService.SaveState(state);
                            DatabaseService.IncrementAttemp(row);
                            return state;
                        }
                        string hexValue = Convert.ToInt64(row.ID_CARD).ToString("X8");

                        Logger.Log<ParsecService>("Warning", $"627 Добавление карты пользователю |" +
                            $"code: {row.ID_CARD} (hex: {hexValue}) | GUID_PEP = {model.GUID_PEP} " +
                            $"| tab_num = {model.TAB_NUM_PEP} " +
                            $"| ФИО (artsec): {model.SURNAME} {model.NAME} {model.PATRONYMIC}");

                        var integServ = new IntegrationService();

                        var person = integServ.GetPerson(ClientState.SessionID, new Guid(model.GUID_PEP));

                        if (person != null)
                        {
                            var res = integServ.OpenPersonEditingSession(ClientState.SessionID, person.ID);

                            if (res.Result != ClientState.Result_Success)
                            {
                                state.ErrorCode = 7;
                                Logger.Log<ParsecService>("Error", $"642 КОД ОШИБКИ: {state.ErrorCode}. Ошибка открытия сессии для редактирования пользователя. " +
                                    $"Ошибка {res.ErrorMessage}");
                                state.desc = $"Ошибка открытия сессии: {res.ErrorMessage}";
                                state.IdCardindev = row.ID;
                                state.Operation = StateService.GetOperationName(row.OPERATION);
                                state.OperationCode = row.OPERATION;
                                state.Status = "ERR";
                                state.ErrorMessage = res.ErrorMessage;
                                state.Attempts = row.ATTEMPS;
                                state.Timestamp = DateTime.Now;
                                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                                state.keyNum = Key.keyNumber;
                                StateService.SaveState(state);
                                DatabaseService.IncrementAttemp(row);
                                return state;
                            }

                            var _editSessionID = res.Value;

                            var creatingItem = new BaseIdentifier();

                            creatingItem.IS_PRIMARY = true;
                            creatingItem.CODE = hexValue;
                            creatingItem.PERSON_ID = new Guid(model.GUID_PEP);

                            var resAddPersonIdentifier = integServ.AddPersonIdentifier(_editSessionID, creatingItem);
                            if (resAddPersonIdentifier.Result != ClientState.Result_Success)
                            {
                                state.ErrorCode = 24;
                                Logger.Log<ParsecService>("Error", $"659 КОД ОШИБКИ: {state.ErrorCode}. Ошибка при добавлении карты пользователю." +
                                    $"Ошибка: {resAddPersonIdentifier.ErrorMessage}");
                                state.desc = $"Ошибка при добавлении карты: {resAddPersonIdentifier.ErrorMessage}";
                                state.IdCardindev = row.ID;
                                state.Operation = StateService.GetOperationName(row.OPERATION);
                                state.OperationCode = row.OPERATION;
                                state.Status = "ERR";
                                state.ErrorMessage = resAddPersonIdentifier.ErrorMessage;
                                state.Attempts = row.ATTEMPS;
                                state.Timestamp = DateTime.Now;
                                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                                state.keyNum = Key.keyNumber;
                                StateService.SaveState(state);
                                DatabaseService.IncrementAttemp(row);
                                return state;
                            }

                            Logger.Log<ParsecService>("Warning", $"665 Карта успешно добавлена | " +
                                $"code: {row.ID_CARD} (hex: {hexValue}) " +
                                $"Пользователю ФИО (parsec): {person.FIRST_NAME} {person.MIDDLE_NAME} {person.LAST_NAME}");
                            state.desc = "Карта успешно добавлена";
                            state.IdCardindev = row.ID;
                            state.Operation = StateService.GetOperationName(row.OPERATION);
                            state.OperationCode = row.OPERATION;
                            state.Status = "OK";
                            state.Attempts = row.ATTEMPS;
                            state.Timestamp = DateTime.Now;
                            state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                            state.keyNum = Key.keyNumber;
                            StateService.SaveState(state);
                            DatabaseService.DeleteIdInDevById(row.ID);
                            return state;
                        }
                        else
                        {
                            state.ErrorCode = 25;
                            var errorDesc = $"674 КОД ОШИБКИ: {state.ErrorCode}. Пользователь с GUID: {model.GUID_PEP} не найден в parsec.";
                            Logger.Log<ParsecService>("Warning", errorDesc);
                            state.desc = errorDesc;
                            state.IdCardindev = row.ID;
                            state.Operation = StateService.GetOperationName(row.OPERATION);
                            state.OperationCode = row.OPERATION;
                            state.Status = "ERR";
                            state.ErrorMessage = $"Ошибка Parsec: пользователь с GUID {model.GUID_PEP} не найден";
                            state.Attempts = row.ATTEMPS;
                            state.Timestamp = DateTime.Now;
                            state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                            state.keyNum = Key.keyNumber;
                            StateService.SaveState(state);
                            DatabaseService.IncrementAttemp(row);
                            return state;
                        }
                    }
                    catch (Exception ex)
                    {
                        state.ErrorCode = 9;
                        var errorDesc = $"717 КОД ОШИБКИ: {state.ErrorCode}. Ошибка в AddCardPeople (внутренний catch): {ex.Message}";
                        Logger.Log<ParsecService>("Warning", errorDesc);
                        state.desc = errorDesc;
                        state.IdCardindev = row.ID;
                        state.Operation = StateService.GetOperationName(row.OPERATION);
                        state.OperationCode = row.OPERATION;
                        state.Status = "ERR";
                        state.ErrorMessage = ex.Message?.Replace("\r\n", " ") ?? "Unknown error";
                        state.Attempts = row.ATTEMPS;
                        state.Timestamp = DateTime.Now;
                        state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                        state.ErrorCode = 9;
                        state.keyNum = Key.keyNumber;
                        StateService.SaveState(state);
                        DatabaseService.IncrementAttemp(row);
                        return state;
                    }
                }
                state.ErrorCode = 25;
                var errorNoData = $"1847 КОД ОШИБКИ: {state.ErrorCode}. Не найден пользователь с ID_PEP = {row.ID_PEP}";
                Logger.Log<ParsecService>("Warning", errorNoData);
                state.desc = errorNoData;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = $"В БД не найден контакт с ID={row.ID_PEP}";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                state.keyNum = Key.keyNumber;
                StateService.SaveState(state);
                DatabaseService.IncrementAttemp(row);
                return state;
            }
            catch (Exception ex)
            {
                state.ErrorCode = 9;
                var errorDesc = $"724 КОД ОШИБКИ: {state.ErrorCode} Ошибка в AddCardPeople (внешний catch): {ex.Message}";
                Logger.Log<ParsecService>("Warning", errorDesc);
                state.desc = errorDesc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = ex.Message?.Replace("\r\n", " ") ?? "Unknown error";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                state.keyNum = Key.keyNumber;
                StateService.SaveState(state);
                DatabaseService.IncrementAttemp(row);
                return state;
            }
        }

        public static State RemoveCardPeople(DbModelRowIDInDev row)
        {
            var state = new State();
            try
            {
                var integServ = new IntegrationService();

                string hexValue = Convert.ToInt64(row.ID_CARD).ToString("X8");

                Logger.Log<ParsecService>("Warning",
                   $"700 Удаление карты | {row.ID_CARD} (hex: {hexValue})");

                var res = integServ.DeleteIdentifier(ClientState.SessionID, hexValue);
                if (res.Result != ClientState.Result_Success)
                {
                    state.ErrorCode = 26;
                    Logger.Log<ParsecService>("Error", $"КОД ОШИБКИ: {state.ErrorCode}.  {res.ErrorMessage}");
                    state.desc = $"Ошибка при удалении карты: {res.ErrorMessage}";
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "ERR";
                    state.ErrorMessage = res.ErrorMessage;
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                    state.ErrorCode = 26;
                    state.keyNum = Key.keyNumber;
                    StateService.SaveState(state);
                    DatabaseService.IncrementAttemp(row);
                    return state;
                }

                Logger.Log<ParsecService>("Warning",
                    $"711 Карта успешно удалена | {row.ID_CARD} (hex: {hexValue}) ");
                state.desc = "Карта успешно удалена";
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "OK";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.keyNum = Key.keyNumber;
                StateService.SaveState(state);
                DatabaseService.DeleteIdInDevById(row.ID);
                return state;
            }
            catch (Exception ex)
            {
                state.ErrorCode = 26;
                var errorDesc = $"КОД ОШИБКИ: {state.ErrorCode}. 754 Ошибка в RemoveCardPeople: {ex.Message}";
                Logger.Log<ParsecService>("Warning", errorDesc);
                state.desc = errorDesc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = ex.Message?.Replace("\r\n", " ") ?? "Unknown error";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                state.keyNum = Key.keyNumber;
                StateService.SaveState(state);
                DatabaseService.IncrementAttemp(row);
                return state;
            }
        }
    }
}