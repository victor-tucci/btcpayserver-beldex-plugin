# Architecture Documentation

This document provides an in-depth view of the BTCPay Server Monero plugin architecture.

## Table of Contents

1. [System Overview](#system-overview)
2. [Component Diagram](#component-diagram)
3. [Core Components](#core-components)
4. [Data Flow](#data-flow)
5. [Service Architecture](#service-architecture)
6. [Plugin Lifecycle](#plugin-lifecycle)
7. [Database Schema](#database-schema)
8. [Security Considerations](#security-considerations)

## System Overview

The BTCPay Server Monero plugin is a payment plugin that integrates Monero cryptocurrency support into BTCPay Server. It follows the BTCPay Server plugin architecture and interfaces with external Monero services.

### High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        BTCPay Server                             │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │                    Plugin System                            │ │
│  │  ┌──────────────────────────────────────────────────────┐  │ │
│  │  │           Monero Plugin                              │  │ │
│  │  │  ┌─────────────┐  ┌──────────────┐  ┌────────────┐  │  │ │
│  │  │  │   Payment   │  │   Services   │  │    RPC     │  │  │ │
│  │  │  │   Handlers  │  │   Layer      │  │   Layer    │  │  │ │
│  │  │  └─────────────┘  └──────────────┘  └────────────┘  │  │ │
│  │  └──────────────────────────────────────────────────────┘  │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                             │          │
                    ┌────────┘          └─────────┐
                    ▼                              ▼
        ┌─────────────────────┐      ┌───────────────────────┐
        │      monerod        │      │  monero-wallet-rpc    │
        │  (Blockchain Node)  │◄────►│   (Wallet Service)    │
        └─────────────────────┘      └───────────────────────┘
                    │
                    ▼
        ┌─────────────────────┐
        │   Monero Blockchain │
        └─────────────────────┘
```

### External Dependencies

1. **monerod** (Monero Daemon)
   - Provides blockchain access
   - Validates transactions
   - Propagates network events
   - Exposes JSON-RPC interface on port 18081 (mainnet) or 28081 (testnet)

2. **monero-wallet-rpc** (Wallet RPC Service)
   - Manages wallets and keys
   - Creates addresses and monitors payments
   - Signs and broadcasts transactions
   - Exposes JSON-RPC interface on port 18082 (mainnet) or 28082 (testnet)

3. **BTCPay Server Core**
   - Provides plugin infrastructure
   - Manages invoices and stores
   - Handles UI and authentication
   - Provides database access

## Component Diagram

```
┌────────────────────────────────────────────────────────────────┐
│                         MoneroPlugin                            │
│  (Entry point, Service registration, Configuration)            │
└────────────────────────────────────────────────────────────────┘
                              │
                              │ registers
                              ▼
┌────────────────────────────────────────────────────────────────┐
│                      Service Container                          │
└────────────────────────────────────────────────────────────────┘
         │                    │                    │
         │                    │                    │
         ▼                    ▼                    ▼
┌────────────────┐  ┌─────────────────┐  ┌──────────────────┐
│   Payment      │  │    Services     │  │   Controllers    │
│   Handlers     │  │                 │  │                  │
└────────────────┘  └─────────────────┘  └──────────────────┘
         │                    │                    │
         │                    │                    │
         ▼                    ▼                    ▼
┌────────────────────────────────────────────────────────────────┐
│                       RPC Layer                                 │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐        │
│  │ JsonRpcClient│  │ RPC Provider │  │  RPC Models  │        │
│  └──────────────┘  └──────────────┘  └──────────────┘        │
└────────────────────────────────────────────────────────────────┘
                              │
                              ▼
                  ┌───────────────────────┐
                  │  External Monero RPC  │
                  └───────────────────────┘
```

## Core Components

### 1. MoneroPlugin

**Location**: `MoneroPlugin.cs`

**Purpose**: Plugin entry point and service registration

**Responsibilities**:
- Register payment method handlers
- Configure dependency injection
- Set up UI extensions
- Initialize Monero network configuration
- Configure HTTP clients for RPC communication

**Key Code**:
```csharp
public class MoneroPlugin : BaseBTCPayServerPlugin
{
    public override void Execute(IServiceCollection services)
    {
        // Register network
        var network = new MoneroLikeSpecificBtcPayNetwork() { ... };
        services.AddBTCPayNetwork(network);
        
        // Register services
        services.AddSingleton<MoneroRPCProvider>();
        services.AddHostedService<MoneroListener>();
        services.AddHostedService<MoneroLikeSummaryUpdaterHostedService>();
        
        // Register payment handlers
        services.AddSingleton<IPaymentMethodHandler>(...);
        
        // Register UI extensions
        services.AddUIExtension("store-nav", "...");
    }
}
```

### 2. Payment Handlers

#### MoneroLikePaymentMethodHandler

**Location**: `Payments/MoneroLikePaymentMethodHandler.cs`

**Purpose**: Core payment processing logic

**Responsibilities**:
- Parse payment method details
- Calculate payment amounts
- Generate payment addresses
- Validate payment configurations
- Format payment data for display

**Payment Method Flow**:
```
User Creates Invoice
       ↓
MoneroLikePaymentMethodHandler.CreatePaymentMethodDetails()
       ↓
Generate Monero Address (via RPC)
       ↓
Create MoneroLikeOnChainPaymentMethodDetails
       ↓
Store Payment Details in Database
       ↓
Display Invoice with Monero Payment Info
```

#### MoneroPaymentLinkExtension

**Location**: `Payments/MoneroPaymentLinkExtension.cs`

**Purpose**: Generate payment URIs and QR codes

**Responsibilities**:
- Create `monero:` URI scheme links
- Include payment amount and recipient
- Generate QR codes for mobile wallets

#### MoneroCheckoutModelExtension

**Location**: `Payments/MoneroCheckoutModelExtension.cs`

**Purpose**: Extend checkout UI with Monero-specific data

**Responsibilities**:
- Add Monero payment information to checkout model
- Provide view data for Razor templates
- Handle checkout customization

### 3. Services Layer

#### MoneroRPCProvider

**Location**: `Services/MoneroRPCProvider.cs`

**Purpose**: Wrapper for Monero RPC communication

**Responsibilities**:
- Manage HTTP clients for daemon and wallet RPC
- Execute RPC methods
- Handle RPC errors and retries
- Provide typed interfaces for Monero operations

**Key Methods**:
```csharp
// Daemon RPC
Task<GetInfoResponse> GetDaemonInfo();
Task<GetHeightResponse> GetHeight();

// Wallet RPC
Task<CreateWalletResponse> CreateWallet(string filename, string password);
Task<OpenWalletResponse> OpenWallet(string filename, string password);
Task<GetBalanceResponse> GetBalance(uint accountIndex);
Task<CreateAddressResponse> CreateAddress(uint accountIndex);
Task<GetTransfersResponse> GetTransfers(GetTransfersRequest request);
```

#### MoneroListener

**Location**: `Services/MoneroListener.cs`

**Purpose**: Background service that monitors blockchain for payments

**Responsibilities**:
- Poll wallet for new transactions
- Match transactions to invoices
- Update payment status
- Trigger payment confirmations
- Handle payment webhooks

**Monitoring Flow**:
```
MoneroListener Starts (Background Service)
       ↓
Poll monero-wallet-rpc (every 15 seconds)
       ↓
GetTransfers() for pending payments
       ↓
Match transactions by address/amount
       ↓
Update invoice payment status
       ↓
Check confirmation count
       ↓
Mark invoice as paid (if sufficient confirmations)
       ↓
Trigger webhooks/notifications
```

#### MoneroLikeSummaryUpdaterHostedService

**Location**: `Services/MoneroLikeSummaryUpdaterHostedService.cs`

**Purpose**: Update wallet sync status periodically

**Responsibilities**:
- Query daemon sync status
- Query wallet balance
- Update sync summary for UI display
- Cache sync information

#### MoneroSyncSummaryProvider

**Location**: `Services/MoneroSyncSummaryProvider.cs`

**Purpose**: Provide wallet sync information

**Responsibilities**:
- Return current sync status
- Provide balance information
- Display sync progress in UI

### 4. RPC Layer

#### JsonRpcClient

**Location**: `RPC/JsonRpcClient.cs`

**Purpose**: Generic JSON-RPC 2.0 client

**Responsibilities**:
- Send JSON-RPC requests
- Parse JSON-RPC responses
- Handle RPC errors
- Support authentication

**Request Flow**:
```csharp
var request = new JsonRpcRequest
{
    JsonRpc = "2.0",
    Id = "0",
    Method = "method_name",
    Params = requestObject
};

var response = await httpClient.PostAsync(uri, jsonContent);
var result = await JsonSerializer.DeserializeAsync<JsonRpcResponse<T>>(response);
```

#### RPC Models

**Location**: `RPC/Models/`

**Purpose**: Typed request/response objects

**Examples**:
- `CreateWalletRequest` / `CreateWalletResponse`
- `GetBalanceRequest` / `GetBalanceResponse`
- `GetTransfersRequest` / `GetTransfersResponse`
- `CreateAddressRequest` / `CreateAddressResponse`

### 5. Controllers

#### MoneroLikeStoreController

**Location**: `Controllers/MoneroLikeStoreController.cs`

**Purpose**: Store-level wallet management UI

**Responsibilities**:
- Display wallet configuration page
- Handle wallet creation/import
- Generate new addresses
- Show wallet balance and transactions
- Manage wallet settings

**Routes**:
- `GET /stores/{storeId}/plugins/monero` - Wallet management page
- `POST /stores/{storeId}/plugins/monero/create` - Create wallet
- `POST /stores/{storeId}/plugins/monero/import` - Import wallet

#### MoneroDaemonCallbackController

**Location**: `Controllers/MoneroDaemonCallbackController.cs`

**Purpose**: Receive notifications from monerod

**Responsibilities**:
- Handle block notifications
- Handle transaction notifications
- Trigger payment status updates

**Routes**:
- `GET /monerolikedaemoncallback/block` - Block notification
- `GET /monerolikedaemoncallback/tx` - Transaction notification

### 6. Views

**Location**: `Views/Monero/`

**Purpose**: Razor views for UI

**Key Views**:
- `GetStoreMoneroLikePaymentMethod.cshtml` - Wallet management page
- `ViewMoneroLikePaymentData.cshtml` - Payment details display
- `MoneroSyncSummary.cshtml` - Sync status widget
- `StoreNavMoneroExtension.cshtml` - Navigation menu extension

## Data Flow

### Invoice Creation Flow

```
1. User creates invoice
   └─► BTCPay Server Invoice Controller
       
2. Invoice includes Monero payment method
   └─► MoneroLikePaymentMethodHandler.CreatePaymentMethodDetails()
       
3. Check if wallet exists for store
   ├─► Yes: Use existing wallet
   └─► No: Prompt user to create wallet
       
4. Generate subaddress for payment
   └─► MoneroRPCProvider.CreateAddress(accountIndex)
       └─► monero-wallet-rpc
           
5. Save payment details
   └─► MoneroLikeOnChainPaymentMethodDetails
       └─► BTCPay Server Database
       
6. Return invoice with payment info
   └─► Display to user (QR code, address, amount)
```

### Payment Detection Flow

```
1. MoneroListener polls wallet (every 15 seconds)
   └─► MoneroRPCProvider.GetTransfers()
       └─► monero-wallet-rpc
       
2. New transaction found
   └─► Extract: txid, amount, address, confirmations
       
3. Match transaction to invoice
   └─► Query BTCPay Server for pending invoices
       └─► Match by address and amount
       
4. Update payment status
   ├─► 0 confirmations: Invoice marked as "processing"
   ├─► 1-9 confirmations: Invoice marked as "processing" (progress shown)
   └─► 10+ confirmations: Invoice marked as "paid"
       
5. Trigger notifications
   └─► BTCPay Server webhooks
       └─► External systems notified
```

### Wallet Management Flow

```
1. User navigates to Store > Monero Wallet
   └─► MoneroLikeStoreController.GetStoreMoneroLikePaymentMethod()
       
2. Display wallet status
   ├─► Wallet exists
   │   └─► Show: Balance, Address, Transactions
   └─► Wallet doesn't exist
       └─► Show: Create/Import options
       
3. User creates wallet
   └─► MoneroLikeStoreController.CreateWallet()
       └─► MoneroRPCProvider.CreateWallet(filename, password)
           └─► monero-wallet-rpc
               └─► Wallet file saved to disk
               
4. Wallet details saved
   └─► BTCPay Server Database (store settings)
       
5. Redirect to wallet page
   └─► Display new wallet info
```

## Service Architecture

### Background Services

The plugin uses ASP.NET Core hosted services for background tasks:

```csharp
public class MoneroListener : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(CheckPayments, null, TimeSpan.Zero, TimeSpan.FromSeconds(15));
    }
    
    private async void CheckPayments(object state)
    {
        // Poll for new payments
        // Update invoice statuses
        // Trigger webhooks
    }
    
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        return Task.CompletedTask;
    }
}
```

### Service Dependencies

```
MoneroPlugin
    │
    ├─► MoneroRPCProvider (Singleton)
    │   └─► HttpClient (Named: "XMRclient")
    │
    ├─► MoneroListener (Hosted Service)
    │   ├─► MoneroRPCProvider
    │   ├─► InvoiceRepository
    │   └─► EventAggregator
    │
    ├─► MoneroLikeSummaryUpdaterHostedService (Hosted Service)
    │   └─► MoneroRPCProvider
    │
    ├─► MoneroLikePaymentMethodHandler (Singleton)
    │   ├─► MoneroRPCProvider
    │   └─► BTCPayNetworkProvider
    │
    └─► Controllers (Scoped)
        ├─► MoneroRPCProvider
        ├─► StoreRepository
        └─► UserManager
```

### Configuration Management

Configuration is loaded from environment variables:

```csharp
var configuration = serviceProvider.GetService<IConfiguration>();

// Environment variable: BTCPAY_XMR_DAEMON_URI
var daemonUri = configuration.GetOrDefault<Uri>("XMR_daemon_uri", null);

// Environment variable: BTCPAY_XMR_WALLET_DAEMON_URI
var walletDaemonUri = configuration.GetOrDefault<Uri>("XMR_wallet_daemon_uri", null);

// Environment variable: BTCPAY_XMR_WALLET_DAEMON_WALLETDIR
var walletDir = configuration.GetOrDefault<string>("XMR_wallet_daemon_walletdir", null);
```

## Plugin Lifecycle

### Initialization

```
1. BTCPay Server starts
   ↓
2. Plugin system loads plugins
   ↓
3. MoneroPlugin.Execute() called
   ↓
4. Services registered in DI container
   ↓
5. MoneroPlugin.Initialize() (if needed)
   ↓
6. Hosted services start
   ├─► MoneroListener
   └─► MoneroLikeSummaryUpdaterHostedService
   ↓
7. Plugin ready
```

### Runtime

```
During Runtime:
   │
   ├─► Invoice creation
   │   └─► MoneroLikePaymentMethodHandler invoked
   │
   ├─► Payment monitoring
   │   └─► MoneroListener polls wallet
   │
   ├─► User navigation
   │   └─► Controllers handle requests
   │
   └─► Blockchain events
       └─► MoneroDaemonCallbackController receives notifications
```

### Shutdown

```
1. BTCPay Server shutdown initiated
   ↓
2. IHostedService.StopAsync() called
   ├─► MoneroListener stopped
   └─► MoneroLikeSummaryUpdaterHostedService stopped
   ↓
3. Timers disposed
   ↓
4. In-flight RPC calls completed
   ↓
5. Plugin unloaded
```

## Database Schema

The plugin uses BTCPay Server's existing database but adds:

### Payment Method Details

Stored as JSON in `InvoicePaymentMethod` table:

```json
{
  "address": "4ABC...XYZ",
  "accountIndex": 0,
  "addressIndex": 5,
  "cryptoCode": "XMR"
}
```

### Store Settings

Stored as JSON in `StoreData.StoreBlob`:

```json
{
  "MoneroLikePaymentMethod": {
    "WalletFileLocation": "/wallet/store_abc123",
    "AccountIndex": 0
  }
}
```

### Payment Data

Stored as JSON in `PaymentData`:

```json
{
  "txid": "abc123...",
  "confirmations": 10,
  "amount": "1.234567890123",
  "address": "4ABC...XYZ"
}
```

## Security Considerations

### Wallet Security

1. **Single Wallet Per Instance**
   - All stores share one wallet
   - Subaddresses separate payments
   - Not suitable for multi-tenant deployments

2. **Private Key Management**
   - Keys stored in wallet files
   - Wallet files protected by host filesystem permissions
   - No private keys in database

3. **RPC Authentication**
   - Support for daemon username/password
   - HTTP Basic Auth for RPC endpoints
   - Consider VPN or private network for RPC access

### Network Security

1. **TLS for Production**
   - Use HTTPS for BTCPay Server
   - Consider TLS for RPC if over public network

2. **Firewall Rules**
   - Restrict access to RPC ports (18081, 18082)
   - Only BTCPay Server should access wallet RPC

### Data Security

1. **Sensitive Data**
   - Wallet passwords not stored in database
   - View keys may be exposed for transaction monitoring
   - Spend keys remain in wallet files

2. **Transaction Privacy**
   - Monero provides transaction privacy by default
   - Stealth addresses protect recipient privacy
   - Ring signatures protect sender privacy

### Best Practices

1. **Production Deployment**
   - Run monerod and wallet RPC on same host as BTCPay Server
   - Use Unix sockets instead of TCP if possible
   - Regular wallet backups
   - Monitor wallet file permissions

2. **Development**
   - Use testnet/regtest for development
   - Never use real funds in development
   - Separate development and production environments

## Performance Considerations

### Scalability Limits

1. **Single Wallet Architecture**
   - All stores share one wallet
   - Limited by wallet RPC performance
   - Subaddress limit: ~4 billion per account

2. **Polling Frequency**
   - Default: 15 second interval
   - Trade-off: responsiveness vs. RPC load
   - Adjustable in code if needed

3. **Database Queries**
   - Efficient querying for pending invoices
   - Indexed by payment method and status

### Optimization Opportunities

1. **RPC Call Batching**
   - Batch multiple RPC calls
   - Reduce network overhead

2. **Caching**
   - Cache wallet balance
   - Cache daemon info
   - TTL-based invalidation

3. **Async Processing**
   - All I/O operations are async
   - Non-blocking payment detection
   - Background service isolation

## Future Architecture Enhancements

### Potential Improvements

1. **Multiple Wallets**
   - One wallet per store
   - Better multi-tenancy support
   - Isolated permissions

2. **View-Only Wallets**
   - Import view keys only
   - Monitor payments without spend capability
   - Enhanced security

3. **External Wallet Support**
   - Connect to user-managed wallets
   - API-based integration
   - Reduced server load

4. **Event-Driven Architecture**
   - Replace polling with webhooks
   - Real-time payment notifications
   - Reduced RPC overhead

5. **Horizontal Scaling**
   - Distributed payment monitoring
   - Message queue integration
   - Multiple BTCPay Server instances

---

For implementation details, see [DEVELOPMENT.md](../DEVELOPMENT.md).
