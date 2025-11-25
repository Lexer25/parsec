using ParsecIntegrationClient.Models;
using Quartz;
using System;
using System.Threading.Tasks;
using System.Web.Services.Description;

namespace ParsecIntegrationClient.Services
{
    internal class MainJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            Logger.Log<MainJob>("Info", $"Запуск главной работы");

            await Task.Run(() => {

                var list = DatabaseService.GetList<DbModelRowIDInDev>(SettingsService.QuerySelectIdDevCardString);

                Logger.Log<MainJob>("Info", $"С базы данных было получено {list.Count} записей");

                int i = 1;

                list.ForEach(row =>
                {
                    try
                    {
                        int operation = Convert.ToInt32(row.OPERATION);
                        MOperation operationName = (MOperation) Enum.GetValues(typeof(MOperation)).GetValue(operation - 1);
                        Logger.Log<MainJob>("Info", $"Обработка строки номер {i} | Опрерация: {operationName}");
                    }
                    catch (Exception)
                    {
                        Logger.Log<MainJob>("Info", $"Обработка строки номер {i} " +
                            $"| Номер операции: {row.OPERATION} Попытка номер: {row.ATTEMPS}");
                    }

                    switch (row.OPERATION)
                    {
                        case "1": // Добавление карточки
                            {
                                ParsecService.AddCardPeople(row);
                                break;
                            }
                        case "2": // Удалить карточку
                            {
                                ParsecService.RemoveCardPeople(row);
                                break;
                            }
                        case "3": //Добавление человека
                            {
                                ParsecService.AddPeople(row);
                                break;
                            }
                        case "4": //Удаление человека
                            {
                                ParsecService.RemovePeople(row);
                                break;
                            }
                        case "5": //Добавление организации
                            {
                                ParsecService.AddOrg(row);
                                break;
                            }
                        case "6": //Удаление организации
                            {
                                ParsecService.RemoveOrg(row);
                                break;
                            }
                        case "7": //Добавление категории доступа
                            {
                                ParsecService.AddIdentifierPeople(row);
                                break;
                            }
                        case "8": //Удаление категории доступа
                            {
                                ParsecService.RemoveIdentifierPeople(row);
                                break;
                            }
                    }

                    i++;
                });

                Logger.Log<MainJob>("Info", $"Завершение главной работы");
            });
        }
    }
}
