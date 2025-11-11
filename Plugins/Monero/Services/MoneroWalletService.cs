using System;
using System.Threading;
using System.Threading.Tasks;

using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Logging;
using BTCPayServer.Plugins.Monero.Configuration;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Monero.Services
{
    public class MoneroWalletService : IHostedService
    {
        private const string CryptoCode = "XMR";
        private readonly MoneroRPCProvider _rpcProvider;
        private readonly Logs _logs;
        private readonly ISettingsRepository _settingsRepository;
        private MoneroWalletState _walletState;

        public MoneroWalletService(
            MoneroRPCProvider rpcProvider,
            Logs logs,
            ISettingsRepository settingsRepository)
        {
            _rpcProvider = rpcProvider;
            _logs = logs;
            _settingsRepository = settingsRepository;
            _walletState = new MoneroWalletState();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!_rpcProvider.IsConfigured(CryptoCode))
                {
                    _logs.PayServer.LogWarning($"{CryptoCode} RPC not configured");
                    return;
                }

                var savedState = await _settingsRepository.GetSettingAsync<MoneroWalletState>();

                if (savedState?.IsInitialized != true)
                {
                    return;
                }

                _walletState = savedState;

                var result = await _rpcProvider.OpenWallet(CryptoCode, _walletState.ActiveWalletName, _walletState.ActiveWalletPassword);

                if (result)
                {
                    _walletState.IsConnected = true;
                    _logs.PayServer.LogInformation($"Successfully opened wallet '{_walletState.ActiveWalletName}' on startup");
                }
            }
            catch (Exception ex)
            {
                _logs.PayServer.LogError(ex, $"Error during {CryptoCode} wallet startup");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_walletState.IsConnected)
                {
                    await _rpcProvider.CloseWallet(CryptoCode);
                    _walletState.IsConnected = false;
                }
            }
            catch (Exception ex)
            {
                _logs.PayServer.LogError(ex, $"Error closing wallet during shutdown");
            }
        }

        public async Task<bool> SetActiveWallet(string walletName, string walletPassword, string changedByStoreId)
        {
            try
            {
                if (_walletState.IsConnected)
                {
                    await _rpcProvider.CloseWallet(CryptoCode);
                    _walletState.IsConnected = false;
                }

                await _rpcProvider.OpenWallet(CryptoCode, walletName, walletPassword);

                _walletState.ActiveWalletName = walletName;
                _walletState.ActiveWalletPassword = walletPassword;
                _walletState.LastActivatedAt = DateTimeOffset.UtcNow;
                _walletState.LastActivatedByStoreId = changedByStoreId;
                _walletState.IsConnected = true;

                var stateToSave = new MoneroWalletState
                {
                    ActiveWalletName = walletName,
                    ActiveWalletPassword = walletPassword,
                    LastActivatedAt = DateTimeOffset.UtcNow,
                    LastActivatedByStoreId = changedByStoreId,
                    IsConnected = true
                };
                await _settingsRepository.UpdateSetting(stateToSave);
                await _rpcProvider.UpdateSummary(CryptoCode);
                _logs.PayServer.LogInformation($"Active wallet changed to '{walletName}' by store {changedByStoreId}");
                return true;
            }
            catch (Exception ex)
            {
                _logs.PayServer.LogError(ex, $"Error setting active wallet to '{walletName}'");
                return false;
            }
        }

        public async Task<bool> CloseActiveWallet()
        {
            try
            {
                var result = await _rpcProvider.CloseWallet(CryptoCode);
                if (result)
                {
                    _logs.PayServer.LogInformation($"Closed wallet '{_walletState.ActiveWalletName}'");
                    _walletState.IsConnected = false;
                    return true;
                }
                else
                {
                    _logs.PayServer.LogError($"Failed to close wallet '{_walletState.ActiveWalletName}'");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logs.PayServer.LogError(ex, "Error closing active wallet");
                return false;
            }
        }

        public MoneroWalletState GetWalletState()
        {
            return _walletState;
        }

        public async Task<bool> ClearWalletState()
        {
            try
            {
                var emptyState = new MoneroWalletState
                {
                    ActiveWalletName = null,
                    ActiveWalletPassword = null,
                    LastActivatedAt = null,
                    LastActivatedByStoreId = null,
                    IsConnected = false
                };

                await _settingsRepository.UpdateSetting(emptyState);
                _walletState = emptyState;

                return true;
            }
            catch (Exception ex)
            {
                _logs.PayServer.LogError(ex, "Error clearing wallet state");
                return false;
            }
        }

        public async Task<(bool Success, string ErrorMessage)> CreateAndActivateWallet(
            string walletName,
            string primaryAddress,
            string privateViewKey,
            string password,
            int restoreHeight,
            string createdByStoreId)
        {
            try
            {
                _logs.PayServer.LogInformation($"Creating and activating wallet '{walletName}' for store {createdByStoreId}");

                var (createSuccess, createError) = await _rpcProvider.CreateWalletFromKeys(
                    CryptoCode,
                    walletName,
                    primaryAddress,
                    privateViewKey,
                    password,
                    restoreHeight);

                if (!createSuccess)
                {
                    _logs.PayServer.LogError($"Failed to create wallet '{walletName}': {createError}");
                    return (false, createError);
                }

                _logs.PayServer.LogInformation($"Successfully created wallet '{walletName}'");

                _walletState.ActiveWalletName = walletName;
                _walletState.ActiveWalletPassword = password;
                _walletState.LastActivatedAt = DateTimeOffset.UtcNow;
                _walletState.LastActivatedByStoreId = createdByStoreId;
                _walletState.IsConnected = true;

                var stateToSave = new MoneroWalletState
                {
                    ActiveWalletName = walletName,
                    ActiveWalletPassword = password,
                    LastActivatedAt = DateTimeOffset.UtcNow,
                    LastActivatedByStoreId = createdByStoreId,
                    IsConnected = true
                };
                await _settingsRepository.UpdateSetting(stateToSave);
                _logs.PayServer.LogInformation($"Active wallet changed to '{walletName}' by store {createdByStoreId}");

                try
                {
                    await _rpcProvider.UpdateSummary(CryptoCode);
                    _logs.PayServer.LogInformation($"Summary updated after wallet creation for '{walletName}'");
                }
                catch (Exception summaryEx)
                {
                    _logs.PayServer.LogWarning(summaryEx, $"Failed to update summary after wallet creation, will update on next cycle");
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                _logs.PayServer.LogError(ex, $"Error creating and activating wallet '{walletName}'");
                return (false, ex.Message);
            }
        }

    }
}