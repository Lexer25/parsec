using ParsecIntegrationClient.IntegrationWebService;
using ParsecIntegrationClient.Models;
using Quartz;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParsecIntegrationClient.Services
{
    public class ContinueSessionJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            await Task.Run(() => {
                var integServ = new IntegrationService();
                integServ.ContinueSession(ClientState.SessionID);
            });
        }
    }
}
