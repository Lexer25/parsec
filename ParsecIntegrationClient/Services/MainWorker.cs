using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ParsecIntegrationClient.Models;
using ParsecIntegrationClient.Services;

namespace ParsecIntegrationClient
{
    public class MainWorker : BackgroundService
    {
        private readonly ILogger<MainWorker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly SettingsService _settings;
        private readonly ParsecService _parsecService;
        private readonly DatabaseService _databaseService;
        private readonly StateService _stateService;
        private readonly LicenseService _licenseService;

        private bool _isAuthorized = false;
        private DateTime _lastErrorTime = DateTime.MinValue;
        private int _consecutiveErrors = 0;

        public MainWorker(
            ILogger<MainWorker> logger,
            IServiceProvider serviceProvider,
            SettingsService settings,
            ParsecService parsecService,
            DatabaseService databaseService,
            StateService stateService,
            LicenseService licenseService)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _settings = settings;
            _parsecService = parsecService;
            _databaseService = databaseService;
            _stateService = stateService;
            _licenseService = licenseService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogWarning("=== MAIN WORKER STARTED ===");

            // Первоначальная авторизация
            if (!await AuthorizeParsecAsync(stoppingToken))
            {
                _logger.LogError("КРИТИЧЕСКАЯ ОШИБКА: Авторизация не удалась. Работа остановлена.");
                return;
            }

            _logger.LogWarning($"Интервал выполнения: {_settings.DatabaseJobTimeout} секунд");
            _logger.LogWarning($"Таймаут ошибок: {_settings.ErrorTimeoutMinutes} минут");

            // Основной цикл
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Проверяем таймаут ошибок
                    if (_lastErrorTime != DateTime.MinValue)
                    {
                        var timeout = _lastErrorTime.AddMinutes(_settings.ErrorTimeoutMinutes);
                        if (DateTime.Now < timeout)
                        {
                            var remaining = (timeout - DateTime.Now).TotalSeconds.ToString("F0");
                            _logger.LogWarning($"Таймаут ошибок активен. Осталось {remaining} секунд");
                            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                            continue;
                        }
                        else
                        {
                            _logger.LogInformation("Таймаут ошибок истек, возобновляем работу");
                            _lastErrorTime = DateTime.MinValue;
                            _consecutiveErrors = 0;
                        }
                    }

                    // Проверка лицензии
                    if (!_licenseService.IsLicensed())
                    {
                        _logger.LogError("ЛИЦЕНЗИЯ ОТСУТСТВУЕТ: Превышен лимит бесплатных транзакций");
                        
                        var state = new State
                        {
                            Status = "ERR",
                            desc = "ЛИЦЕНЗИЯ ОТСУТСТВУЕТ: Превышен лимит бесплатных транзакций",
                            ErrorMessage = "Лимит бесплатных транзакций (10) исчерпан",
                            Timestamp = DateTime.Now,
                            NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes),
                            keyNum = Key.keyNumber
                        };
                        _stateService.SaveState(state);
                        
                        _lastErrorTime = DateTime.Now;
                        await Task.Delay(TimeSpan.FromMinutes(_settings.ErrorTimeoutMinutes), stoppingToken);
                        continue;
                    }

                    // Основная обработка
                    await ProcessTasksAsync(stoppingToken);

                    // Сбрасываем счетчик ошибок при успешном выполнении
                    _consecutiveErrors = 0;

