# Payment Flow Documentation

This document describes how Monero payments are processed in the BTCPay Server Monero plugin.

## Table of Contents

1. [Overview](#overview)
2. [Invoice Creation](#invoice-creation)
3. [Payment Address Generation](#payment-address-generation)
4. [Payment Monitoring](#payment-monitoring)
5. [Payment Detection](#payment-detection)
6. [Confirmation Tracking](#confirmation-tracking)
7. [Payment Completion](#payment-completion)
8. [Edge Cases](#edge-cases)
9. [Sequence Diagrams](#sequence-diagrams)

## Overview

The payment flow involves multiple components working together to accept Monero payments:

1. **Invoice Creation**: User creates invoice with Monero payment method
2. **Address Generation**: Unique Monero subaddress generated for payment
3. **Payment Monitoring**: Background service polls for incoming transactions
4. **Payment Detection**: Transaction matched to invoice
5. **Confirmation Tracking**: Monitor blockchain confirmations
6. **Payment Completion**: Invoice marked as paid, webhooks triggered

### Key Concepts

- **Subaddresses**: Each invoice gets a unique subaddress for privacy and tracking
- **Atomic Units**: Monero amounts are represented in piconero (1 XMR = 10^12 piconero)
- **Confirmations**: Number of blocks after transaction inclusion
- **Payment Window**: Time limit for invoice payment (typically 15-60 minutes)

## Invoice Creation

### User Creates Invoice

When a user creates an invoice in BTCPay Server:

```
User → BTCPay Server → Invoice Controller
                            ↓
                     Create Invoice Entity
                            ↓
                     Add Payment Methods
                            ↓
                     MoneroLikePaymentMethodHandler
```

### Payment Method Handler

The `MoneroLikePaymentMethodHandler` is invoked to prepare Monero payment details:

```csharp
public class MoneroLikePaymentMethodHandler : IPaymentMethodHandler
{
    public async Task<IPaymentMethodDetails> CreatePaymentMethodDetails(
        InvoiceEntity invoice,
        PaymentMethod paymentMethod,
        StoreData store)
    {
        // 1. Get or create wallet for store
        var walletInfo = await GetWalletInfo(store);
        
        // 2. Generate new subaddress
        var address = await _rpcProvider.CreateAddress(
            accountIndex: walletInfo.AccountIndex,
            label: $"Invoice {invoice.Id}"
        );
        
        // 3. Calculate payment amount
        var cryptoAmount = paymentMethod.Calculate().Due;
        
        // 4. Create payment details
        return new MoneroLikeOnChainPaymentMethodDetails
        {
            Address = address.Address,
            AccountIndex = walletInfo.AccountIndex,
            AddressIndex = address.AddressIndex,
            NextNetworkFee = GetNetworkFee()
        };
    }
}
```

### Payment Details Saved

Payment details are stored in the database:

```json
{
  "invoiceId": "ABC123",
  "paymentMethodId": "XMR",
  "address": "8AvK5H9s...",
  "accountIndex": 0,
  "addressIndex": 42,
  "amount": "1.234567890123",
  "cryptoCode": "XMR",
  "status": "New"
}
```

## Payment Address Generation

### Subaddress Architecture

Monero uses subaddresses to provide privacy and improve tracking:

```
Master Wallet
    │
    ├─► Account 0 (Store A)
    │   ├─► Subaddress 0 (Primary)
    │   ├─► Subaddress 1 (Invoice 1)
    │   ├─► Subaddress 2 (Invoice 2)
    │   └─► Subaddress N (Invoice N)
    │
    └─► Account 1 (Store B - future)
        └─► Subaddresses...
```

### Address Generation Process

```
1. Check if wallet exists for store
   └─► If not, prompt user to create wallet
       
2. Get next available subaddress index
   └─► Query wallet for existing addresses
   └─► Increment highest index
       
3. Create new subaddress
   └─► RPC: create_address(account_index, label)
   └─► Returns: address + address_index
       
4. Save address mapping
   └─► Invoice ID → Address
   └─► Address → Account/Subaddress Index
       
5. Return payment details
   └─► Include address in invoice
```

### Example Address Generation

```csharp
// MoneroRPCProvider.cs
public async Task<CreateAddressResponse> CreateAddress(
    uint accountIndex, 
    string label = "")
{
    var request = new CreateAddressRequest
    {
        AccountIndex = accountIndex,
        Label = label
    };
    
    var response = await SendRPCRequest<CreateAddressRequest, CreateAddressResponse>(
        "create_address", 
        request, 
        WalletClient
    );
    
    return response;
}
```

## Payment Monitoring

### MoneroListener Background Service

The `MoneroListener` runs continuously to monitor for payments:

```csharp
public class MoneroListener : IHostedService
{
    private Timer _timer;
    private const int PollingIntervalSeconds = 15;
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MoneroListener started");
        
        _timer = new Timer(
            CheckForPayments, 
            null, 
            TimeSpan.Zero, 
            TimeSpan.FromSeconds(PollingIntervalSeconds)
        );
        
        return Task.CompletedTask;
    }
    
    private async void CheckForPayments(object state)
    {
        try
        {
            await PollWalletForTransactions();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for payments");
        }
    }
}
```

### Polling Strategy

```
MoneroListener Timer (every 15 seconds)
    ↓
Query wallet for transfers
    ↓
Filter: in=true, pending=true, pool=true
    ↓
Get all incoming transactions
    ↓
Match transactions to invoices
    ↓
Update payment status
    ↓
Trigger notifications (if status changed)
```

### Transaction Query

```csharp
private async Task PollWalletForTransactions()
{
    // Get all pending invoices with Monero payment method
    var pendingInvoices = await _invoiceRepository.GetInvoices(new InvoiceQuery
    {
        Status = new[] { InvoiceStatus.New, InvoiceStatus.Processing }
    });
    
    // Get wallet transfers
    var transfers = await _rpcProvider.GetTransfers(new GetTransfersRequest
    {
        In = true,
        Pending = true,
        Pool = true,
        AccountIndex = 0
    });
    
    // Match transactions to invoices
    foreach (var invoice in pendingInvoices)
    {
        await MatchAndProcessPayment(invoice, transfers);
    }
}
```

## Payment Detection

### Transaction Matching

Transactions are matched to invoices by:

1. **Address**: Transaction received on invoice address
2. **Amount**: Transaction amount matches or exceeds invoice amount
3. **Status**: Invoice is in payable state

```csharp
private async Task MatchAndProcessPayment(
    InvoiceEntity invoice, 
    GetTransfersResponse transfers)
{
    var paymentMethod = invoice.GetPaymentMethod("XMR");
    var details = paymentMethod.GetPaymentMethodDetails() as MoneroLikeOnChainPaymentMethodDetails;
    
    // Find transaction to this invoice's address
    var matchingTx = transfers.In.FirstOrDefault(tx => 
        tx.Address == details.Address
    );
    
    if (matchingTx != null)
    {
        await ProcessPayment(invoice, matchingTx);
    }
}
```

### Payment Amount Validation

```csharp
private bool IsPaymentAmountValid(long receivedAmount, decimal invoiceAmount)
{
    // Convert invoice amount to atomic units
    long invoiceAmountPiconero = (long)(invoiceAmount * 1_000_000_000_000m);
    
    // Allow small underpayment due to transaction fees
    long minimumAcceptable = (long)(invoiceAmountPiconero * 0.99m); // 99%
    
    return receivedAmount >= minimumAcceptable;
}
```

### Detection Flow

```
New transaction detected
    ↓
Extract: address, amount, txid, confirmations
    ↓
Find invoice with matching address
    ↓
Validate amount
    ├─► Amount too low → Log warning, ignore
    ├─► Amount valid → Process payment
    └─► Amount too high → Process payment, note overpayment
    ↓
Create or update payment entity
    ↓
Update invoice status
```

## Confirmation Tracking

### Confirmation Levels

Different confirmation levels trigger different actions:

| Confirmations | Status | Description |
|--------------|--------|-------------|
| 0 (mempool) | Detected | Payment seen but not confirmed |
| 1-9 | Processing | Payment confirming |
| 10+ | Paid | Payment fully confirmed |

### Confirmation Monitoring

```csharp
private async Task ProcessPayment(InvoiceEntity invoice, Transfer transaction)
{
    var confirmations = transaction.Confirmations;
    var requiredConfirmations = 10; // Configurable per store
    
    if (confirmations == 0)
    {
        // Payment detected in mempool
        await UpdateInvoiceStatus(invoice, InvoiceStatus.Processing);
        _logger.LogInformation(
            "Payment detected for invoice {InvoiceId}, txid: {Txid}",
            invoice.Id, transaction.Txid
        );
    }
    else if (confirmations < requiredConfirmations)
    {
        // Payment confirming
        await UpdatePaymentConfirmations(invoice, confirmations);
        _logger.LogInformation(
            "Payment confirming for invoice {InvoiceId}, confirmations: {Confirmations}/{Required}",
            invoice.Id, confirmations, requiredConfirmations
        );
    }
    else
    {
        // Payment fully confirmed
        await MarkInvoiceAsPaid(invoice, transaction);
        _logger.LogInformation(
            "Payment confirmed for invoice {InvoiceId}, txid: {Txid}",
            invoice.Id, transaction.Txid
        );
    }
}
```

### Confirmation Progress

The UI displays confirmation progress:

```
0 confirmations:  [░░░░░░░░░░] 0/10
3 confirmations:  [███░░░░░░░] 3/10
10 confirmations: [██████████] 10/10 ✓
```

## Payment Completion

### Marking Invoice as Paid

When sufficient confirmations are reached:

```csharp
private async Task MarkInvoiceAsPaid(InvoiceEntity invoice, Transfer transaction)
{
    // Create payment data
    var payment = new PaymentEntity
    {
        Id = transaction.Txid,
        Type = PaymentTypes.CHAIN.GetPaymentMethodId("XMR"),
        CryptoCode = "XMR",
        Value = MoneroMoney.FromPiconero(transaction.Amount),
        ReceivedTime = DateTimeOffset.FromUnixTimeSeconds(transaction.Timestamp),
        Confirmations = transaction.Confirmations,
        Status = PaymentStatus.Confirmed
    };
    
    // Add payment to invoice
    await _invoiceRepository.AddPayment(invoice.Id, payment);
    
    // Update invoice status
    invoice.Status = InvoiceStatus.Paid;
    await _invoiceRepository.UpdateInvoice(invoice.Id, invoice);
    
    // Trigger webhooks
    await _eventAggregator.Publish(new InvoiceEvent(invoice, InvoiceEvent.PaidAfterExpiration));
}
```

### Webhook Notifications

BTCPay Server triggers webhooks when payment status changes:

```json
{
  "deliveryId": "abc123",
  "webhookId": "webhook_123",
  "originalDeliveryId": null,
  "isRedelivery": false,
  "type": "InvoicePaymentSettled",
  "timestamp": 1234567890,
  "storeId": "store123",
  "invoiceId": "invoice123",
  "metadata": {
    "cryptoCode": "XMR",
    "txid": "abc123...",
    "confirmations": 10,
    "amount": "1.234567890123"
  }
}
```

### Payment Complete Flow

```
Sufficient confirmations reached
    ↓
Create PaymentEntity
    ↓
Add to Invoice
    ↓
Update Invoice status → Paid
    ↓
Trigger InvoiceEvent
    ↓
Send Webhooks
    ↓
Update UI (if customer still viewing)
    ↓
Log completion
```

## Edge Cases

### Underpayment

If payment amount is less than required:

```csharp
if (receivedAmount < invoiceAmount * 0.99m)
{
    // Mark as invalid payment
    payment.Status = PaymentStatus.Invalid;
    
    _logger.LogWarning(
        "Underpayment for invoice {InvoiceId}. Expected: {Expected}, Received: {Received}",
        invoice.Id, invoiceAmount, receivedAmount
    );
    
    // Invoice remains unpaid
    return;
}
```

### Overpayment

If payment amount exceeds invoice amount:

```csharp
if (receivedAmount > invoiceAmount * 1.01m)
{
    // Accept payment but log overpayment
    _logger.LogInformation(
        "Overpayment for invoice {InvoiceId}. Expected: {Expected}, Received: {Received}",
        invoice.Id, invoiceAmount, receivedAmount
    );
}

// Process normally
await MarkInvoiceAsPaid(invoice, transaction);
```

### Multiple Payments

If multiple payments received for same invoice:

```csharp
private async Task HandleMultiplePayments(InvoiceEntity invoice, List<Transfer> transactions)
{
    // Sum all transactions
    var totalReceived = transactions.Sum(tx => tx.Amount);
    
    if (totalReceived >= invoice.AmountDue)
    {
        // Combine all transactions into one payment
        var payment = new PaymentEntity
        {
            Id = string.Join(",", transactions.Select(tx => tx.Txid)),
            Value = MoneroMoney.FromPiconero(totalReceived),
            Confirmations = transactions.Min(tx => tx.Confirmations),
            Status = PaymentStatus.Confirmed
        };
        
        await MarkInvoiceAsPaid(invoice, payment);
    }
}
```

### Late Payments

If payment arrives after invoice expires:

```csharp
if (invoice.Status == InvoiceStatus.Expired)
{
    if (receivedAmount >= invoiceAmount)
    {
        // Mark as late payment
        invoice.Status = InvoiceStatus.Paid;
        invoice.ExceptionStatus = InvoiceExceptionStatus.PaidLate;
        
        await _invoiceRepository.UpdateInvoice(invoice.Id, invoice);
        
        // Still trigger webhooks
        await _eventAggregator.Publish(
            new InvoiceEvent(invoice, InvoiceEvent.PaidAfterExpiration)
        );
    }
}
```

### Double Spend Attempts

Monero's consensus mechanism prevents double spends, but unconfirmed transactions can be replaced:

```csharp
if (confirmations == 0)
{
    // Don't mark as paid yet
    invoice.Status = InvoiceStatus.Processing;
    
    _logger.LogInformation(
        "Payment detected but unconfirmed for invoice {InvoiceId}",
        invoice.Id
    );
    
    // Wait for confirmations before finalizing
}
```

## Sequence Diagrams

### Complete Payment Flow

```
User          BTCPay Server    MoneroPlugin    Wallet RPC    Blockchain
  │                 │                │              │             │
  │  Create Invoice │                │              │             │
  ├────────────────►│                │              │             │
  │                 │                │              │             │
  │                 │ Generate Addr  │              │             │
  │                 ├───────────────►│              │             │
  │                 │                │              │             │
  │                 │                │ create_address()           │
  │                 │                ├─────────────►│             │
  │                 │                │              │             │
  │                 │                │   Address    │             │
  │                 │                │◄─────────────┤             │
  │                 │                │              │             │
  │                 │   Address      │              │             │
  │                 │◄───────────────┤              │             │
  │                 │                │              │             │
  │   Invoice (XMR) │                │              │             │
  │◄────────────────┤                │              │             │
  │                 │                │              │             │
  │  Send XMR       │                │              │             │
  ├─────────────────┼────────────────┼──────────────┼────────────►│
  │                 │                │              │             │
  │                 │                │              │  TX in mempool
  │                 │                │              │◄────────────┤
  │                 │                │              │             │
  │                 │  Poll Wallet   │              │             │
  │                 │                │◄─────────────│             │
  │                 │                │              │             │
  │                 │                │ get_transfers()            │
  │                 │                ├─────────────►│             │
  │                 │                │              │             │
  │                 │                │  Transfers   │             │
  │                 │                │◄─────────────┤             │
  │                 │                │              │             │
  │                 │   Match TX     │              │             │
  │                 │◄───────────────┤              │             │
  │                 │                │              │             │
  │  Status: Processing              │              │             │
  │◄────────────────┤                │              │             │
  │                 │                │              │             │
  │                 │                │              │  TX confirmed
  │                 │                │              │◄────────────┤
  │                 │                │              │             │
  │                 │  Poll Wallet   │              │             │
  │                 │                │◄─────────────│             │
  │                 │                │              │             │
  │                 │                │ get_transfers()            │
  │                 │                ├─────────────►│             │
  │                 │                │              │             │
  │                 │                │  Transfers (conf: 10)      │
  │                 │                │◄─────────────┤             │
  │                 │                │              │             │
  │                 │   Mark Paid    │              │             │
  │                 │◄───────────────┤              │             │
  │                 │                │              │             │
  │  Status: Paid   │                │              │             │
  │◄────────────────┤                │              │             │
  │                 │                │              │             │
  │                 │  Webhook       │              │             │
  │                 ├───────────────►│              │             │
  │                 │                │              │             │
```

### Address Generation Flow

```
BTCPay Server    MoneroPlugin    MoneroRPCProvider    Wallet RPC
      │                │                  │                │
      │  Create Addr   │                  │                │
      ├───────────────►│                  │                │
      │                │                  │                │
      │                │  CreateAddress() │                │
      │                ├─────────────────►│                │
      │                │                  │                │
      │                │                  │ create_address │
      │                │                  ├───────────────►│
      │                │                  │                │
      │                │                  │  JSON Response │
      │                │                  │◄───────────────┤
      │                │                  │                │
      │                │   Address Object │                │
      │                │◄─────────────────┤                │
      │                │                  │                │
      │  Address Info  │                  │                │
      │◄───────────────┤                  │                │
      │                │                  │                │
```

---

For more information, see:
- [ARCHITECTURE.md](ARCHITECTURE.md) - System architecture
- [RPC.md](RPC.md) - RPC communication details
- [DEVELOPMENT.md](../DEVELOPMENT.md) - Development guide
