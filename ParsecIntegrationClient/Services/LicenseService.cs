using System;
using ParsecIntegrationClient.Models;

namespace ParsecIntegrationClient.Services
{
    public class LicenseService
    {
        private readonly object _lock = new object();
        private bool _isLicensed = false;
        private int _remainingTransactions = 10;
        private const int MAX_FREE_TRANSACTIONS = 10;

        public bool IsLicensed()
        {
            lock (_lock)
            {
                return _isLicensed;
            }
        }

        public int RemainingTransactions => _remainingTransactions;

        public bool CanPerformOperation()
        {
            lock (_lock)
            {
                if (_isLicensed)
                    return true;

                if (_remainingTransactions > 0)
                    return true;

                return false;
            }
        }

        public void SetLicenseStatus(bool licensed)
        {
            lock (_lock)
            {
                _isLicensed = licensed;
                if (licensed)
                {
                    _remainingTransactions = int.MaxValue;
                }
                else
                {
                    _remainingTransactions = MAX_FREE_TRANSACTIONS;
                }
                
                Logger.Log<LicenseService>("Warning", $"Лицензия установлена: {(_isLicensed ? "ПОЛНАЯ" : "ОГРАНИЧЕННАЯ")}, осталось транзакций: {_remainingTransactions}");
            }
        }

        public void UseTransaction()
        {
            lock (_lock)
            {
                if (!_isLicensed && _remainingTransactions > 0)
                {
                    _remainingTransactions--;
                    Logger.Log<LicenseService>("Info", $"Использована транзакция. Осталось: {_remainingTransactions}");
                }
            }
        }

        public void ResetTransactions()
        {
            lock (_lock)
            {
                if (!_isLicensed)
                {
                    _remainingTransactions = MAX_FREE_TRANSACTIONS;
                }
            }
        }
    }
}