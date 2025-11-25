using ParsecIntegrationClient.IntegrationWebService;
using ParsecIntegrationClient.Models;
using ParsecIntegrationClient.Services;
using Quartz.Impl;
using Quartz;
using System;
using System.ServiceProcess;
using System.IO;
using System.Threading.Tasks;

namespace ParsecIntegrationClient
{
    public partial class Service1 : ServiceBase
    {

        public static string MainPath = "C:\\IntegrationClient";

        StdSchedulerFactory factory = null;
        IScheduler scheduler = null;

        IJobDetail databaseJob = null;
        IJobDetail continueSessionJob = null;

        ITrigger triggerPing = null;
        ITrigger continueSessionPing = null;

        public Service1()
        {
            InitializeComponent();

            if (!Directory.Exists($@"{MainPath}\log"))
                Directory.CreateDirectory($@"{MainPath}\log");

            SettingsService.Update();

            Logger.Log<Service1>("Info", $"MainPath: {MainPath}");

            factory = new StdSchedulerFactory();
        }

        protected async override void OnStart(string[] args)
        {
            Logger.Log<Service1>("Info", "OnStart");

            await Task.Run(async () => {
                try
                {
                    string name = "parsec";
                    string password = "parsec";
                    string domain = "SYSTEM";

                    var igServ = new IntegrationService();
                    var result = igServ.OpenSession(domain, name, password);

                    if (result.Result != ClientState.Result_Success)
                    {
                        Logger.Log<Service1>("Info", $"Authorization error | {result.ErrorMessage}");
                        return;
                    }
                    else
                    {
                        ClientState.SetSession(result.Value, domain, name);
                        Logger.Log<Service1>("Info", $"Authorization ok | {result.Value}");
                    }
                    


                    databaseJob = JobBuilder.Create<MainJob>().Build();
                    triggerPing = TriggerBuilder.Create()
                           .StartNow()
                           .WithSimpleSchedule(x => x
                               .WithIntervalInSeconds(SettingsService.DatabaseJobTimeout)
                               .RepeatForever())
                           .Build();

                    continueSessionJob = JobBuilder.Create<ContinueSessionJob>().Build();
                    continueSessionPing = TriggerBuilder.Create()
                          .WithSimpleSchedule(x => x
                              .WithIntervalInSeconds(4 * 60)
                              .RepeatForever())
                          .Build();

                    scheduler = await factory.GetScheduler();
                    await scheduler.Start();
                    await scheduler.ScheduleJob(databaseJob, triggerPing);
                    await scheduler.ScheduleJob(continueSessionJob, continueSessionPing);
                }
                catch (Exception ex)
                {
                    Logger.Log<Service1>("Exception", $"{ex.Message}");
                }
            });
        }

        protected override void OnStop()
        {
            Logger.Log<Service1>("Info", "OnStop");
        }
    }
}
