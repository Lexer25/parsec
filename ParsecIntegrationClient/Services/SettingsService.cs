using Newtonsoft.Json;
using System.IO;

namespace ParsecIntegrationClient.Services
{
    public class SettingsService
    {
        private static string fileName = "appsettings.json";

        public string _databaseConnectionString
        {
            get { return DatabaseConnectionString; }
            set { DatabaseConnectionString = value; }
        }

        public static string DatabaseConnectionString = "User = SYSDBA; Password = temp; " +
            "Database = C:\\Program Files (x86)\\Cardsoft\\DuoSE\\Access\\ShieldPro_rest.gdb; " +
            "DataSource = 127.0.0.1; Port = 3050; Dialect = 3; Charset = win1251; Role =; " +
            "Connection lifetime = 15; Pooling = true; MinPoolSize = 0; MaxPoolSize = 50; " +
            "Packet Size = 8192; ServerType = 0;";

        public string _querySelectIdDevCardString
        {
            get { return QuerySelectIdDevCardString; }
            set { QuerySelectIdDevCardString = value; }
        }

        public static string QuerySelectIdDevCardString = "select cd.id_cardindev" +
            " as id, cd.id_card, cd.id_pep, cd.operation, cd.attempts from " +
            "cardindev cd where ((cd.id_dev in (select d.id_dev from device d " +
            "join device d2 on d2.id_ctrl=d.id_ctrl and d2.id_reader is null " +
            "join servertypelist stl on d2.id_server=stl.id_server " +
            "join servertype st on st.id=stl.id_type and st.sname='parsec'" +
            " where d.id_reader is not null)) or(cd.id_dev is null)) " +
            "and cd.attempts<20";


        public int _databaseJobTimeout
        {
            get { return DatabaseJobTimeout; }
            set { DatabaseJobTimeout = value; }
        }

        public static int DatabaseJobTimeout = 60;


        public static void Update()
        {
            if (!File.Exists($@"{Service1.MainPath}\{fileName}"))
            {
                string json = JsonConvert.SerializeObject(new SettingsService());
                File.WriteAllText($@"{Service1.MainPath}\{fileName}", json);
            }
            else
            {
                var json = File.ReadAllText($@"{Service1.MainPath}\{fileName}");
                var settings = JsonConvert.DeserializeObject<SettingsService>(json);

                DatabaseConnectionString = settings._databaseConnectionString;
                DatabaseJobTimeout = settings._databaseJobTimeout;
                QuerySelectIdDevCardString = settings._querySelectIdDevCardString;
            }
        }
    }
}
