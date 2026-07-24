using FirebirdSql.Data.FirebirdClient;
using ParsecIntegrationClient.Models;
using System;
using System.Collections.Generic;
using System.Management.Instrumentation;
using System.Reflection;

namespace ParsecIntegrationClient.Services
{
    public class DatabaseService
    {
        private readonly SettingsService _settings;

        // ⬇️ Конструктор с DI ⬇️
        public DatabaseService(SettingsService settings)
        {
            _settings = settings;
        }

        public string GetString(string query)
        {
            var connectionString = _settings.DatabaseConnectionString;  // ← используем экземпляр

            using (var connection = new FbConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    var cmd = new FbCommand(query, connection);
                    var result = cmd.ExecuteScalar();
                    return result == null ? null : result.ToString();
                }
                catch (Exception ex)
                {
                    Logger.Log<DatabaseService>("Error", $"DatabaseService.GetString failed: {ex.Message}");
                    return null;
                }
            }
        }

        public List<T> GetList<T>(string query)
        {
            var rows = new List<T>();
            var connectionString = _settings.DatabaseConnectionString;  // ← используем экземпляр

            using (var connection = new FbConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    var cmd = new FbCommand(query, connection);

                    using (var dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            var instance = (T)Activator.CreateInstance(typeof(T));
                            int i = 0;
                            var fields = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Static | 
                                BindingFlags.NonPublic | BindingFlags.Public);

                            foreach (var field in fields)
                            {
                                field.SetValue(instance, dr.GetValue(i).ToString());
                                i++;
                            }

                            rows.Add(instance);
                        }
                    }

                    return rows;
                }
                catch (Exception ex)
                {
                    Logger.Log<DatabaseService>("Exception", $"{ex.Message}");
                }
            }

            return rows;
        }

        public T Get<T>(string query)
        {
            var instance = (T)Activator.CreateInstance(typeof(T));
            var connectionString = _settings.DatabaseConnectionString;  // ← используем экземпляр

            using (var connection = new FbConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    var cmd = new FbCommand(query, connection);

                    using (var dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            int i = 0;
                            var fields = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Static |
                                BindingFlags.NonPublic | BindingFlags.Public);

                            foreach (var field in fields)
                            {
                                field.SetValue(instance, dr.GetValue(i).ToString());
                                i++;
                            }

                            return instance;
                        }

                        Logger.Log<DatabaseService>("Warning", $"Результат запроса пустой");
                        return instance;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log<DatabaseService>("Exception", $"{ex.Message}");
                }
            }

            return instance;
        }

        public void DeleteIdInDevById(string id)
        {
            try
            {
                Logger.Log<DatabaseService>("Warning", $"Удаление записи в таблице CardInDev | ID {id}");
                var connectionString = _settings.DatabaseConnectionString;  // ← используем экземпляр

                using (var connection = new FbConnection(connectionString))
                {
                    connection.Open();
                    var stringCommand = $"delete from CARDINDEV cg where cg.id_cardindev = {id}";
                    var cmd = new FbCommand(stringCommand, connection);
                    var affectedRows = cmd.ExecuteNonQuery();
                    Logger.Log<DatabaseService>("Warning", $"Удалено записей: {affectedRows} | ID {id}");
                    
                    if (affectedRows == 0)
                    {
                        Logger.Log<DatabaseService>("Error", $"Не удалось удалить запись | ID {id} - записи не найдены");
                    }
                }
            }
            catch (Exception ex) 
            {
                Logger.Log<DatabaseService>("Exception", $"{ex.Message}");
            }
        }

        public void IncrementAttemp(DbModelRowIDInDev row)
        {
            try
            {
                var attemps = Convert.ToInt32(row.ATTEMPS);
                attemps++;
                var connectionString = _settings.DatabaseConnectionString;  // ← используем экземпляр

                using (var connection = new FbConnection(connectionString))
                {
                    connection.Open();
                    var stringCommand = $"update cardindev cd set cd.attempts = {attemps} where cd.id_cardindev={row.ID}";
                    var cmd = new FbCommand(stringCommand, connection);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Logger.Log<DatabaseService>("Exception", $"{ex.Message}");
            }
        }

        public void DeleteMethod(string id)
        {
            try
            {
                Logger.Log<DatabaseService>("Warning", $"DeleteMethod вызван для ID: {id}");
                var connectionString = _settings.DatabaseConnectionString;  // ← используем экземпляр

                using (var connection = new FbConnection(connectionString))
                {
                    connection.Open();
                    var stringCommand = $"delete from CARDINDEV cg where cg.id_cardindev = {id}";
                    var cmd = new FbCommand(stringCommand, connection);
                    var affectedRows = cmd.ExecuteNonQuery();
                    Logger.Log<DatabaseService>("Warning", $"DeleteMethod удалено записей: {affectedRows} | ID {id}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log<DatabaseService>("Exception", $"DeleteMethod ошибка: {ex.Message}");
            }
        }
    }
}