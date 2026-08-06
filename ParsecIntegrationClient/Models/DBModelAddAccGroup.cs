using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParsecIntegrationClient.Models
{
    internal class DbModelAddAccessCategory
    {

        //Кому добавлять
        public string GUID_PEP { get; set; }
        
        //что добавлять
        public string GUID_ACCGROUP { get; set; }
        
    }
}
