using System;
using Guardant;
using ParsecIntegrationClient.Models;

namespace ParsecIntegrationClient.Services
{
    public class GuardantService
    {
        private readonly LicenseService _licenseService;
        private readonly Logger _logger;

        // Константы из Program.cs
        private const uint MY_PROG_NUMBER = 1;
        private const uint MY_KEY_ID = 0;
        private const uint MY_KEY_SN = 5;
        private const uint MY_KEY_VER = 2;
        private static ushort _myKeyMask = 0;

        public GuardantService(LicenseService licenseService, Logger logger)
        {
            _licenseService = licenseService;
            _logger = logger;
        }

        public bool CheckGuardantKey()
        {
            Handle guardantHandle = null;
            bool isApiInitialized = false;
            bool keyFound = false;

            try
            {
                _logger.Log<GuardantService>("Warning", "Guardant: Начало проверки ключа...");

                // 1. Инициализация API
                GrdE result = GrdApi.GrdStartup(GrdFMR.Local);

                if (result != GrdE.OK)
                {
                    _logger.Log<GuardantService>("Error", $"Guardant: Ошибка инициализации API: {result}");
                    _logger.Log<GuardantService>("Warning", "Guardant: Работа в ограниченном режиме (10 транзакций)");
                    _licenseService.SetLicenseStatus(false);
                    return true;
                }
                isApiInitialized = true;
                _logger.Log<GuardantService>("Warning", "Guardant: API инициализирован");

                // 2. Создаём хэндл
                guardantHandle = GrdApi.GrdCreateHandle(GrdCHM.MultiThread);
                _logger.Log<GuardantService>("Warning", "Guardant: Хэндл создан");

                // Устанавливаем коды доступа
                result = GrdApi.GrdSetAccessCodes(guardantHandle, 0x4651A7A2);
                _logger.Log<GuardantService>("Warning", $"Guardant: GrdSetAccessCodes result = {result}");

                // 3. Устанавливаем критерии поиска
                result = GrdApi.GrdSetFindMode(
                    guardantHandle,
                    GrdFMR.Local,
                    GrdFM.Ver,
                    MY_PROG_NUMBER,
                    MY_KEY_ID,
                    MY_KEY_SN,
                    MY_KEY_VER,
                    _myKeyMask,
                    GrdDT.ALL,
                    GrdFMM.SignUSB,
                    GrdFMI.USB);

                _logger.Log<GuardantService>("Warning", $"Guardant: GrdSetFindMode result = {result}");

                if (result != GrdE.OK)
                {
                    _logger.Log<GuardantService>("Error", $"Guardant: Ошибка установки критериев: {result}");
                    _logger.Log<GuardantService>("Warning", "Guardant: Работа в ограниченном режиме (10 транзакций)");
                    _licenseService.SetLicenseStatus(false);
                    return true;
                }
                _logger.Log<GuardantService>("Warning", "Guardant: Критерии поиска установлены");

                // 4. Ищем ключ
                uint foundId;
                FindInfo findInfo;
                result = GrdApi.GrdFind(guardantHandle, GrdF.First, out foundId, out findInfo);

                while (result == GrdE.OK)
                {
                    string keyInfo = string.Format(
                        "{0,8:X} {1,5:X} {2,3:D} {3,5:X} {4,8:X} {5,4:D} {6,3:D} {7,5:D} {8,5:X} {9,5:D} {10,6:D} {11,8:X}",
                        findInfo.dwPublicCode, findInfo.byHrwVersion, findInfo.byMaxNetRes,
                        findInfo.wType, findInfo.dwID, findInfo.byNProg, findInfo.byVer,
                        findInfo.wSN, findInfo.wMask, findInfo.wGP, findInfo.wRealNetRes, findInfo.dwIndex);

                    _logger.Log<GuardantService>("Info", $"Guardant: Найден ключ: {keyInfo}");

                    Key.keyNumber = keyInfo;

                    // Проверяем, подходит ли ключ по программе и версии
                    if (findInfo.byNProg == MY_PROG_NUMBER && findInfo.byVer == MY_KEY_VER)
                    {
                        _myKeyMask = findInfo.wMask;
                        keyFound = true;
                        _logger.Log<GuardantService>("Warning", $"Guardant: Найден подходящий ключ! Маска: {_myKeyMask:X4}");
                        break;
                    }

                    result = GrdApi.GrdFind(guardantHandle, GrdF.Next, out foundId, out findInfo);
                }

                if (keyFound)
                {
                    // Проверяем крайний правый бит маски
                    uint rightmostBit = (uint)_myKeyMask & 1;
                    
                    if (rightmostBit == 1)
                    {
                        _licenseService.SetLicenseStatus(true);
                        _logger.Log<GuardantService>("Warning", "Guardant: ПОЛНАЯ ЛИЦЕНЗИЯ (маска xxx1)");
                    }
                    else
                    {
                        _licenseService.SetLicenseStatus(false);
                        _logger.Log<GuardantService>("Warning", $"Guardant: ОГРАНИЧЕННЫЙ РЕЖИМ (маска: {_myKeyMask:X4}, бит = 0)");
                    }
                }
                else
                {
                    _licenseService.SetLicenseStatus(false);
                    _logger.Log<GuardantService>("Warning", "Guardant: Подходящий ключ не найден. ОГРАНИЧЕННЫЙ РЕЖИМ (10 транзакций)");
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Log<GuardantService>("Exception", $"Guardant: Исключение: {ex.Message}");
                _logger.Log<GuardantService>("Warning", "Guardant: Исключение при проверке ключа. ОГРАНИЧЕННЫЙ РЕЖИМ (10 транзакций)");
                _licenseService.SetLicenseStatus(false);
                return true;
            }
            finally
            {
                try
                {
                    if (isApiInitialized)
                    {
                        GrdApi.GrdCleanup();
                        _logger.Log<GuardantService>("Warning", "Guardant: API деинициализирован");
                    }
                }
                catch { /* ignore */ }
            }
        }
    }
}