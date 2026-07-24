using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ParsecIntegrationClient.Services;

namespace ParsecIntegrationClient
{
    public class SessionKeepAliveService : BackgroundService
    {
        private readonly ILogger<SessionKeepAliveService> _logger;
        private readonly ParsecService _parsecService;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(4);

        public SessionKeepAliveService(
            ILogger<SessionKeepAliveService> logger,
            ParsecService parsecService)
        {
            _logger = logger;
            _parsecService = parsecService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("=== SESSION KEEP ALIVE STARTED ===");
            _logger.LogInformation($"Интервал продления: {_interval.TotalMinutes} минут");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_interval, stoppingToken);

                    if (ClientState.SessionID != Guid.Empty)
                    {
                        _parsecService.ContinueSession(ClientState.SessionID);
                        _logger.LogDebug($"Сессия {ClientState.SessionID} продлена");
                    }
                    else
                    {
                        _logger.LogWarning("Сессия не активна (SessionID пустой)");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при продлении сессии");
                }
            }

            _logger.LogInformation("=== SESSION KEEP ALIVE STOPPED ===");
        }
    }
}