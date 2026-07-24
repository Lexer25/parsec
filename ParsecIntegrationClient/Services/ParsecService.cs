using Newtonsoft.Json;
using ParsecIntegrationClient.IntegrationWebService;
using ParsecIntegrationClient.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ParsecIntegrationClient.Services
{
    public class ParsecService
    {
        private readonly DatabaseService _databaseService;
        private readonly SettingsService _settings;

        public ParsecService(DatabaseService databaseService, SettingsService settings)
        {
            _databaseService = databaseService;
            _settings = settings;
        }

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

        public static bool CheckGuidePresent(Guid guid)
        {
            try
            {
                var integServ = new IntegrationService();
                var result = integServ.GetObjectName(ClientState.SessionID, guid);

                if (result != null && result.Value != null && !string.IsNullOrEmpty(result.Value.ToString()))
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Logger.Log<ParsecService>("Error", $"CheckGuidePresent: Ошибка при проверке GUID {guid}: {ex.Message}");
                return false;
            }
        }

        public static AccessGroup GetAccessGroups(Guid idGroup)
        {
            try
            {
                var integServ = new IntegrationService();
                var accessGroupsResult = integServ.GetAccessGroups(ClientState.SessionID);

                if (accessGroupsResult == null)
                {
                    Logger.Log<ParsecService>("Warning", $"GetAccessGroups: Результат null для SessionID: {ClientState.SessionID}");
                    return null;
                }

                var accesGroups = accessGroupsResult.ToList();

                foreach (var item in accesGroups)
                {
                    if (item != null && item.ID.Equals(idGroup))
                        return item;
                }

                Logger.Log<ParsecService>("Warning", $"GetAccessGroups: Группа с GUID {idGroup} не найдена");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Log<ParsecService>("Error", $"GetAccessGroups: Ошибка при получении групп доступа: {ex.Message}");
                return null;
            }
        }

        public State AddIdentifierPeople(DbModelRowIDInDev row)
        {
            var state = new State();
            Logger.Log<ParsecService>("Warning", $"Start AddIdentifierPeople {row.ID}");
            try
            {
                var integServ = new IntegrationService();

                var query = "select c.id_card, an.guid, p.guid as people_guid, " +
                    "p.tabnum, p.name, p.patronymic, p.surname, c.id_cardtype from card c, " +
                    "accessname an join people p on p.id_pep = c.id_pep " +
                    $"where c.id_pep = {row.ID_PEP} " +
                    $"and an.id_accessname = {row.ID_CARD}";

                Logger.Log<ParsecService>("Info", $"Запрос к базе {query} для model");
                var model = _databaseService.Get<DbModelAddIdentifier>(query);
                Logger.Log<ParsecService>("Info", $"model {JsonConvert.SerializeObject(model)}");

                if (int.Parse(model.CARDTYPE) != 1)
                {
                    var desc = $"Интегратор не обрабатывает идентификаторы с {model.CARDTYPE}. Обработка прерывается";
                    Logger.Log<ParsecService>("Error", desc);
                    state.desc = desc;
                    state.Status = "ERR";
                    state.ErrorMessage = desc;
                    state.IdCardindev = row.ID;
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                    state.ErrorCode = 3;
                    state.keyNum = Key.keyNumber;
                    return state;
                }

                if (model.CODE == null)
                {
                    state.ErrorCode = 4;
                    var desc = $"КОД: {state.ErrorCode}. SQL нет номера карты в Артонит. Добавление категории доступа прервано.";
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
                    state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                    state.keyNum = Key.keyNumber;
                    return state;
                }

                try
                {
                    if (model.GUID_PEP == null || model.GUID_PEP == string.Empty)
                    {
                        state.ErrorCode = 5;
                        var desc = $"КОД: {state.ErrorCode}. Добавление категории | Сотрудник: {model.SURNAME} {model.NAME} {model.PATRONYMIC} | Ошибка: сотрудник не зарегистрирован в Артонит (GUID_PEP пустой)";
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
                        state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                        state.keyNum = Key.keyNumber;
                        return state;
                    }

                    if (!CheckGuidePresent(new Guid(model.GUID_PEP)))
                    {
                        state.ErrorCode = 5;
                        var desc = $"КОД {state.ErrorCode} Добавление категории | Сотрудник: {model.SURNAME} {model.NAME} {model.PATRONYMIC} | Ошибка: сотрудник не найден в Парсек (GUID_PEP: {model.GUID_PEP})";
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
                        state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                        state.keyNum = Key.keyNumber;
                        return state;
                    }

                    if (model.GUID_ACCESS_GROUP == null || model.GUID_ACCESS_GROUP == string.Empty)
                    {
                        state.ErrorCode = 5;
                        var desc = $"Добавление категории | Сотрудник: {model.SURNAME} {model.NAME} {model.PATRONYMIC} | Ошибка: группа доступа не найдена в Артонит (GUID_ACCESS_GROUP пустой)";
                        var errorMessage = $"Ошибка БД: поле GUID_ACCESS_GROUP не заполнено";
                        Logger.Log<ParsecService>("Warning", desc);
                        state.desc = desc;
                        state.IdCardindev = row.ID;
                        state.Operation = StateService.GetOperationName(row.OPERATION);
                        state.OperationCode = row.OPERATION;
                        state.Status = "ERR";
                        state.ErrorMessage = errorMessage;
                        state.Attempts = row.ATTEMPS;
                        state.Timestamp = DateTime.Now;
                        state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                        state.keyNum = Key.keyNumber;
                        return state;
                    }

                    var accessGroupGuid = new Guid(model.GUID_ACCESS_GROUP);
                    if (!CheckGuidePresent(accessGroupGuid))
                    {
                        var desc = $"Добавление категории | Сотрудник: {model.SURNAME} {model.NAME} {model.PATRONYMIC} | Ошибка: группа доступа не найдена в Парсек (GUID: {model.GUID_ACCESS_GROUP})";
                        var errorMessage = $"Ошибка: группа доступа не синхронизирована в Parsec (GUID: {model.GUID_ACCESS_GROUP})";
                        Logger.Log<ParsecService>("Warning", desc);
                        state.desc = desc;
                        state.IdCardindev = row.ID;
                        state.Operation = StateService.GetOperationName(row.OPERATION);
                        state.OperationCode = row.OPERATION;
                        state.Status = "ERR";
                        state.ErrorMessage = errorMessage;
                        state.Attempts = row.ATTEMPS;
                        state.Timestamp = DateTime.Now;
                        state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                        state.ErrorCode = 6;
                        state.keyNum = Key.keyNumber;
                        return state;
                    }

                    long cardDec = Convert.ToInt64(model.CODE);
                    string hexValue = cardDec.ToString("X8");
                    string accessName = _databaseService.GetString($"select an.name from accessname an where an.id_accessname = {row.ID_CARD}");

                    var person = integServ.GetPerson(ClientState.SessionID, new Guid(model.GUID_PEP));

                    if (person != null)
                    {
                        var res = integServ.OpenPersonEditingSession(ClientState.SessionID, person.ID);

                        if (res.Result != ClientState.Result_Success)
                        {
                            Logger.Log<ParsecService>("Error", $"Ошибка открытия сессии для редактирования пользователя. Ошибка {res.ErrorMessage}");
                            state.desc = $"Ошибка открытия сессии: {res.ErrorMessage}";
                            state.IdCardindev = row.ID;
                            state.Operation = StateService.GetOperationName(row.OPERATION);
                            state.OperationCode = row.OPERATION;
                            state.Status = "ERR";
                            state.ErrorMessage = res.ErrorMessage;
                            state.Attempts = row.ATTEMPS;
                            state.Timestamp = DateTime.Now;
                            state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                            state.ErrorCode = 7;
                            state.keyNum = Key.keyNumber;
                            return state;
                        }

                        var _editSessionID = res.Value;

                        var accesGroup = GetAccessGroups(new Guid(model.GUID_ACCESS_GROUP));

                        if (accesGroup == null)
                        {
                            var desc = $"Группа доступа {(!string.IsNullOrWhiteSpace(accessName) ? accessName : row.ID_CARD)} не найдена в парсек";
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
                            state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                            state.ErrorCode = 6;
                            state.keyNum = Key.keyNumber;
                            return state;
                        }

                        Logger.Log<ParsecService>("Warning", $"Добавление группы доступа пользователю | Группа доступа: {accesGroup.NAME} | карта: hex {hexValue} (dec: {cardDec}) | GUID_PEP = {model.GUID_PEP}");

                        var identifiers = integServ.GetPersonIdentifiers(ClientState.SessionID, new Guid(model.GUID_PEP));

                        var identifier = null as Identifier;
                        if (identifiers != null)
                        {
                            identifier = identifiers.FirstOrDefault(x => x.CODE == hexValue);
                            Logger.Log<ParsecService>("Warning", $"Идентификатор с кодом: {hexValue} | {identifier?.CODE} действительно имеется у пользователя.");
                        }

                        var creatingItem = new Identifier();
                        creatingItem.PERSON_ID = new Guid(model.GUID_PEP);
                        creatingItem.NAME = "";

                        if (identifier != null)
                        {
                            Logger.Log<ParsecService>("Warning", $"карте {identifier.CODE} присвоена категория доступа identifier.ACCGROUP_ID --> {identifier.ACCGROUP_ID}");

                            if (identifier.ACCGROUP_ID == Guid.Empty)
                            {
                                Logger.Log<ParsecService>("Warning", $"у карты {identifier.CODE} категории доступа не было, поэтому присваиваю identifier.ACCGROUP_ID --> {model.GUID_ACCESS_GROUP}");
                                creatingItem.ACCGROUP_ID = new Guid(model.GUID_ACCESS_GROUP);
                                creatingItem.IS_PRIMARY = true;
                                creatingItem.CODE = hexValue;
                            }
                            else
                            {
                                Logger.Log<ParsecService>("Warning", $"у карты {identifier.CODE} уже была категории доступа identifier.ACCGROUP_ID --> {identifier.ACCGROUP_ID}");
                                Logger.Log<ParsecService>("Warning", $"Формирую промежуточную иерархию категорий доступа");

                                var arrayInheritedAccessGroups = integServ.GetInheritedAccessGroups(ClientState.SessionID, identifier.ACCGROUP_ID);
                                var inheritedAccessGroups = (arrayInheritedAccessGroups == null ? new List<Guid>() : arrayInheritedAccessGroups.ToList());

                                if (inheritedAccessGroups.Count == 0)
                                    inheritedAccessGroups.Add(identifier.ACCGROUP_ID);

                                if (accesGroup != null && accesGroup.ID != Guid.Empty)
                                    inheritedAccessGroups.Add(accesGroup.ID);

                                inheritedAccessGroups = inheritedAccessGroups.Distinct().ToList();

                                Logger.Log<ParsecService>("Warning", $"Добавлена новая группа доступа {inheritedAccessGroups.Count}");

                                var resCheckAccessGroups = CheckAccessGroups(inheritedAccessGroups);

                                Logger.Log<ParsecService>("Warning", $"Результат поиска группы доступа с такими же вложенными группами доступа {resCheckAccessGroups}");

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
                                        var errorDesc = $"Ошибка CreateAccessGroup: {resCreateAccessGroup.ErrorMessage}";
                                        Logger.Log<ParsecService>("Error", errorDesc);
                                        state.desc = errorDesc;
                                        state.IdCardindev = row.ID;
                                        state.Operation = StateService.GetOperationName(row.OPERATION);
                                        state.OperationCode = row.OPERATION;
                                        state.Status = "ERR";
                                        state.ErrorMessage = resCreateAccessGroup.ErrorMessage;
                                        state.Attempts = row.ATTEMPS;
                                        state.Timestamp = DateTime.Now;
                                        state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
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

                        Logger.Log<ParsecService>("Error", $"Вызываем метод AddPersonIdentifier с параметрами: ACCGROUP_ID = {creatingItem.ACCGROUP_ID}, CODE = {creatingItem.CODE}, PERSON_ID = {creatingItem.PERSON_ID}");

                        var resAddPersonIdentifier = integServ.AddPersonIdentifier(_editSessionID, creatingItem);
                        Logger.Log<ParsecService>("Error", $"Результат выполнения AddPersonIdentifier: {resAddPersonIdentifier.Result}.");

                        if (resAddPersonIdentifier.Result != ClientState.Result_Success)
                        {
                            var errorDesc = $"Ошибка при добавлении группы доступа пользователю. Ошибка: {resAddPersonIdentifier.ErrorMessage}";
                            Logger.Log<ParsecService>("Error", errorDesc);
                            state.desc = errorDesc;
                            state.IdCardindev = row.ID;
                            state.Operation = StateService.GetOperationName(row.OPERATION);
                            state.OperationCode = row.OPERATION;
                            state.Status = "ERR";
                            state.ErrorMessage = resAddPersonIdentifier.ErrorMessage;
                            state.Attempts = row.ATTEMPS;
                            state.Timestamp = DateTime.Now;
                            state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                            state.ErrorCode = 2;
                            state.keyNum = Key.keyNumber;
                            return state;
                        }

                        Logger.Log<ParsecService>("Warning", $"Группа доступа успешно добавлена | code: {row.ID_CARD} (hex: {hexValue}) Пользователю ФИО (parsec): {person.FIRST_NAME} {person.MIDDLE_NAME} {person.LAST_NAME}");

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
                        state.desc = "Пользователь не найден в parsec, задача удалена";
                        state.IdCardindev = row.ID;
                        state.Operation = StateService.GetOperationName(row.OPERATION);
                        state.OperationCode = row.OPERATION;
                        state.Status = "OK";
                        state.Attempts = row.ATTEMPS;
                        state.Timestamp = DateTime.Now;
                        state.keyNum = Key.keyNumber;
                        Logger.Log<ParsecService>("Warning", $"Пользователь с GUID: {model.GUID_PEP} не найден в parsec. Задача {row.ID} удалена");
                        return state;
                    }
                }
                catch (Exception ex)
                {
                    var errorDesc = $"Ошибка в AddIdentifierPeople (внутренний catch): {ex.Message}";
                    Logger.Log<ParsecService>("Warning", $"{errorDesc} | {ex.Source} | {ex.StackTrace}");
                    state.desc = errorDesc;
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "ERR";
                    state.ErrorMessage = ex.Message?.Replace("\r\n", " ") ?? "Unknown error";
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                    state.ErrorCode = 9;
                    state.keyNum = Key.keyNumber;
                    return state;
                }
            }
            catch (Exception ex)
            {
                var errorDesc = $"Ошибка в AddIdentifierPeople (внешний catch): {ex.Message}";
                Logger.Log<ParsecService>("Warning", $"{errorDesc} | {ex.Source} | {ex.StackTrace}");
                state.desc = errorDesc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = ex.Message?.Replace("\r\n", " ") ?? "Unknown error";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                state.ErrorCode = 9;
                state.keyNum = Key.keyNumber;
                return state;
            }
        }

        public State RemoveIdentifierPeople(DbModelRowIDInDev row)
        {
            var state = new State();
            Logger.Log<ParsecService>("Warning", $"Start RemoveIdentifierPeople {row.ID}");
            var discBuilder = new StringBuilder();
            Action<string> addDisc = msg =>
            {
                if (string.IsNullOrWhiteSpace(msg)) return;
                if (discBuilder.Length > 0) discBuilder.Append(" | ");
                discBuilder.Append(msg);
            };
            addDisc("type=RemoveIdentifierPeople");
            addDisc($"cardindev={row.ID}");
            addDisc($"operation={row.OPERATION}");

            var integServ = new IntegrationService();

            var query = "select c.id_card, an.guid, p.guid as people_guid, " +
                "p.tabnum, p.name, p.patronymic, p.surname, c.id_cardtype from card c, " +
                "accessname an join people p on p.id_pep = c.id_pep " +
                $"where c.id_pep = {row.ID_PEP} " +
                $"and an.id_accessname = {row.ID_CARD}";

            var model = _databaseService.Get<DbModelAddIdentifier>(query);

            if (int.Parse(model.CARDTYPE) != 1)
            {
                var desc = $"Интегратор не обрабатывает идентификаторы с {model.CARDTYPE}. Обработка прерывается";
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
                state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                state.ErrorCode = 3;
                state.keyNum = Key.keyNumber;
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
                state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                state.ErrorCode = 10;
                state.keyNum = Key.keyNumber;
                return state;
            }

            if (model.CODE == null)
            {
                var desc = "В результате запроса к базе данных не было получено данных";
                var errorMessage = $"Ошибка БД: отсутствует код карты (CODE=null) для контакта ID={row.ID_PEP}";
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
                state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                state.ErrorCode = 4;
                state.keyNum = Key.keyNumber;
                return state;
            }

            if (model.GUID_PEP == null || model.GUID_PEP == string.Empty)
            {
                var desc = "GUID_PEP null or empty";
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
                state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                state.ErrorCode = 11;
                state.keyNum = Key.keyNumber;
                return state;
            }

            string hexValue = Convert.ToInt64(model.CODE).ToString("X8");
            addDisc($"model_ok_guidPep={model.GUID_PEP}");
            addDisc($"card_hex={hexValue}");

            Logger.Log<ParsecService>("Warning", $"Удаление идентификатора | {model.CODE} ({hexValue})");

            var person = integServ.GetPerson(ClientState.SessionID, new Guid(model.GUID_PEP));
            addDisc("after_get_person");

            if (person == null)
            {
                var desc = $"RemoveIdentifierPeople: человека {model.SURNAME} {model.NAME} {model.PATRONYMIC} нет в базе данных Парсек";
                var errorMessage = $"Ошибка Parsec: пользователь с GUID {model.GUID_PEP} ({model.SURNAME} {model.NAME} {model.PATRONYMIC}) не найден в базе данных";
                state.desc = desc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.Status = "ERR";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.OperationCode = row.OPERATION;
                state.ErrorMessage = errorMessage;
                state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                state.ErrorCode = 12;
                state.keyNum = Key.keyNumber;
                addDisc(desc);
                Logger.Log<ParsecService>("Error", state.ToString());
                return state;
            }

            var res = integServ.OpenPersonEditingSession(ClientState.SessionID, person.ID);

            if (res.Result != ClientState.Result_Success)
            {
                var desc = $"Ошибка открытия сессии для редактирования пользователя. Ошибка {res.ErrorMessage}";
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
                state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                state.ErrorCode = 7;
                state.keyNum = Key.keyNumber;
                return state;
            }

            var _editSessionID = res.Value;
            addDisc("editing_session_opened");

            var identifiers = integServ.GetPersonIdentifiers(ClientState.SessionID, new Guid(model.GUID_PEP));
            addDisc("after_get_identifiers");

            if (identifiers == null)
            {
                var desc = $"У {model.SURNAME} {model.NAME} {model.PATRONYMIC} {model.TAB_NUM_PEP} карты нет. Удалить категорию доступа не могу.";
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
                state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                state.ErrorCode = 13;
                state.keyNum = Key.keyNumber;
                return state;
            }

            Logger.Log<ParsecService>("Warning", $"Получены идентификаторы у человека в количестве {identifiers.Length}");

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
                state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                state.ErrorCode = 14;
                state.keyNum = Key.keyNumber;
                return state;
            }

            Logger.Log<ParsecService>("Info", $"Найден идентификатор: {JsonConvert.SerializeObject(identifier)}");
            addDisc($"identifier_found={identifier.CODE}");

            if (identifier.ACCGROUP_ID == Guid.Empty || identifier.ACCGROUP_ID.ToString() == "00000000-0000-0000-0000-000000000000")
            {
                var desc = $"У идентификатора {hexValue} нет привязанной группы доступа (ACCGROUP_ID пустой)";
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
                    state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                    state.ErrorCode = 16;
                    state.keyNum = Key.keyNumber;
                    return state;
                }

                var inheritedAccessGroups = arrayInheritedAccessGroups.ToList();

                inheritedAccessGroups.Remove(new Guid(model.GUID_ACCESS_GROUP));

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
                    Logger.Log<ParsecService>("Warning", $"Результат поиска группы доступа с такими же вложенными группами доступа {resCheckAccessGroups}");

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
                            var errorDesc = $"Ошибка CreateAccessGroup: {resCreateAccessGroup.ErrorMessage}";
                            Logger.Log<ParsecService>("Error", errorDesc);
                            state.desc = errorDesc;
                            state.IdCardindev = row.ID;
                            state.Operation = StateService.GetOperationName(row.OPERATION);
                            state.OperationCode = row.OPERATION;
                            state.Status = "ERR";
                            state.ErrorMessage = resCreateAccessGroup.ErrorMessage;
                            state.Attempts = row.ATTEMPS;
                            state.Timestamp = DateTime.Now;
                            state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                            state.ErrorCode = 8;
                            state.keyNum = Key.keyNumber;
                            return state;
                        }

                        var rGuid = resCreateAccessGroup.Value;
                        integServ.SetInheritedAccessGroups(ClientState.SessionID, rGuid, inheritedAccessGroups.ToArray());
                        creatingItem = new Identifier()
                        {
                            ACCGROUP_ID = rGuid,
                            IS_PRIMARY = true,
                            CODE = hexValue,
                            PERSON_ID = personGuid
                        };
                    }
                }

                var resAddPersonIdentifier = integServ.AddPersonIdentifier(_editSessionID, creatingItem);
                if (resAddPersonIdentifier.Result != ClientState.Result_Success)
                {
                    var errorDesc = $"Ошибка при добавлении группы доступа пользователю. Ошибка: {resAddPersonIdentifier.ErrorMessage}";
                    Logger.Log<ParsecService>("Error", errorDesc);
                    state.desc = errorDesc;
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "ERR";
                    state.ErrorMessage = resAddPersonIdentifier.ErrorMessage;
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                    state.ErrorCode = 2;
                    state.keyNum = Key.keyNumber;
                    return state;
                }

                Logger.Log<ParsecService>("Warning", $"Группа доступа успешно обновлена | code: {row.ID_CARD} (hex: {hexValue}) Пользователю ФИО (parsec): {person.FIRST_NAME} {person.MIDDLE_NAME} {person.LAST_NAME}");

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
                var errorDesc = $"Ошибка в RemoveIdentifierPeople: {ex.Message}";
                Logger.Log<ParsecService>("Warning", $"{errorDesc} | {ex.Source} | {ex.StackTrace}");
                state.desc = errorDesc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = ex.Message?.Replace("\r\n", " ") ?? "Unknown error";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                state.ErrorCode = 17;
                state.keyNum = Key.keyNumber;
                return state;
            }
        }

        public State AddPeople(DbModelRowIDInDev row)
        {
            var state = new State();
            Logger.Log<ParsecService>("Error", $"Start AddPeople {row.ID}");
            try
            {
                var query = "select p.id_pep, p.guid as guid_pep, " +
                    "o.guid as guid_org, p.name, p.surname, p.patronymic, p.tabnum, o.name as org_name " +
                    "from people p " +
                    "left join organization o on p.id_org=o.id_org " +
                    $"where p.id_pep={row.ID_PEP};";

                var people = _databaseService.Get<DbModelAddPeople>(query);

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

                    Logger.Log<ParsecService>("Info", $"Добавляется сотрудник {JsonConvert.SerializeObject(person)}");

                    if (CheckGuidePresent(person.ID))
                    {
                        state.ErrorCode = 18;
                        var desc = $"КОД ОШИБКИ: {state.ErrorCode}. Уже имеется сотрудник с GUID {person.ID}. Работу завершаю.";
                        var errorMessage = $"Ошибка: сотрудник с GUID {person.ID} уже существует в Parsec";
                        Logger.Log<ParsecService>("Warning", desc);
                        state.desc = desc;
                        state.IdCardindev = row.ID;
                        state.Operation = StateService.GetOperationName(row.OPERATION);
                        state.OperationCode = row.OPERATION;
                        state.Status = "ERR";
                        state.ErrorMessage = errorMessage;
                        state.Attempts = row.ATTEMPS;
                        state.Timestamp = DateTime.Now;
                        state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                        state.keyNum = Key.keyNumber;
                        return state;
                    }

                    Logger.Log<ParsecService>("Warning", $"Сотрудник с GUID {person.ID} нет в Парсек. Продолжаю добавление.");

                    if (!CheckGuidePresent(person.ORG_ID))
                    {
                        var desc = $"Не существует организация {people.ORG_NAME} с указанным GUID {person.ORG_ID} в Парсек. Работу завершаю.";
                        var errorMessage = $"Ошибка: организация не найдена в Parsec (GUID: {person.ORG_ID})";
                        Logger.Log<ParsecService>("Warning", desc);
                        state.desc = desc;
                        state.IdCardindev = row.ID;
                        state.Operation = StateService.GetOperationName(row.OPERATION);
                        state.OperationCode = row.OPERATION;
                        state.Status = "ERR";
                        state.ErrorMessage = errorMessage;
                        state.Attempts = row.ATTEMPS;
                        state.Timestamp = DateTime.Now;
                        state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                        state.ErrorCode = 19;
                        state.keyNum = Key.keyNumber;
                        return state;
                    }

                    var integServ = new IntegrationService();
                    var res = integServ.CreatePerson(ClientState.SessionID, person);

                    if (res.Result != ClientState.Result_Success)
                    {
                        Logger.Log<ParsecService>("Error", $"Не смог вставить сотрудника {JsonConvert.SerializeObject(person)}");
                        Logger.Log<ParsecService>("Error", $"{res.ErrorMessage}");
                        state.desc = $"Ошибка при добавлении сотрудника: {res.ErrorMessage}";
                        state.IdCardindev = row.ID;
                        state.Operation = StateService.GetOperationName(row.OPERATION);
                        state.OperationCode = row.OPERATION;
                        state.Status = "ERR";
                        state.ErrorMessage = res.ErrorMessage;
                        state.Attempts = row.ATTEMPS;
                        state.Timestamp = DateTime.Now;
                        state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                        state.ErrorCode = 1;
                        state.keyNum = Key.keyNumber;
                        return state;
                    }

                    Logger.Log<ParsecService>("Info", $"Пользователь добавлен успешно {JsonConvert.SerializeObject(person)}");
                    state.desc = "Пользователь добавлен успешно";
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "OK";
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.keyNum = Key.keyNumber;
                    return state;
                }

                var descNotFound = $"Пользователь с {row.ID_PEP} не найден в базе СКУД Артонит.";
                Logger.Log<ParsecService>("Warning", descNotFound);
                state.desc = descNotFound;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = $"Ошибка БД: пользователь с ID_PEP={row.ID_PEP} не найден в базе СКУД";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                state.ErrorCode = 10;
                state.keyNum = Key.keyNumber;
                return state;
            }
            catch (Exception ex)
            {
                var errorDesc = $"Ошибка в AddPeople: {ex.Message}";
                Logger.Log<ParsecService>("Warning", errorDesc);
                state.desc = errorDesc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = ex.Message?.Replace("\r\n", " ") ?? "Unknown error";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                state.ErrorCode = 10;
                state.keyNum = Key.keyNumber;
                return state;
            }
        }

        public State RemovePeople(DbModelRowIDInDev row)
        {
            var state = new State();
            Logger.Log<ParsecService>("Error", $"Start RemovePeople {row.ID}");

            try
            {
                if (!CheckGuidePresent(new Guid(row.ID_CARD)))
                {
                    var desc = $"Сотрудник отсутствует в Парсек. Команда по удалению выполнена успешно.";
                    Logger.Log<ParsecService>("Info", desc);
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

                Logger.Log<ParsecService>("Info", $"Сотрудник присутствует в Парсек, продолжаю удаление.");

                var integServ = new IntegrationService();
                var res = integServ.DeletePerson(ClientState.SessionID, new Guid(row.ID_CARD));

                if (res.Result == ClientState.Result_Success)
                {
                    Logger.Log<ParsecService>("Info", $"Пользователь успешно удален");
                    state.desc = "Пользователь успешно удален";
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
                    if (res.ErrorMessage.Contains("Could not delete person. There is no person with id"))
                    {
                        state.desc = "Удаление человека произошло успешно. Человека не было в Парсек";
                        state.IdCardindev = row.ID;
                        state.Operation = StateService.GetOperationName(row.OPERATION);
                        state.OperationCode = row.OPERATION;
                        state.Status = "OK";
                        state.Attempts = row.ATTEMPS;
                        state.Timestamp = DateTime.Now;
                        state.keyNum = Key.keyNumber;
                        return state;
                    }

                    state.ErrorCode = 20;
                    Logger.Log<ParsecService>("Error", $"КОД ОШИБКИ: {state.ErrorCode} Ошибка при удалении пользователя. Ошибка: {res.ErrorMessage}");
                    state.desc = $"Ошибка при удалении пользователя: {res.ErrorMessage}";
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "ERR";
                    state.ErrorMessage = res.ErrorMessage;
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                    state.keyNum = Key.keyNumber;
                    return state;
                }
            }
            catch (Exception ex)
            {
                state.ErrorCode = 20;
                var errorDesc = $"Ошибка в RemovePeople: {ex.Message}";
                Logger.Log<ParsecService>("Warning", errorDesc);
                state.desc = errorDesc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = ex.Message?.Replace("\r\n", " ") ?? "Unknown error";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                state.keyNum = Key.keyNumber;
                return state;
            }
        }

        public State AddOrg(DbModelRowIDInDev row)
        {
            var state = new State();
            Logger.Log<ParsecService>("Error", $"Start AddOrg {row.ID}");
            try
            {
                var query = "select o.guid as guide_for_add, " +
                    "o2.guid as guid_for_parent, o.name, o.divcode, o.id_org from organization o " +
                    "join organization o2 on o2.id_org=o.id_parent " +
                    $"where o.guid='{row.ID_CARD}'";

                var model = _databaseService.Get<DbModelAddOrg>(query);

                Logger.Log<ParsecService>("Warning", $"Добавление организации | NAME: {model.NAME} DIVCODE: {model.DIVCODE}");

                if (model.NAME == null)
                {
                    state.ErrorCode = 21;
                    var desc = $"Организация {row.ID_CARD} не найдена";
                    var errorMessage = $"Ошибка БД: организация с GUID={row.ID_CARD} не найдена в базе СКУД";
                    Logger.Log<ParsecService>("Error", desc);
                    state.desc = desc;
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "ERR";
                    state.ErrorMessage = errorMessage;
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                    state.keyNum = Key.keyNumber;
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
                    Logger.Log<ParsecService>("Error", $"КОД ОШИБКИ: {state.ErrorCode}. {result.ErrorMessage}");
                    state.desc = $"Ошибка при добавлении организации: {result.ErrorMessage}";
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "ERR";
                    state.ErrorMessage = result.ErrorMessage;
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                    state.keyNum = Key.keyNumber;
                    return state;
                }

                Logger.Log<ParsecService>("Warning", $"Организация добавлена успешно: {org.NAME} | ID: {org.ID} Parent ID: {org.PARENT_ID}");
                state.desc = "Организация добавлена успешно";
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
                state.ErrorCode = 22;
                var errorDesc = $"Ошибка в AddOrg: {ex.Message}";
                Logger.Log<ParsecService>("Warning", errorDesc);
                state.desc = errorDesc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = ex.Message?.Replace("\r\n", " ") ?? "Unknown error";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                state.keyNum = Key.keyNumber;
                return state;
            }
        }

        public State RemoveOrg(DbModelRowIDInDev row)
        {
            var state = new State();
            Logger.Log<ParsecService>("Error", $"Start RemoveOrg {row.ID}");
            try
            {
                var integServ = new IntegrationService();

                Logger.Log<ParsecService>("Warning", $"Удаление организации | {row.ID_CARD}");

                var res = integServ.DeleteOrgUnit(ClientState.SessionID, new Guid(row.ID_CARD));
                if (res.Result != ClientState.Result_Success)
                {
                    state.ErrorCode = 22;
                    Logger.Log<ParsecService>("Error", $"КОД ОШИБКИ: {state.ErrorCode} {res.ErrorMessage}");
                    state.desc = $"Ошибка при удалении организации: {res.ErrorMessage}";
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "ERR";
                    state.ErrorMessage = res.ErrorMessage;
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                    state.keyNum = Key.keyNumber;
                    return state;
                }

                Logger.Log<ParsecService>("Warning", $"Организация успешно удалена | {row.ID_CARD}");
                state.desc = "Организация успешно удалена";
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
                state.ErrorCode = 22;
                var errorDesc = $"Ошибка в RemoveOrg: {ex.Message}";
                Logger.Log<ParsecService>("Warning", errorDesc);
                state.desc = errorDesc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = ex.Message?.Replace("\r\n", " ") ?? "Unknown error";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                state.keyNum = Key.keyNumber;
                return state;
            }
        }

        public State AddCardPeople(DbModelRowIDInDev row)
        {
            var state = new State();
            Logger.Log<ParsecService>("Error", $"Start AddCardPeople {row.ID}");
            try
            {
                var query = "select p.guid, p.tabnum, p.name, p.patronymic, " +
                    $"p.surname from people p where p.id_pep = {row.ID_PEP}";

                var list = _databaseService.GetList<DbModelAddCard>(query);

                foreach (var model in list)
                {
                    try
                    {
                        if (model.GUID_PEP == null || model.GUID_PEP == string.Empty)
                        {
                            state.ErrorCode = 23;
                            var errorDesc = $"КОД ОШИБКИ: {state.ErrorCode}. GUID_PEP null or empty для контакта ID={row.ID_PEP}";
                            Logger.Log<ParsecService>("Warning", errorDesc);
                            state.desc = errorDesc;
                            state.IdCardindev = row.ID;
                            state.Operation = StateService.GetOperationName(row.OPERATION);
                            state.OperationCode = row.OPERATION;
                            state.Status = "ERR";
                            state.ErrorMessage = $"Ошибка БД: поле GUID_PEP не заполнено для контакта ID={row.ID_PEP}";
                            state.Attempts = row.ATTEMPS;
                            state.Timestamp = DateTime.Now;
                            state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                            state.keyNum = Key.keyNumber;
                            return state;
                        }

                        string hexValue = Convert.ToInt64(row.ID_CARD).ToString("X8");

                        Logger.Log<ParsecService>("Warning", $"Добавление карты пользователю | code: {row.ID_CARD} (hex: {hexValue}) | GUID_PEP = {model.GUID_PEP}");

                        var integServ = new IntegrationService();

                        var person = integServ.GetPerson(ClientState.SessionID, new Guid(model.GUID_PEP));

                        if (person != null)
                        {
                            var res = integServ.OpenPersonEditingSession(ClientState.SessionID, person.ID);

                            if (res.Result != ClientState.Result_Success)
                            {
                                state.ErrorCode = 7;
                                Logger.Log<ParsecService>("Error", $"КОД ОШИБКИ: {state.ErrorCode}. Ошибка открытия сессии для редактирования пользователя. Ошибка {res.ErrorMessage}");
                                state.desc = $"Ошибка открытия сессии: {res.ErrorMessage}";
                                state.IdCardindev = row.ID;
                                state.Operation = StateService.GetOperationName(row.OPERATION);
                                state.OperationCode = row.OPERATION;
                                state.Status = "ERR";
                                state.ErrorMessage = res.ErrorMessage;
                                state.Attempts = row.ATTEMPS;
                                state.Timestamp = DateTime.Now;
                                state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                                state.keyNum = Key.keyNumber;
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
                                Logger.Log<ParsecService>("Error", $"КОД ОШИБКИ: {state.ErrorCode}. Ошибка при добавлении карты пользователю. Ошибка: {resAddPersonIdentifier.ErrorMessage}");
                                state.desc = $"Ошибка при добавлении карты: {resAddPersonIdentifier.ErrorMessage}";
                                state.IdCardindev = row.ID;
                                state.Operation = StateService.GetOperationName(row.OPERATION);
                                state.OperationCode = row.OPERATION;
                                state.Status = "ERR";
                                state.ErrorMessage = resAddPersonIdentifier.ErrorMessage;
                                state.Attempts = row.ATTEMPS;
                                state.Timestamp = DateTime.Now;
                                state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                                state.keyNum = Key.keyNumber;
                                return state;
                            }

                            Logger.Log<ParsecService>("Warning", $"Карта успешно добавлена | code: {row.ID_CARD} (hex: {hexValue}) Пользователю ФИО (parsec): {person.FIRST_NAME} {person.MIDDLE_NAME} {person.LAST_NAME}");
                            state.desc = "Карта успешно добавлена";
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
                            state.ErrorCode = 25;
                            var errorDesc = $"КОД ОШИБКИ: {state.ErrorCode}. Пользователь с GUID: {model.GUID_PEP} не найден в parsec.";
                            Logger.Log<ParsecService>("Warning", errorDesc);
                            state.desc = errorDesc;
                            state.IdCardindev = row.ID;
                            state.Operation = StateService.GetOperationName(row.OPERATION);
                            state.OperationCode = row.OPERATION;
                            state.Status = "ERR";
                            state.ErrorMessage = $"Ошибка Parsec: пользователь с GUID {model.GUID_PEP} не найден";
                            state.Attempts = row.ATTEMPS;
                            state.Timestamp = DateTime.Now;
                            state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                            state.keyNum = Key.keyNumber;
                            return state;
                        }
                    }
                    catch (Exception ex)
                    {
                        state.ErrorCode = 9;
                        var errorDesc = $"Ошибка в AddCardPeople (внутренний catch): {ex.Message}";
                        Logger.Log<ParsecService>("Warning", errorDesc);
                        state.desc = errorDesc;
                        state.IdCardindev = row.ID;
                        state.Operation = StateService.GetOperationName(row.OPERATION);
                        state.OperationCode = row.OPERATION;
                        state.Status = "ERR";
                        state.ErrorMessage = ex.Message?.Replace("\r\n", " ") ?? "Unknown error";
                        state.Attempts = row.ATTEMPS;
                        state.Timestamp = DateTime.Now;
                        state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                        state.keyNum = Key.keyNumber;
                        return state;
                    }
                }

                state.ErrorCode = 25;
                var errorNoData = $"Не найден пользователь с ID_PEP = {row.ID_PEP}";
                Logger.Log<ParsecService>("Warning", errorNoData);
                state.desc = errorNoData;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = $"В БД не найден контакт с ID={row.ID_PEP}";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                state.keyNum = Key.keyNumber;
                return state;
            }
            catch (Exception ex)
            {
                state.ErrorCode = 9;
                var errorDesc = $"Ошибка в AddCardPeople (внешний catch): {ex.Message}";
                Logger.Log<ParsecService>("Warning", errorDesc);
                state.desc = errorDesc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = ex.Message?.Replace("\r\n", " ") ?? "Unknown error";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                state.keyNum = Key.keyNumber;
                return state;
            }
        }

        public State RemoveCardPeople(DbModelRowIDInDev row)
        {
            var state = new State();
            try
            {
                var integServ = new IntegrationService();

                string hexValue = Convert.ToInt64(row.ID_CARD).ToString("X8");

                Logger.Log<ParsecService>("Warning", $"Удаление карты | {row.ID_CARD} (hex: {hexValue})");

                var res = integServ.DeleteIdentifier(ClientState.SessionID, hexValue);
                if (res.Result != ClientState.Result_Success)
                {
                    state.ErrorCode = 26;
                    Logger.Log<ParsecService>("Error", $"КОД ОШИБКИ: {state.ErrorCode}. {res.ErrorMessage}");
                    state.desc = $"Ошибка при удалении карты: {res.ErrorMessage}";
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "ERR";
                    state.ErrorMessage = res.ErrorMessage;
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                    state.keyNum = Key.keyNumber;
                    return state;
                }

                Logger.Log<ParsecService>("Warning", $"Карта успешно удалена | {row.ID_CARD} (hex: {hexValue})");
                state.desc = "Карта успешно удалена";
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
                state.ErrorCode = 26;
                var errorDesc = $"Ошибка в RemoveCardPeople: {ex.Message}";
                Logger.Log<ParsecService>("Warning", errorDesc);
                state.desc = errorDesc;
                state.IdCardindev = row.ID;
                state.Operation = StateService.GetOperationName(row.OPERATION);
                state.OperationCode = row.OPERATION;
                state.Status = "ERR";
                state.ErrorMessage = ex.Message?.Replace("\r\n", " ") ?? "Unknown error";
                state.Attempts = row.ATTEMPS;
                state.Timestamp = DateTime.Now;
                state.NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes);
                state.keyNum = Key.keyNumber;
                return state;
            }
        }
    }
}