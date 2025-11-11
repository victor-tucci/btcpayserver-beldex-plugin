using System.Collections.Generic;

namespace BTCPayServer.Plugins.Monero.Payments
{
    public class MoneroPaymentPromptDetails
    {
        public Dictionary<string, long> WalletAccountIndexes { get; set; } = [];

        public long AccountIndex { get; set; }

        public long? InvoiceSettledConfirmationThreshold { get; set; }

        public long GetAccountIndexForWallet(string walletName)
        {
            if (string.IsNullOrEmpty(walletName))
            {
                return 0;
            }

            return WalletAccountIndexes.TryGetValue(walletName, out var index) ? index : 0;
        }

        public void SetAccountIndexForWallet(string walletName, long accountIndex)
        {
            if (string.IsNullOrEmpty(walletName))
            {
                return;
            }

            WalletAccountIndexes[walletName] = accountIndex;
        }

        public bool RemoveWallet(string walletName)
        {
            if (string.IsNullOrEmpty(walletName))
            {
                return false;
            }

            return WalletAccountIndexes.Remove(walletName);
        }
    }
}