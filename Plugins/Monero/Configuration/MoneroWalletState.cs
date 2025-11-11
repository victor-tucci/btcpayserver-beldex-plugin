using System;

namespace BTCPayServer.Plugins.Monero.Configuration
{
    public class MoneroWalletState
    {
        public string ActiveWalletName { get; set; }

        public string ActiveWalletPassword { get; set; }

        public DateTimeOffset? LastActivatedAt { get; set; }

        public string LastActivatedByStoreId { get; set; }

        public bool IsInitialized => !string.IsNullOrEmpty(ActiveWalletName);

        public bool IsConnected { get; set; }
    }
}