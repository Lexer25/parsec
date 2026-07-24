using System.Collections.Generic;

namespace ParsecIntegrationClient.Models
{
    public class Settings
    {
        public string LogLevel { get; set; } = "Warning";
        public string DatabaseConnectionString { get; set; } = "User = SYSDBA; Password = temp; Database = C:\\Program Files (x86)\\Cardsoft\\DuoSE\\Access\\ShieldPro_rest.gdb; DataSource = 127.0.0.1; Port = 3050; Dialect = 3; Charset = win1251; Role =; Connection lifetime = 15; Pooling = true; MinPoolSize = 0; MaxPoolSize = 50; Packet Size = 8192; ServerType = 0;";
        public string QuerySelectIdDevCardString { get; set; } = "select cd.id_cardindev as id, cd.id_card, cd.id_pep, cd.operation, cd.attempts from cardindev cd where ((cd.id_dev in (select d.id_dev from device d join device d2 on d2.id_ctrl=d.id_ctrl and d2.id_reader is null join servertypelist stl on d2.id_server=stl.id_server join servertype st on st.id=stl.id_type and st.sname='parsec' where d.id_reader is not null)) or(cd.id_dev is null)) and cd.attempts<20 order by cd.id_cardindev";
        public int DatabaseJobTimeout { get; set; } = 60;
        public int ErrorTimeoutMinutes { get; set; } = 1;
        public List<int> SkipErrCode { get; set; } = new List<int>();
    }
}