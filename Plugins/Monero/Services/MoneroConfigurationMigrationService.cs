using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Logging;
using BTCPayServer.Plugins.Monero.Configuration;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


namespace BTCPayServer.Plugins.Monero.Services
{
    public class MoneroConfigurationMigrationService : IHostedService
    {
        private const string CryptoCode = "XMR";
        private const string WalletStateMigrationKey = "MoneroWalletStateMigration";

        private readonly ISettingsRepository _settingsRepository;
        private readonly MoneroRPCProvider _rpcProvider;
        private readonly Logs _logs;

        public MoneroConfigurationMigrationService(
            ISettingsRepository settingsRepository,
            MoneroRPCProvider rpcProvider,
            Logs logs)
        {
            _settingsRepository = settingsRepository;
            _rpcProvider = rpcProvider;
            _logs = logs;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await MigrateWalletState();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private async Task MigrateWalletState()
        {
            try
            {
                var migrationStatus = await _settingsRepository.GetSettingAsync<MigrationStatus>(WalletStateMigrationKey);
                if (migrationStatus?.Complete == true)
                {
                    _logs.PayServer.LogDebug("Wallet migration already completed");
                    return;
                }

                _logs.PayServer.LogInformation("Starting wallet migration");

                string walletDir = _rpcProvider.GetWalletDirectory(CryptoCode);
                if (string.IsNullOrEmpty(walletDir))
                {
                    _logs.PayServer.LogInformation("No wallet directory configured, skipping wallet migration");
                    await _settingsRepository.UpdateSetting(new MigrationStatus { Complete = true }, WalletStateMigrationKey);
                    return;
                }

                string passwordFile = Path.Combine(walletDir, "password");
                if (!File.Exists(passwordFile))
                {
                    _logs.PayServer.LogInformation("No password file found, skipping wallet migration");
                    await _settingsRepository.UpdateSetting(new MigrationStatus { Complete = true }, WalletStateMigrationKey);
                    return;
                }

                string[] availableWallets = _rpcProvider.GetWalletList(CryptoCode);
                if (availableWallets is null or { Length: 0 })
                {
                    _logs.PayServer.LogWarning("Password file found but no wallet files exist");
                    await _settingsRepository.UpdateSetting(new MigrationStatus { Complete = true }, WalletStateMigrationKey);
                    return;
                }

                string password = await File.ReadAllTextAsync(passwordFile);
                password = password.Trim();
                string walletName = availableWallets.First();

                bool opened = await _rpcProvider.OpenWallet(CryptoCode, walletName, password);
                if (!opened)
                {
                    _logs.PayServer.LogWarning($"Failed to open wallet '{walletName}' during migration - password may be incorrect");
                    await _settingsRepository.UpdateSetting(new MigrationStatus { Complete = true }, WalletStateMigrationKey);
                    return;
                }

                await _rpcProvider.CloseWallet(CryptoCode);

                var walletState = new MoneroWalletState
                {
                    ActiveWalletName = walletName,
                    ActiveWalletPassword = password,
                    LastActivatedAt = DateTimeOffset.UtcNow,
                    LastActivatedByStoreId = "migration",
                    IsConnected = false
                };
                await _settingsRepository.UpdateSetting(walletState);

                _logs.PayServer.LogInformation($"Successfully migrated legacy wallet '{walletName}'");

                await _settingsRepository.UpdateSetting(new MigrationStatus { Complete = true }, WalletStateMigrationKey);
            }
            catch (Exception ex)
            {
                _logs.PayServer.LogError(ex, "Error during wallet migration");
                await _settingsRepository.UpdateSetting(new MigrationStatus { Complete = true }, WalletStateMigrationKey);
            }
        }

        private class MigrationStatus
        {
            public bool Complete { get; set; }
        }
    }
}