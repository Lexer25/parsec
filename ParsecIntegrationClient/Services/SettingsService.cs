using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;

namespace ParsecIntegrationClient.Services
{
    public class SettingsService
    {
        private static readonly string FileName = "appsettings.json";
        private static SettingsService _instance;

        // ⬇️ ТОЛЬКО СВОЙСТВА ЭКЗЕМПЛЯРА ⬇️
        public string LogLevel { get; set; } = "Warning";
        public string DatabaseConnectionString { get; set; } = "User = SYSDBA; Password = temp; Database = C:\\Program Files (x86)\\Cardsoft\\DuoSE\\Access\\ShieldPro_rest.gdb; DataSource = 127.0.0.1; Port = 3050; Dialect = 3; Charset = win1251; Role =; Connection lifetime = 15; Pooling = true; MinPoolSize = 0; MaxPoolSize = 50; Packet Size = 8192; ServerType = 0;";
        public string QuerySelectIdDevCardString { get; set; } = "select cd.id_cardindev as id, cd.id_card, cd.id_pep, cd.operation, cd.attempts from cardindev cd where ((cd.id_dev in (select d.id_dev from device d join device d2 on d2.id_ctrl=d.id_ctrl and d2.id_reader is null join servertypelist stl on d2.id_server=stl.id_server join servertype st on st.id=stl.id_type and st.sname='parsec' where d.id_reader is not null)) or(cd.id_dev is null)) and cd.attempts<20 order by cd.id_cardindev";
        public int DatabaseJobTimeout { get; set; } = 60;
        public int ErrorTimeoutMinutes { get; set; } = 1;
        public List<int> SkipErrCode { get; set; } = new List<int>();

        // ⬇️ СТАТИЧЕСКИЕ ПОЛЯ ДЛЯ КОНФИГУРАЦИИ (без дублирования) ⬇️
        public static SettingsService Instance => _instance ??= Load();

        public static SettingsService Load()
        {
            var filePath = $@"{ServiceConfig.MainPath}\{FileName}";

            if (!File.Exists(filePath))
            {
                var defaultSettings = new SettingsService();
                defaultSettings.Save();
                return defaultSettings;
            }

            var jsonContent = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<SettingsService>(jsonContent) ?? new SettingsService();
        }

        public void Save()
        {
            var filePath = $@"{ServiceConfig.MainPath}\{FileName}";
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        // ⬇️ УПРОЩЕННЫЙ Update() - НЕТ КОНФЛИКТОВ ⬇️
        public static void Update()
        {
            // Просто перезагружаем экземпляр
            _instance = Load();
        }
    }
}