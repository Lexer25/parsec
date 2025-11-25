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
        public static List<T> GetList<T>(string query)
        {
            var rows = new List<T>();

            var connectionString = SettingsService.DatabaseConnectionString;

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
                            var instance = (T) Activator.CreateInstance(typeof(T));

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

        public static T Get<T>(string query)
        {

           // Logger.Log<DatabaseService>("Info", $"Выполняется запрос к базе данных: {query}");

            var instance = (T) Activator.CreateInstance(typeof(T));

            var connectionString = SettingsService.DatabaseConnectionString;

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

                        Logger.Log<DatabaseService>("Info", $"Результат запроса пустой");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log<DatabaseService>("Exception", $"{ex.Message}");
                }
            }

            return instance;
        }

        public static void DeleteIdInDevById(string id)
        {
            try
            {
                Logger.Log<DatabaseService>("Info", $"Удаление записи в таблице CardInDev | ID {id}");

                var connectionString = SettingsService.DatabaseConnectionString;

                using (var connection = new FbConnection(connectionString))
                {
                    connection.Open();

                    var stringCommand = $"delete from CARDINDEV cg where cg.id_cardindev = {id}";
                    var cmd = new FbCommand(stringCommand, connection);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex) 
            {
                Logger.Log<DatabaseService>("Exception", $"{ex.Message}");
            }
        }

        public static void IncrementAttemp(DbModelRowIDInDev row)
        {
            try
            {
                Logger.Log<DatabaseService>("Info", $"Инкремент attempts | ID: {row.ID}");

                var attemps = Convert.ToInt32(row.ATTEMPS);
                attemps++;

                var connectionString = SettingsService.DatabaseConnectionString;

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
    }
}
