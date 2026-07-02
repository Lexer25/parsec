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

            Logger.Log<Service1>("Warning", $"MainPath: {MainPath}");

            factory = new StdSchedulerFactory();
        }

        protected override void OnStart(string[] args)
        {
            Logger.Log<Service1>("Warning", "OnStart");

            Task.Run(async () => {
                try
                {
                    string name = "parsec";
                    string password = "parsec";
                    string domain = "SYSTEM";

                    var igServ = new IntegrationService();
                    var result = igServ.OpenSession(domain, name, password);

                    if (result.Result != ClientState.Result_Success)
                    {
                        Logger.Log<Service1>("Error", $"Authorization error | {result.ErrorMessage}");
                        Logger.Log<Service1>("Error", "КРИТИЧЕСКАЯ ОШИБКА: Авторизация не удалась, планировщик не запущен");
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
                    Logger.Log<Service1>("Info", "Quartz scheduler started");

                    await scheduler.ScheduleJob(databaseJob, triggerPing);
                    Logger.Log<Service1>("Info", $"MainJob scheduled with interval {SettingsService.DatabaseJobTimeout} seconds");

                    await scheduler.ScheduleJob(continueSessionJob, continueSessionPing);
                    Logger.Log<Service1>("Info", "ContinueSessionJob scheduled");

                    Logger.Log<Service1>("Info", "=== SERVICE STARTED SUCCESSFULLY ===");
                }
                catch (Exception ex)
                {
                    Logger.Log<Service1>("Exception", $"OnStart error: {ex.Message}");
                    Logger.Log<Service1>("Error", $"КРИТИЧЕСКАЯ ОШИБКА при запуске: {ex.Message}");
                }
            }).Wait(TimeSpan.FromSeconds(30));
        }

        protected override void OnStop()
        {
            Logger.Log<Service1>("Warning", "104 Запрошена остановка службы");
			
			try{
				if (scheduler != null)
				{
					Logger.Log<Service1>("Warning","109 Остановка планировщика Quartz...");
					scheduler.Shutdown(waitForJobsToComplete: true).Wait(TimeSpan.FromSeconds(10));
					Logger.Log<Service1>("Warning", "111 Планировщик Quartz остановлен успешно");
				}
				var stopVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
				Logger.Log<Service1>("Warning",$"114 Служба завершена корректно (версия {stopVersion})");
			}catch(Exception ex)
			{
				Logger.Log<Service1>("Exception", $"117 Ошибка при остановке службы: {ex.Message}");
				Logger.Log<Service1>("Error", $"118 КРИТИЧЕСКАЯ ОШИБКА при остановке службы: {ex.Message}");
			}
        }
    }
}
