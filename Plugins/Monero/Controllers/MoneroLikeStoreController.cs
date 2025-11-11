using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Monero.Configuration;
using BTCPayServer.Plugins.Monero.Payments;
using BTCPayServer.Plugins.Monero.RPC.Models;
using BTCPayServer.Plugins.Monero.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.Monero.Controllers
{
    [Route("stores/{storeId}/monerolike")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class UIMoneroLikeStoreController : Controller
    {
        private readonly MoneroLikeConfiguration _MoneroLikeConfiguration;
        private readonly StoreRepository _StoreRepository;
        private readonly MoneroRPCProvider _MoneroRpcProvider;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly MoneroWalletService _walletService;
        private readonly Logs _logs;
        private IStringLocalizer StringLocalizer { get; }

        public UIMoneroLikeStoreController(MoneroLikeConfiguration moneroLikeConfiguration,
            StoreRepository storeRepository, MoneroRPCProvider moneroRpcProvider,
            PaymentMethodHandlerDictionary handlers,
            MoneroWalletService walletService,
            IStringLocalizer stringLocalizer,
            Logs logs)
        {
            _MoneroLikeConfiguration = moneroLikeConfiguration;
            _StoreRepository = storeRepository;
            _MoneroRpcProvider = moneroRpcProvider;
            _handlers = handlers;
            _walletService = walletService;
            StringLocalizer = stringLocalizer;
            _logs = logs;
        }

        public StoreData StoreData => HttpContext.GetStoreData();

        [HttpGet()]
        public async Task<IActionResult> GetStoreMoneroLikePaymentMethods()
        {
            return View("/Views/Monero/GetStoreMoneroLikePaymentMethods.cshtml", await GetVM(StoreData));
        }
        [NonAction]
        public async Task<MoneroLikePaymentMethodListViewModel> GetVM(StoreData storeData)
        {
            var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();

            var accountsList = _MoneroLikeConfiguration.MoneroLikeConfigurationItems.ToDictionary(pair => pair.Key,
                pair => _MoneroRpcProvider.GetAccounts(pair.Key));

            await Task.WhenAll(accountsList.Values);
            return new MoneroLikePaymentMethodListViewModel()
            {
                Items = _MoneroLikeConfiguration.MoneroLikeConfigurationItems.Select(pair =>
                    GetMoneroLikePaymentMethodViewModel(storeData, pair.Key, excludeFilters,
                        accountsList[pair.Key].Result))
            };
        }

        private MoneroLikePaymentMethodViewModel GetMoneroLikePaymentMethodViewModel(
            StoreData storeData, string cryptoCode,
            IPaymentFilter excludeFilters, GetAccountsResponse accountsResponse)
        {
            var monero = storeData.GetPaymentMethodConfigs(_handlers)
                .Where(s => s.Value is MoneroPaymentPromptDetails)
                .Select(s => (PaymentMethodId: s.Key, Details: (MoneroPaymentPromptDetails)s.Value));
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
            var settings = monero.Where(method => method.PaymentMethodId == pmi).Select(m => m.Details).SingleOrDefault();
            _MoneroRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary);
            var accounts = accountsResponse?.SubaddressAccounts?.Select(account =>
                new SelectListItem(
                    $"{account.AccountIndex} - {(string.IsNullOrEmpty(account.Label) ? "No label" : account.Label)}",
                    account.AccountIndex.ToString(CultureInfo.InvariantCulture)));

            var settlementThresholdChoice = MoneroLikeSettlementThresholdChoice.StoreSpeedPolicy;
            if (settings != null && settings.InvoiceSettledConfirmationThreshold is { } confirmations)
            {
                settlementThresholdChoice = confirmations switch
                {
                    0 => MoneroLikeSettlementThresholdChoice.ZeroConfirmation,
                    1 => MoneroLikeSettlementThresholdChoice.AtLeastOne,
                    10 => MoneroLikeSettlementThresholdChoice.AtLeastTen,
                    _ => MoneroLikeSettlementThresholdChoice.Custom
                };
            }

            MoneroWalletState walletState = _walletService.GetWalletState();
            string currentWallet = walletState?.ActiveWalletName;
            long accountIndex = 0;

            if (settings != null && !string.IsNullOrEmpty(currentWallet))
            {
                accountIndex = settings.GetAccountIndexForWallet(currentWallet);

                if (accountIndex == 0 && settings.AccountIndex != 0 && settings.WalletAccountIndexes != null && !settings.WalletAccountIndexes.ContainsKey(currentWallet))
                {
                    accountIndex = settings.AccountIndex;
                }
            }
            else if (settings != null && settings.AccountIndex != 0)
            {
                accountIndex = settings.AccountIndex;
            }
            else
            {
                accountIndex = accountsResponse?.SubaddressAccounts?.FirstOrDefault()?.AccountIndex ?? 0;
            }

            return new MoneroLikePaymentMethodViewModel()
            {
                Enabled =
                    settings != null &&
                    !excludeFilters.Match(PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode)),
                Summary = summary,
                CryptoCode = cryptoCode,
                AccountIndex = accountIndex,
                Accounts = accounts == null ? null : new SelectList(accounts, nameof(SelectListItem.Value),
                    nameof(SelectListItem.Text)),
                SettlementConfirmationThresholdChoice = settlementThresholdChoice,
                CustomSettlementConfirmationThreshold =
                    settings != null &&
                    settlementThresholdChoice is MoneroLikeSettlementThresholdChoice.Custom
                        ? settings.InvoiceSettledConfirmationThreshold
                        : null,

                WalletState = walletState,
                CurrentActiveWallet = currentWallet ?? "None",
                LastWalletOpenedAt = walletState?.LastActivatedAt
            };
        }

        private async Task<(bool success, string errorMessage, MoneroLikePaymentMethodViewModel viewModel)> ValidateAndCreateWallet(
            MoneroLikePaymentMethodViewModel viewModel)
        {
            if (string.IsNullOrWhiteSpace(viewModel.WalletName))
            {
                ModelState.AddModelError(nameof(viewModel.WalletName), StringLocalizer["A wallet name is required to create a new wallet."]);
                return (false, null, viewModel);
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(viewModel.WalletName, "^[a-zA-Z0-9_-]+$") ||
                viewModel.WalletName.Length > 64)
            {
                ModelState.AddModelError(nameof(viewModel.WalletName),
                    StringLocalizer["Wallet name must contain only letters, numbers, dashes, and underscores (max 64 characters)."]);
                return (false, null, viewModel);
            }

            // TODO: Validate shape of primary address and private view key
            if (string.IsNullOrEmpty(viewModel.PrimaryAddress))
            {
                ModelState.AddModelError(nameof(viewModel.PrimaryAddress), StringLocalizer["The primary address is required to create a new wallet."]);
                return (false, null, viewModel);
            }

            if (string.IsNullOrEmpty(viewModel.PrivateViewKey))
            {
                ModelState.AddModelError(nameof(viewModel.PrivateViewKey), StringLocalizer["The private view key is required to create a new wallet."]);
                return (false, null, viewModel);
            }

            if (string.IsNullOrEmpty(viewModel.WalletPassword))
            {
                ModelState.AddModelError(nameof(viewModel.WalletPassword), StringLocalizer["A password is required for the wallet."]);
                return (false, null, viewModel);
            }

            if (!ModelState.IsValid)
            {
                return (false, null, viewModel);
            }

            try
            {
                var (success, errorMessage) = await _walletService.CreateAndActivateWallet(
                    viewModel.WalletName,
                    viewModel.PrimaryAddress,
                    viewModel.PrivateViewKey,
                    viewModel.WalletPassword,
                    viewModel.RestoreHeight,
                    StoreData.Id);

                return (success, errorMessage, viewModel);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, viewModel);
            }
        }

        [HttpGet("setup/{cryptoCode}")]
        public async Task<IActionResult> SetupMoneroWallet(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_MoneroLikeConfiguration.MoneroLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            IPaymentFilter excludedPaymentMethods = StoreData.GetStoreBlob().GetExcludedPaymentMethods();
            GetAccountsResponse accounts = await _MoneroRpcProvider.GetAccounts(cryptoCode);
            var vm = GetMoneroLikePaymentMethodViewModel(StoreData, cryptoCode, excludedPaymentMethods, accounts);

            return View("/Views/Monero/SetupMoneroWallet.cshtml", vm);
        }

        [HttpGet("connect/{cryptoCode}")]
        public async Task<IActionResult> ConnectNewWallet(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_MoneroLikeConfiguration.MoneroLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            IPaymentFilter excludedPaymentMethods = StoreData.GetStoreBlob().GetExcludedPaymentMethods();
            GetAccountsResponse accounts = await _MoneroRpcProvider.GetAccounts(cryptoCode);
            var vm = GetMoneroLikePaymentMethodViewModel(StoreData, cryptoCode, excludedPaymentMethods, accounts);

            return View("/Views/Monero/ConnectNewWallet.cshtml", vm);
        }

        [HttpPost("connect/{cryptoCode}")]
        public async Task<IActionResult> ConnectNewWallet(MoneroLikePaymentMethodViewModel viewModel, string command, string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_MoneroLikeConfiguration.MoneroLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            if (command == "connect-wallet")
            {
                var (success, errorMessage, _) = await ValidateAndCreateWallet(viewModel);

                if (success)
                {
                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Severity = StatusMessageModel.StatusSeverity.Success,
                        Message = StringLocalizer["Wallet '{0}' created successfully and now active", viewModel.WalletName].Value
                    });

                    return RedirectToAction(nameof(GetStoreMoneroLikePaymentMethod), new { storeId = StoreData.Id, cryptoCode });
                }

                if (!string.IsNullOrEmpty(errorMessage))
                {
                    ModelState.AddModelError(string.Empty, StringLocalizer["Could not create wallet: {0}", errorMessage]);
                }
            }

            if (!ModelState.IsValid)
            {
                IPaymentFilter excludedPaymentMethods = StoreData.GetStoreBlob().GetExcludedPaymentMethods();
                GetAccountsResponse accounts = await _MoneroRpcProvider.GetAccounts(cryptoCode);
                var vm = GetMoneroLikePaymentMethodViewModel(StoreData, cryptoCode, excludedPaymentMethods, accounts);
                vm.WalletName = viewModel.WalletName;
                vm.PrimaryAddress = viewModel.PrimaryAddress;
                vm.PrivateViewKey = viewModel.PrivateViewKey;
                vm.RestoreHeight = viewModel.RestoreHeight;
                return View("/Views/Monero/ConnectNewWallet.cshtml", vm);
            }

            return RedirectToAction(nameof(GetStoreMoneroLikePaymentMethod), new { storeId = StoreData.Id, cryptoCode });
        }

        [HttpGet("{cryptoCode}")]
        public async Task<IActionResult> GetStoreMoneroLikePaymentMethod(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_MoneroLikeConfiguration.MoneroLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            IPaymentFilter excludedPaymentMethods = StoreData.GetStoreBlob().GetExcludedPaymentMethods();
            GetAccountsResponse accounts = await _MoneroRpcProvider.GetAccounts(cryptoCode);
            var vm = GetMoneroLikePaymentMethodViewModel(StoreData, cryptoCode, excludedPaymentMethods, accounts);
            vm.AvailableWallets = _MoneroRpcProvider.GetWalletList(cryptoCode) ?? Array.Empty<string>();

            return View("/Views/Monero/GetStoreMoneroLikePaymentMethod.cshtml", vm);
        }

        [HttpPost("{cryptoCode}")]
        public async Task<IActionResult> GetStoreMoneroLikePaymentMethod(MoneroLikePaymentMethodViewModel viewModel, string command, string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_MoneroLikeConfiguration.MoneroLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            if (command == "add-account")
            {
                try
                {
                    CreateAccountResponse newAccount = await _MoneroRpcProvider.CreateAccount(cryptoCode, viewModel.NewAccountLabel);
                    if (newAccount != null)
                    {
                        viewModel.AccountIndex = newAccount.AccountIndex;
                    }
                }
                catch (Exception)
                {
                    ModelState.AddModelError(nameof(viewModel.AccountIndex), StringLocalizer["Could not create a new account."]);
                }

            }
            else if (command == "set-active-wallet")
            {
                var valid = true;
                if (string.IsNullOrWhiteSpace(viewModel.NewActiveWallet))
                {
                    ModelState.AddModelError(nameof(viewModel.NewActiveWallet), StringLocalizer["Please select a wallet"]);
                    valid = false;
                }
                if (string.IsNullOrWhiteSpace(viewModel.ActiveWalletPassword))
                {
                    ModelState.AddModelError(nameof(viewModel.ActiveWalletPassword), StringLocalizer["Please provide the wallet password"]);
                    valid = false;
                }

                if (valid)
                {
                    try
                    {
                        bool success = await _walletService.SetActiveWallet(
                            viewModel.NewActiveWallet,
                            viewModel.ActiveWalletPassword,
                            StoreData.Id);

                        if (!success)
                        {
                            throw new Exception("Failed to set the active wallet");
                        }

                        TempData.SetStatusMessageModel(new StatusMessageModel
                        {
                            Severity = StatusMessageModel.StatusSeverity.Success,
                            Message = StringLocalizer["Wallet changed to '{0}'", viewModel.NewActiveWallet].Value
                        });
                        return RedirectToAction(nameof(GetStoreMoneroLikePaymentMethod), new { storeId = StoreData.Id, cryptoCode });
                    }
                    catch (Exception ex)
                    {
                        ModelState.AddModelError(string.Empty, StringLocalizer["Failed to set active wallet: {0}", ex.Message]);
                    }
                }
            }
            else if (command == "replace-wallet")
            {
                var (success, errorMessage, _) = await ValidateAndCreateWallet(viewModel);

                if (success)
                {
                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Severity = StatusMessageModel.StatusSeverity.Success,
                        Message = StringLocalizer["New wallet '{0}' created and activated", viewModel.WalletName].Value
                    });

                    return RedirectToAction(nameof(GetStoreMoneroLikePaymentMethod), new { storeId = StoreData.Id, cryptoCode });
                }

                if (!string.IsNullOrEmpty(errorMessage))
                {
                    ModelState.AddModelError(string.Empty, StringLocalizer["Failed to create wallet: {0}", errorMessage]);
                }
            }
            else if (command == "delete-wallets")
            {
                var valid = true;

                if (viewModel.WalletsToDelete?.Any() != true)
                {
                    ModelState.AddModelError(nameof(viewModel.WalletsToDelete), StringLocalizer["You must select at least one wallet to delete."]);
                    valid = false;
                }

                if (valid)
                {
                    try
                    {
                        MoneroWalletState walletState = _walletService.GetWalletState();
                        bool deletingActiveWallet = viewModel.WalletsToDelete.Contains(walletState.ActiveWalletName);
                        int successCount = 0;
                        List<string> failedWallets = [];
                        string message;

                        if (deletingActiveWallet)
                        {
                            await _walletService.CloseActiveWallet();
                            await _walletService.ClearWalletState();
                        }

                        foreach (var walletName in viewModel.WalletsToDelete)
                        {
                            bool success = _MoneroRpcProvider.DeleteWallet(cryptoCode, walletName);
                            if (success)
                            {
                                successCount++;
                                await RemoveWalletFromStoreConfigs(walletName, cryptoCode);
                            }
                            else
                            {
                                failedWallets.Add(walletName);
                            }
                        }

                        if (successCount == 1)
                        {
                            message = StringLocalizer["One wallet has been deleted."].Value;
                        }
                        else
                        {
                            message = StringLocalizer["{0} wallets have been deleted.", successCount].Value;
                        }

                        if (failedWallets.Any())
                        {
                            message += " " + StringLocalizer["Failed to delete: {0}", string.Join(", ", failedWallets)].Value;
                        }

                        TempData.SetStatusMessageModel(new StatusMessageModel
                        {
                            Severity = failedWallets.Any() ? StatusMessageModel.StatusSeverity.Warning : StatusMessageModel.StatusSeverity.Success,
                            Message = message
                        });

                        string[] remainingWallets = _MoneroRpcProvider.GetWalletList(cryptoCode) ?? Array.Empty<string>();

                        if (!remainingWallets.Any())
                        {
                            return RedirectToAction(nameof(SetupMoneroWallet), new { storeId = StoreData.Id, cryptoCode });
                        }

                        return RedirectToAction(nameof(GetStoreMoneroLikePaymentMethod), new { storeId = StoreData.Id, cryptoCode });
                    }
                    catch (Exception ex)
                    {
                        ModelState.AddModelError(string.Empty, StringLocalizer["Failed to delete wallets: {0}", ex.Message]);
                    }
                }
            }

            if (!ModelState.IsValid)
            {
                IPaymentFilter excludedPaymentMethods = StoreData.GetStoreBlob().GetExcludedPaymentMethods();
                GetAccountsResponse accounts = await _MoneroRpcProvider.GetAccounts(cryptoCode);
                var vm = GetMoneroLikePaymentMethodViewModel(StoreData, cryptoCode, excludedPaymentMethods, accounts);

                vm.Enabled = viewModel.Enabled;
                vm.NewAccountLabel = viewModel.NewAccountLabel;
                vm.AccountIndex = viewModel.AccountIndex;
                vm.SettlementConfirmationThresholdChoice = viewModel.SettlementConfirmationThresholdChoice;
                vm.CustomSettlementConfirmationThreshold = viewModel.CustomSettlementConfirmationThreshold;
                return View("/Views/Monero/GetStoreMoneroLikePaymentMethod.cshtml", vm);
            }

            StoreData storeData = StoreData;
            StoreBlob blob = storeData.GetStoreBlob();
            PaymentMethodId pmi = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);

            string currentWallet = _walletService.GetWalletState()?.ActiveWalletName;

            if (string.IsNullOrEmpty(currentWallet))
            {
                IPaymentFilter excludedPaymentMethods = StoreData.GetStoreBlob().GetExcludedPaymentMethods();
                GetAccountsResponse accounts = await _MoneroRpcProvider.GetAccounts(cryptoCode);
                var vm = GetMoneroLikePaymentMethodViewModel(StoreData, cryptoCode, excludedPaymentMethods, accounts);
                vm.Enabled = viewModel.Enabled;
                vm.NewAccountLabel = viewModel.NewAccountLabel;
                vm.AccountIndex = viewModel.AccountIndex;
                vm.SettlementConfirmationThresholdChoice = viewModel.SettlementConfirmationThresholdChoice;
                vm.CustomSettlementConfirmationThreshold = viewModel.CustomSettlementConfirmationThreshold;
                return View("/Views/Monero/GetStoreMoneroLikePaymentMethod.cshtml", vm);
            }

            var existingSettings = storeData.GetPaymentMethodConfig<MoneroPaymentPromptDetails>(pmi, _handlers);
            var settings = existingSettings ?? new MoneroPaymentPromptDetails();

            settings.SetAccountIndexForWallet(currentWallet, viewModel.AccountIndex);

            settings.InvoiceSettledConfirmationThreshold = viewModel.SettlementConfirmationThresholdChoice switch
            {
                MoneroLikeSettlementThresholdChoice.ZeroConfirmation => 0,
                MoneroLikeSettlementThresholdChoice.AtLeastOne => 1,
                MoneroLikeSettlementThresholdChoice.AtLeastTen => 10,
                MoneroLikeSettlementThresholdChoice.Custom when viewModel.CustomSettlementConfirmationThreshold is { } custom => custom,
                _ => null
            };

            storeData.SetPaymentMethodConfig(_handlers[pmi], settings);

            blob.SetExcluded(PaymentTypes.CHAIN.GetPaymentMethodId(viewModel.CryptoCode), !viewModel.Enabled);
            storeData.SetStoreBlob(blob);
            await _StoreRepository.UpdateStore(storeData);
            return RedirectToAction("GetStoreMoneroLikePaymentMethods",
                new { StatusMessage = $"{cryptoCode} settings updated successfully", storeId = StoreData.Id });
        }

        private async Task RemoveWalletFromStoreConfigs(string walletName, string cryptoCode)
        {
            PaymentMethodId pmi = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
            MoneroWalletState walletState = _walletService.GetWalletState();
            bool isActiveWallet = walletName == walletState.ActiveWalletName;
            IEnumerable<StoreData> allStores = await _StoreRepository.GetStores();

            foreach (StoreData store in allStores)
            {
                MoneroPaymentPromptDetails paymentDetails = store.GetPaymentMethodConfig<MoneroPaymentPromptDetails>(pmi, _handlers);
                bool storeUpdated = false;

                if (paymentDetails?.RemoveWallet(walletName) == true)
                {
                    store.SetPaymentMethodConfig(_handlers[pmi], paymentDetails);
                    storeUpdated = true;
                }

                if (isActiveWallet && paymentDetails != null)
                {
                    StoreBlob blob = store.GetStoreBlob();
                    bool wasEnabled = !blob.IsExcluded(pmi);

                    if (wasEnabled)
                    {
                        blob.SetExcluded(pmi, true);
                        store.SetStoreBlob(blob);
                        storeUpdated = true;
                    }
                }
                if (storeUpdated)
                {
                    await _StoreRepository.UpdateStore(store);
                }
            }
        }

        public class MoneroLikePaymentMethodListViewModel
        {
            public IEnumerable<MoneroLikePaymentMethodViewModel> Items { get; set; }
        }

        public class MoneroLikePaymentMethodViewModel : IValidatableObject
        {
            public MoneroRPCProvider.MoneroLikeSummary Summary { get; set; }
            public string CryptoCode { get; set; }
            public string NewAccountLabel { get; set; }
            public long AccountIndex { get; set; }
            public bool Enabled { get; set; }
            public IEnumerable<SelectListItem> Accounts { get; set; }
            public bool WalletFileFound { get; set; }
            [Display(Name = "Primary Public Address")]
            public string PrimaryAddress { get; set; }
            [Display(Name = "Private View Key")]
            public string PrivateViewKey { get; set; }
            [Display(Name = "Restore Height")]
            public int RestoreHeight { get; set; }
            [Display(Name = "Wallet Name")]
            public string WalletName { get; set; }
            [Display(Name = "Wallet Password")]
            public string WalletPassword { get; set; }
            [Display(Name = "Consider the invoice settled when the payment transaction …")]
            public MoneroLikeSettlementThresholdChoice SettlementConfirmationThresholdChoice { get; set; }
            [Display(Name = "Required Confirmations"), Range(0, 100)]
            public long? CustomSettlementConfirmationThreshold { get; set; }
            [Display(Name = "Currently Active Wallet")]
            public string CurrentActiveWallet { get; set; }
            [Display(Name = "Date Last Wallet Opened")]
            public DateTimeOffset? LastWalletOpenedAt { get; set; }
            [Display(Name = "Select Active Wallet")]
            public string NewActiveWallet { get; set; }
            [Display(Name = "Active Wallet Password")]
            public string ActiveWalletPassword { get; set; }
            [Display(Name = "Wallet to Delete")]
            public string WalletToDelete { get; set; }
            [Display(Name = "Wallets to Delete")]
            public string[] WalletsToDelete { get; set; }
            public string[] AvailableWallets { get; set; }
            public MoneroWalletState WalletState { get; set; }

            public bool HasActiveWallet => !string.IsNullOrEmpty(CurrentActiveWallet) && CurrentActiveWallet != "None";

            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {
                if (SettlementConfirmationThresholdChoice is MoneroLikeSettlementThresholdChoice.Custom
                    && CustomSettlementConfirmationThreshold is null)
                {
                    yield return new ValidationResult(
                        "You must specify the number of required confirmations when using a custom threshold.",
                        new[] { nameof(CustomSettlementConfirmationThreshold) });
                }
            }
        }

        public enum MoneroLikeSettlementThresholdChoice
        {
            [Display(Name = "Store Speed Policy", Description = "Use the store's speed policy")]
            StoreSpeedPolicy,
            [Display(Name = "Zero Confirmation", Description = "Is unconfirmed")]
            ZeroConfirmation,
            [Display(Name = "At Least One", Description = "Has at least 1 confirmation")]
            AtLeastOne,
            [Display(Name = "At Least Ten", Description = "Has at least 10 confirmations")]
            AtLeastTen,
            [Display(Name = "Custom", Description = "Custom")]
            Custom
        }
    }
}