                    // Ожидание до следующего выполнения
                    await Task.Delay(TimeSpan.FromSeconds(_settings.DatabaseJobTimeout), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Основной цикл остановлен по запросу");
                    break;
                }
                catch (Exception ex)
                {
                    _consecutiveErrors++;
                    _lastErrorTime = DateTime.Now;
                    _logger.LogError(ex, $"Ошибка в основном цикле (попытка {_consecutiveErrors})");

                    // Экспоненциальная задержка при ошибках
                    var delay = Math.Min(_consecutiveErrors * 5, 60);
                    _logger.LogWarning($"Ожидание {delay} секунд перед следующей попыткой");
                    await Task.Delay(TimeSpan.FromSeconds(delay), stoppingToken);
                }
            }

            _logger.LogWarning("=== MAIN WORKER STOPPED ===");
        }

        private async Task<bool> AuthorizeParsecAsync(CancellationToken cancellationToken)
        {
            _logger.LogWarning("Авторизация в Parsec...");

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    var result = _parsecService.OpenSession("SYSTEM", "parsec", "parsec");

                    if (result.Result == ClientState.Result_Success)
                    {
                        ClientState.SetSession(result.Value, "SYSTEM", "parsec");
                        _isAuthorized = true;
                        _logger.LogWarning($"Авторизация успешна (Session: {result.Value})");
                        return true;
                    }
                    else
                    {
                        _logger.LogError($"Авторизация не удалась (попытка {attempt}): {result.ErrorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Исключение при авторизации (попытка {attempt})");
                }

                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), cancellationToken);
            }

            _isAuthorized = false;
            return false;
        }

        private async Task ProcessTasksAsync(CancellationToken cancellationToken)
        {
            // Проверяем, не нужно ли обновить сессию
            if (!_isAuthorized)
            {
                _logger.LogWarning("Сессия не активна, попытка переавторизации...");
                await AuthorizeParsecAsync(cancellationToken);
                if (!_isAuthorized)
                {
                    _logger.LogError("Переавторизация не удалась. Пропуск цикла.");
                    return;
                }
            }

            _logger.LogInformation("=== НАЧАЛО ОБРАБОТКИ ЗАДАЧ ===");

            // Получение задач из БД
            var rows = _databaseService.GetList<DbModelRowIDInDev>(_settings.QuerySelectIdDevCardString)
                .OrderBy(x => Convert.ToInt32(x.ID))
                .ToArray();

            if (rows.Length == 0)
            {
                _logger.LogInformation("Нет задач для обработки");
                return;
            }

            _logger.LogInformation($"Получено {rows.Length} задач для выполнения");

            // Проверка глобального таймаута ошибок
            if (await IsErrorTimeoutActive())
            {
                return;
            }

            // Обработка каждой задачи
            int processed = 0;
            int errors = 0;

            foreach (var row in rows)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                // Проверка лицензии перед каждой транзакцией
                if (!_licenseService.CanPerformOperation())
                {
                    _logger.LogError($"ЛИЦЕНЗИЯ ОТСУТСТВУЕТ: Задача {row.ID} пропущена. Осталось транзакций: {_licenseService.RemainingTransactions}");
                    _databaseService.DeleteIdInDevById(row.ID);
                    continue;
                }

                // Предварительная валидация
                if (!ValidateOperation(row, out string validationError))
                {
                    var errorState = CreateErrorState(row, validationError);
                    _stateService.SaveState(errorState);
                    _logger.LogError($"ОШИБКА ВАЛИДАЦИИ: {validationError} (Задача {row.ID})");
                    
                    _databaseService.DeleteIdInDevById(row.ID);
                    continue;
                }

                // Выполнение операции
                _logger.LogInformation($"Обработка задачи {row.ID} (операция {row.OPERATION}). Осталось транзакций: {_licenseService.RemainingTransactions}");
                var result = ExecuteOperation(row);
                _stateService.SaveState(result);

                if (result.Status == "OK")
                {
                    _databaseService.DeleteIdInDevById(row.ID);
                    processed++;
                    _logger.LogInformation($"Задача {row.ID} выполнена успешно");
                }
                else if (result.Status == "SKIP")
                {
                    _databaseService.DeleteIdInDevById(row.ID);
                    _logger.LogInformation($"Задача {row.ID} пропущена: {result.desc}");
                }
                else // ERR
                {
                    errors++;
                    
                    if (_settings.SkipErrCode != null && _settings.SkipErrCode.Contains(result.ErrorCode))
                    {
                        _databaseService.DeleteIdInDevById(row.ID);
                        _logger.LogInformation($"Задача {row.ID} удалена (код ошибки {result.ErrorCode} в списке пропуска)");
                    }
                    else
                    {
                        _databaseService.IncrementAttemp(row);
                        _lastErrorTime = DateTime.Now;
                        
                        _logger.LogError($"Ошибка в задаче {row.ID}: {result.ErrorMessage} (Код: {result.ErrorCode})");
                        _logger.LogWarning($"ГЛОБАЛЬНАЯ ОСТАНОВКА: Обработка прекращена. Следующая попытка через {_settings.ErrorTimeoutMinutes} минут");
                        
                        // Выходим из цикла при критической ошибке
                        return;
                    }
                }
            }

            _logger.LogInformation($"=== ОБРАБОТКА ЗАВЕРШЕНА: успешно {processed}, ошибок {errors} ===");
        }

        private bool ValidateOperation(DbModelRowIDInDev row, out string error)
        {
            error = null;

            if (!int.TryParse(row.OPERATION, out int op) || op < 1 || op > 10)
            {
                error = $"Некорректный код операции: {row.OPERATION} (ожидается 1-10)";
                return false;
            }

            // Для операций 4 и 6 проверяем GUID
            if ((op == 4 || op == 6) && !Guid.TryParse(row.ID_CARD, out _))
            {
                error = $"Для операции {op} ожидается GUID в ID_CARD, получено: '{row.ID_CARD}'";
                return false;
            }

            return true;
        }

        private State ExecuteOperation(DbModelRowIDInDev row)
        {
            try
            {
                return row.OPERATION switch
                {
                    "1" => _parsecService.AddCardPeople(row),     // Добавление карты
                    "2" => _parsecService.RemoveCardPeople(row),  // Удаление карты
                    "3" => _parsecService.AddPeople(row),         // Добавление человека
                    "4" => _parsecService.RemovePeople(row),      // Удаление человека
                    "5" => _parsecService.AddOrg(row),            // Добавление организации
                    "6" => _parsecService.RemoveOrg(row),         // Удаление организации
                    "7" => _parsecService.AddIdentifierPeople(row), // Добавление категории доступа
                    "8" => _parsecService.RemoveIdentifierPeople(row), // Удаление категории доступа
                    "9" => _parsecService.AddCardPeople(row),     // Добавление карты человеку
                    "10" => _parsecService.RemoveCardPeople(row), // Удаление карты у человека
                    _ => new State 
                    { 
                        Status = "SKIP", 
                        desc = $"Операция {row.OPERATION} не реализована",
                        IdCardindev = row.ID,
                        Attempts = row.ATTEMPS,
                        Timestamp = DateTime.Now
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Исключение при выполнении операции {row.OPERATION} для задачи {row.ID}");
                return new State
                {
                    IdCardindev = row.ID,
                    Status = "ERR",
                    desc = $"Исключение: {ex.Message}",
                    ErrorMessage = ex.Message,
                    Attempts = row.ATTEMPS,
                    Timestamp = DateTime.Now,
                    NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes),
                    ErrorCode = 999,
                    keyNum = Key.keyNumber
                };
            }
        }

        private async Task<bool> IsErrorTimeoutActive()
        {
            var state = _stateService.LoadState();
            if (state?.Status == "ERR" && state.Timestamp.HasValue)
            {
                var timeout = state.Timestamp.Value.AddMinutes(_settings.ErrorTimeoutMinutes);
                if (DateTime.Now < timeout)
                {
                    var remaining = (timeout - DateTime.Now).TotalSeconds.ToString("F0");
                    _logger.LogWarning($"Таймаут ошибок активен (из state.json). Осталось: {remaining} секунд");
                    return true;
                }
            }
            return false;
        }

        private State CreateErrorState(DbModelRowIDInDev row, string error)
        {
            return new State
            {
                IdCardindev = row.ID,
                Status = "ERR",
                desc = error,
                ErrorMessage = error,
                Attempts = row.ATTEMPS,
                Timestamp = DateTime.Now,
                Operation = StateService.GetOperationName(row.OPERATION),
                OperationCode = row.OPERATION,
                NextStart = DateTime.Now.AddMinutes(_settings.ErrorTimeoutMinutes),
                ErrorCode = 100,
                keyNum = Key.keyNumber
            };
        }
    }
}