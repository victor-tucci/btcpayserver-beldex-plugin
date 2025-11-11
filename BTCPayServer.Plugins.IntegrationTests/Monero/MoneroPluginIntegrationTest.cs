using System.Diagnostics;

using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Plugins.Monero.Configuration;
using BTCPayServer.Plugins.Monero.Services;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using BTCPayServer.Tests;
using BTCPayServer.Tests.Mocks;

using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.IntegrationTests.Monero;

public class MoneroPluginIntegrationTest(ITestOutputHelper helper) : MoneroAndBitcoinIntegrationTestBase(helper)
{
    #region Methods

    private async Task CreateWalletViaForm(
        PlaywrightTester s,
        string walletName,
        string address,
        string viewKey,
        string password,
        string restoreHeight)
    {
        await s.Page.FillAsync("input#WalletName", walletName);
        await s.Page.FillAsync("input#PrimaryAddress", address);
        await s.Page.FillAsync("input#PrivateViewKey", viewKey);
        await s.Page.FillAsync("input#WalletPassword", password);
        await s.Page.FillAsync("input#RestoreHeight", restoreHeight);
        await s.Page.ClickAsync("button[name='command'][value='connect-wallet']");
        await s.Page.Locator($".wallet-card[data-wallet='{walletName}']").WaitForAsync();
    }

    private async Task CreateWalletViaModal(
        PlaywrightTester s,
        string walletName,
        string address,
        string viewKey,
        string password,
        string restoreHeight)
    {
        await s.Page.FillAsync("#createWalletModal input#WalletName", walletName);
        await s.Page.FillAsync("#createWalletModal input#PrimaryAddress", address);
        await s.Page.FillAsync("#createWalletModal input#PrivateViewKey", viewKey);
        await s.Page.FillAsync("#createWalletModal input#WalletPassword", password);
        await s.Page.FillAsync("#createWalletModal input#RestoreHeight", restoreHeight);
        await s.Page.ClickAsync("#createWalletModal button[name='command'][value='replace-wallet']");
        await s.Page.Locator($".wallet-card[data-wallet='{walletName}']").WaitForAsync();

    }

    private async Task<bool> WalletExists(PlaywrightTester s, string walletName)
    {
        var walletCard = s.Page.Locator($".wallet-card[data-wallet='{walletName}']");
        try
        {
            await walletCard.WaitForAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task SwitchActiveWallet(PlaywrightTester s, string walletName, string password)
    {
        var walletCard = s.Page.Locator($".wallet-card[data-wallet='{walletName}']");
        await walletCard.Locator(".wallet-card-content").ClickAsync();
        await s.Page.Locator("input#ActiveWalletPassword").WaitForAsync();
        await s.Page.FillAsync("input#ActiveWalletPassword", password);
        await s.Page.ClickAsync("button[name='command'][value='set-active-wallet']");
        await s.Page.Locator($".wallet-card[data-wallet='{walletName}'].wallet-card-active").WaitForAsync();
    }

    private async Task DeleteWallet(PlaywrightTester s, string walletName)
    {
        var walletCard = s.Page.Locator($".wallet-card[data-wallet='{walletName}']");
        await walletCard.Locator(".wallet-actions .dropdown-toggle").ClickAsync();
        await walletCard.Locator(".wallet-action-delete").WaitForAsync();
        await walletCard.Locator(".wallet-action-delete").ClickAsync();
        await s.Page.Locator("#deleteWalletModal").WaitForAsync();
        await s.Page.ClickAsync("#deleteWalletModal button[name='command'][value='delete-wallets']");
        await walletCard.WaitForAsync(new() { State = Microsoft.Playwright.WaitForSelectorState.Detached });
    }

    private async Task<bool> WalletIsActive(PlaywrightTester s, string walletName)
    {
        var walletCard = s.Page.Locator($".wallet-card[data-wallet='{walletName}']");
        var hasActiveClass = await walletCard.GetAttributeAsync("class");
        return hasActiveClass?.Contains("wallet-card-active") ?? false;
    }

    #endregion

    [Fact]
    public async Task CompleteMigratePasswordFromFile()
    {
        const string walletName = "legacy-wallet";
        const string walletPassword = "legacy-password-123";
        string passwordFile;

        {
            await using var tempTester = CreatePlaywrightTester();
            await tempTester.StartAsync();
            var rpcProvider = tempTester.Server.PayTester.GetService<MoneroRPCProvider>();
            var xmrWallet = await rpcProvider.CreateWalletFromKeys(
                "XMR",
                walletName,
                "43Pnj6ZKGFTJhaLhiecSFfLfr64KPJZw7MyGH73T6PTDekBBvsTAaWEUSM4bmJqDuYLizhA13jQkMRPpz9VXBCBqQQb6y5L",
                "1bfa03b0c78aa6bc8292cf160ec9875657d61e889c41d0ebe5c54fd3a2c4b40e",
                walletPassword,
                0);

            Assert.True(xmrWallet.Success);
            await rpcProvider.CloseWallet("XMR");
            string walletDir = rpcProvider.GetWalletDirectory("XMR");
            Assert.NotNull(walletDir);
            passwordFile = Path.Combine(walletDir, "password");
            await File.WriteAllTextAsync(passwordFile, walletPassword);

            var settingsRepository = tempTester.Server.PayTester.GetService<ISettingsRepository>();
            await settingsRepository.UpdateSetting(new { Complete = false }, "MoneroWalletStateMigration");

        }

        await using var s = CreatePlaywrightTester();
        await s.StartAsync();
        var walletService = s.Server.PayTester.GetService<MoneroWalletService>();
        MoneroWalletState walletState = walletService.GetWalletState();
        Assert.Equal(walletPassword, walletState.ActiveWalletPassword);
        Assert.Equal(walletName, walletState.ActiveWalletName);
        Assert.Equal("migration", walletState.LastActivatedByStoreId);
        Assert.True(walletState.IsConnected);
        await s.RegisterNewUser(true);
        await s.CreateNewStore();
        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();

        var walletCard = s.Page.Locator($".wallet-card[data-wallet='{walletName}']");
        await walletCard.WaitForAsync();
        Assert.True(await WalletExists(s, walletName));
        Assert.True(await WalletIsActive(s, walletName));

        await CleanUp(s);
        if (File.Exists(passwordFile))
        {
            File.Delete(passwordFile);
        }
    }

    [Fact]
    public async Task EnableMoneroPluginSuccessfully()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();

        if (s.Server.PayTester.MockRates)
        {
            var rateProviderFactory = s.Server.PayTester.GetService<RateProviderFactory>();
            rateProviderFactory.Providers.Clear();

            var coinAverageMock = new MockRateProvider();
            coinAverageMock.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("BTC_USD"), new BidAsk(5000m)));
            coinAverageMock.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("BTC_EUR"), new BidAsk(4000m)));
            coinAverageMock.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("XMR_BTC"), new BidAsk(4500m)));
            rateProviderFactory.Providers.Add("coingecko", coinAverageMock);

            var kraken = new MockRateProvider();
            kraken.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("BTC_USD"), new BidAsk(0.1m)));
            kraken.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("XMR_USD"), new BidAsk(0.1m)));
            kraken.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("XMR_BTC"), new BidAsk(0.1m)));
            rateProviderFactory.Providers.Add("kraken", kraken);
        }

        await s.RegisterNewUser(true);
        await s.CreateNewStore(preferredExchange: "Kraken");
        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
        await s.Page.Locator("text=No Wallet Configured").WaitForAsync();
        await s.Page.ClickAsync("a[href*='/connect/XMR']");
        await s.Page.Locator("input#WalletName").WaitForAsync();
        await CreateWalletViaForm(
            s,
            walletName: "primary-wallet",
            address: "43Pnj6ZKGFTJhaLhiecSFfLfr64KPJZw7MyGH73T6PTDekBBvsTAaWEUSM4bmJqDuYLizhA13jQkMRPpz9VXBCBqQQb6y5L",
            viewKey: "1bfa03b0c78aa6bc8292cf160ec9875657d61e889c41d0ebe5c54fd3a2c4b40e",
            password: "pass123",
            restoreHeight: "0"
        );
        Assert.True(await WalletExists(s, "primary-wallet"));
        Assert.True(await WalletIsActive(s, "primary-wallet"));

        await s.Page.CheckAsync("#Enabled");
        await s.Page.SelectOptionAsync("#SettlementConfirmationThresholdChoice", "2");
        await s.Page.ClickAsync("#SaveButton");
        await s.Page.Locator("svg.icon-checkmark.text-success").WaitForAsync();

        var invoiceId = await s.CreateInvoice(s.StoreId, 10, "USD");
        await s.GoToInvoiceCheckout(invoiceId);

        var copyButton = s.Page.Locator("button[data-clipboard].clipboard-button");
        var moneroAddress = await copyButton.GetAttributeAsync("data-clipboard");
        Assert.NotNull(moneroAddress);
        Assert.True(moneroAddress.StartsWith('8'), $"Expected Monero address to start with 8, but got: {moneroAddress}");

        await s.GoToStore(s.StoreId);
        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
        Assert.True(await WalletIsActive(s, "primary-wallet"));

        await s.Page.CheckAsync("#Enabled");
        await s.Page.SelectOptionAsync("#SettlementConfirmationThresholdChoice", "3");
        await s.Page.ClickAsync("#SaveButton");

        await s.GoToStore(s.StoreId);
        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
        await s.Page.Locator("#walletManagementMenu").WaitForAsync();

        Assert.True(await WalletExists(s, "primary-wallet"));
        Assert.True(await WalletIsActive(s, "primary-wallet"));
        await s.Page.ClickAsync("#walletManagementMenu");
        await s.Page.ClickAsync("button[data-bs-target='#createWalletModal']");
        await s.Page.Locator("#createWalletModal input#WalletName").WaitForAsync();

        await CreateWalletViaModal(
            s,
            walletName: "secondary-wallet",
            address: "41u3R13W1UDhvHGNS1cqtoh4CKFkuZ5aWbkmkEskhm3SNsuaLQAeFtrXyd6Q2XAgwzf41CMC65u8fVWjB38RLAUb8AKJMw9",
            viewKey: "134af10334bd65ce91e015db3ea9f0b1abd1a9ed2fa378bc537498f4f52f6f0f",
            password: "pass456",
            restoreHeight: "100"
        );
        Assert.True(await WalletExists(s, "primary-wallet"));
        Assert.True(await WalletExists(s, "secondary-wallet"));
        Assert.True(await WalletIsActive(s, "secondary-wallet"));

        await SwitchActiveWallet(s, "primary-wallet", "pass123");
        Assert.True(await WalletIsActive(s, "primary-wallet"));
        Assert.False(await WalletIsActive(s, "secondary-wallet"));

        await DeleteWallet(s, "secondary-wallet");
        Assert.True(await WalletExists(s, "primary-wallet"));
        Assert.False(await WalletExists(s, "secondary-wallet"));
        Assert.True(await WalletIsActive(s, "primary-wallet"));
        await CleanUp(s);
    }


    [Fact]
    public async Task ShouldFailWhenWrongPrimaryAddress()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();

        await s.RegisterNewUser(true);
        await s.CreateNewStore();
        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
        await s.Page.Locator("text=No Wallet Configured").WaitForAsync();
        await s.Page.ClickAsync("a[href*='/connect/XMR']");
        await s.Page.Locator("input#WalletName").WaitForAsync();
        await s.Page.FillAsync("input#WalletName", "wrong-wallet");
        await s.Page.FillAsync("input#PrimaryAddress", "wrongprimaryaddressfSF6ZKGFT7MyGH73T6PTDekBBvsTAaWEUSM4bmJqDuYLizhA13jQkMRPpz9VXBCBqQQb6y5L");
        await s.Page.FillAsync("input#PrivateViewKey", "1bfa03b0c78aa6bc8292cf160ec9875657d61e889c41d0ebe5c54fd3a2c4b40e");
        await s.Page.FillAsync("input#WalletPassword", "pass123");
        await s.Page.FillAsync("input#RestoreHeight", "0");
        await s.Page.ClickAsync("button[name='command'][value='connect-wallet']");
        var errorLocator = s.Page.Locator("div.validation-summary-errors li");
        await errorLocator.WaitForAsync();
        var errorText = await errorLocator.InnerTextAsync();

        Assert.Equal("Could not create wallet: Failed to parse public address", errorText);

        await CleanUp(s);
    }

    private static async Task CleanUp(PlaywrightTester playwrightTester)
    {
        MoneroRPCProvider moneroRpcProvider = playwrightTester.Server.PayTester.GetService<MoneroRPCProvider>();
        MoneroWalletService walletService = playwrightTester.Server.PayTester.GetService<MoneroWalletService>();

        if (moneroRpcProvider.IsAvailable("XMR"))
        {
            await moneroRpcProvider.CloseWallet("XMR");
            await moneroRpcProvider.UpdateSummary("XMR");
        }

        await walletService.ClearWalletState();

        if (playwrightTester.Server.PayTester.InContainer)
        {
            moneroRpcProvider.DeleteAllWallets();
        }
        else
        {
            await RemoveWalletFromLocalDocker();
        }
    }

    static async Task RemoveWalletFromLocalDocker()
    {
        try
        {
            var removeWalletFromDocker = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "exec xmr_wallet sh -c \"rm -rf /wallet/*\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(removeWalletFromDocker);
            if (process is null)
            {
                return;
            }

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                Console.WriteLine(stdout);
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Console.WriteLine(stderr);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cleanup failed: {ex}");
        }
    }
}