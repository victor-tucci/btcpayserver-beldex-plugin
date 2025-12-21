# RPC Documentation

This document details the Monero RPC integration in the BTCPay Server Monero plugin.

## Table of Contents

1. [Overview](#overview)
2. [JSON-RPC Protocol](#json-rpc-protocol)
3. [RPC Configuration](#rpc-configuration)
4. [Daemon RPC Methods](#daemon-rpc-methods)
5. [Wallet RPC Methods](#wallet-rpc-methods)
6. [Error Handling](#error-handling)
7. [RPC Models](#rpc-models)
8. [Testing RPC Calls](#testing-rpc-calls)
9. [Common Issues](#common-issues)

## Overview

The plugin communicates with Monero services using JSON-RPC 2.0 protocol over HTTP. Two separate RPC endpoints are used:

1. **Daemon RPC** (`monerod`) - Blockchain queries
2. **Wallet RPC** (`monero-wallet-rpc`) - Wallet operations

### RPC Architecture

```
┌──────────────────────┐
│  MoneroRPCProvider   │
│                      │
│  ┌────────────────┐  │
│  │ Daemon Client  │  │ ──────► monerod (port 18081)
│  └────────────────┘  │
│                      │
│  ┌────────────────┐  │
│  │ Wallet Client  │  │ ──────► monero-wallet-rpc (port 18082)
│  └────────────────┘  │
└──────────────────────┘
```

## JSON-RPC Protocol

### Request Format

All RPC requests follow JSON-RPC 2.0 specification:

```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "method": "method_name",
  "params": {
    "param1": "value1",
    "param2": "value2"
  }
}
```

### Response Format

Successful response:

```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "result": {
    "field1": "value1",
    "field2": "value2"
  }
}
```

Error response:

```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "error": {
    "code": -1,
    "message": "Error description"
  }
}
```

### HTTP Headers

```http
POST /json_rpc HTTP/1.1
Host: localhost:18081
Content-Type: application/json
Content-Length: 123
```

For authenticated RPC:

```http
Authorization: Basic dXNlcm5hbWU6cGFzc3dvcmQ=
```

## RPC Configuration

### Environment Variables

```bash
# Daemon RPC endpoint
BTCPAY_XMR_DAEMON_URI=http://127.0.0.1:18081

# Daemon authentication (optional)
BTCPAY_XMR_DAEMON_USERNAME=myuser
BTCPAY_XMR_DAEMON_PASSWORD=mypassword

# Wallet RPC endpoint
BTCPAY_XMR_WALLET_DAEMON_URI=http://127.0.0.1:18082

# Wallet directory for created wallets
BTCPAY_XMR_WALLET_DAEMON_WALLETDIR=/home/user/wallets
```

### Configuration in Code

```csharp
public class MoneroLikeConfiguration
{
    public Dictionary<string, MoneroLikeConfigurationItem> MoneroLikeConfigurationItems { get; set; }
}

public class MoneroLikeConfigurationItem
{
    public Uri DaemonRpcUri { get; set; }
    public Uri InternalWalletRpcUri { get; set; }
    public string WalletDirectory { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
}
```

## Daemon RPC Methods

### get_info

Get general information about the daemon and blockchain.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "method": "get_info"
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "result": {
    "height": 2345678,
    "target_height": 2345678,
    "difficulty": 123456789,
    "tx_count": 1234567,
    "tx_pool_size": 10,
    "alt_blocks_count": 0,
    "outgoing_connections_count": 8,
    "incoming_connections_count": 5,
    "rpc_connections_count": 1,
    "white_peerlist_size": 100,
    "grey_peerlist_size": 200,
    "mainnet": true,
    "testnet": false,
    "stagenet": false,
    "nettype": "mainnet",
    "top_block_hash": "abc123...",
    "cumulative_difficulty": 98765432109876,
    "block_size_limit": 600000,
    "block_size_median": 300000,
    "start_time": 1234567890,
    "status": "OK"
  }
}
```

**C# Usage:**
```csharp
var info = await moneroRPCProvider.GetDaemonInfo();
Console.WriteLine($"Current height: {info.Height}");
Console.WriteLine($"Synced: {info.Height >= info.TargetHeight}");
```

### get_height

Get current blockchain height.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "method": "get_height"
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "result": {
    "height": 2345678
  }
}
```

**C# Usage:**
```csharp
var heightResponse = await moneroRPCProvider.GetHeight();
Console.WriteLine($"Blockchain height: {heightResponse.Height}");
```

### get_block_count

Get the number of blocks in the blockchain.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "method": "get_block_count"
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "result": {
    "count": 2345678,
    "status": "OK"
  }
}
```

## Wallet RPC Methods

### create_wallet

Create a new wallet file.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "method": "create_wallet",
  "params": {
    "filename": "my_wallet",
    "password": "supersecret",
    "language": "English"
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "result": {}
}
```

**C# Model:**
```csharp
public class CreateWalletRequest
{
    [JsonPropertyName("filename")]
    public string Filename { get; set; }
    
    [JsonPropertyName("password")]
    public string Password { get; set; }
    
    [JsonPropertyName("language")]
    public string Language { get; set; } = "English";
}
```

**C# Usage:**
```csharp
await moneroRPCProvider.CreateWallet("store_wallet_123", "password123");
```

### open_wallet

Open an existing wallet file.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "method": "open_wallet",
  "params": {
    "filename": "my_wallet",
    "password": "supersecret"
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "result": {}
}
```

**C# Usage:**
```csharp
await moneroRPCProvider.OpenWallet("store_wallet_123", "password123");
```

### close_wallet

Close currently opened wallet.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "method": "close_wallet"
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "result": {}
}
```

### get_balance

Get wallet balance.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "method": "get_balance",
  "params": {
    "account_index": 0
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "result": {
    "balance": 10000000000000,
    "unlocked_balance": 9500000000000,
    "multisig_import_needed": false,
    "per_subaddress": [
      {
        "address_index": 0,
        "address": "4ABC...XYZ",
        "balance": 10000000000000,
        "unlocked_balance": 9500000000000,
        "label": "Primary account",
        "num_unspent_outputs": 5
      }
    ]
  }
}
```

**Note**: Amounts are in atomic units (piconero). 1 XMR = 10^12 piconero.

**C# Usage:**
```csharp
var balance = await moneroRPCProvider.GetBalance(accountIndex: 0);
decimal xmr = balance.Balance / 1000000000000m;
Console.WriteLine($"Balance: {xmr} XMR");
```

### get_address

Get wallet address(es).

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "method": "get_address",
  "params": {
    "account_index": 0
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "result": {
    "address": "4ABC...XYZ",
    "addresses": [
      {
        "address": "4ABC...XYZ",
        "label": "Primary account",
        "address_index": 0,
        "used": true
      },
      {
        "address": "8DEF...UVW",
        "label": "Invoice 123",
        "address_index": 1,
        "used": false
      }
    ]
  }
}
```

**C# Usage:**
```csharp
var addressResponse = await moneroRPCProvider.GetAddress(accountIndex: 0);
Console.WriteLine($"Primary address: {addressResponse.Address}");
```

### create_address

Create a new subaddress.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "method": "create_address",
  "params": {
    "account_index": 0,
    "label": "Invoice payment 12345"
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "result": {
    "address": "8DEF...UVW",
    "address_index": 5
  }
}
```

**C# Usage:**
```csharp
var newAddress = await moneroRPCProvider.CreateAddress(
    accountIndex: 0, 
    label: "Invoice ABC123"
);
Console.WriteLine($"New address: {newAddress.Address}");
Console.WriteLine($"Address index: {newAddress.AddressIndex}");
```

### get_transfers

Get incoming and outgoing transfers.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "method": "get_transfers",
  "params": {
    "in": true,
    "out": false,
    "pending": true,
    "failed": false,
    "pool": true,
    "account_index": 0,
    "subaddr_indices": [0, 1, 2]
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "result": {
    "in": [
      {
        "txid": "abc123...",
        "payment_id": "0000000000000000",
        "height": 2345678,
        "timestamp": 1234567890,
        "amount": 1000000000000,
        "confirmations": 10,
        "fee": 1000000,
        "note": "",
        "destinations": [
          {
            "amount": 1000000000000,
            "address": "4ABC...XYZ"
          }
        ],
        "type": "in",
        "unlock_time": 0,
        "subaddr_index": {
          "major": 0,
          "minor": 5
        },
        "address": "4ABC...XYZ"
      }
    ],
    "pool": []
  }
}
```

**C# Usage:**
```csharp
var transfers = await moneroRPCProvider.GetTransfers(new GetTransfersRequest
{
    In = true,
    Pending = true,
    Pool = true,
    AccountIndex = 0
});

foreach (var tx in transfers.In)
{
    Console.WriteLine($"Transaction: {tx.Txid}");
    Console.WriteLine($"Amount: {tx.Amount} piconero");
    Console.WriteLine($"Confirmations: {tx.Confirmations}");
}
```

### make_uri

Create a Monero payment URI.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "method": "make_uri",
  "params": {
    "address": "4ABC...XYZ",
    "amount": 1000000000000,
    "payment_id": "",
    "recipient_name": "BTCPay Server",
    "tx_description": "Invoice payment"
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "result": {
    "uri": "monero:4ABC...XYZ?tx_amount=1.000000000000&recipient_name=BTCPay%20Server&tx_description=Invoice%20payment"
  }
}
```

**C# Usage:**
```csharp
var uri = await moneroRPCProvider.MakeUri(
    address: "4ABC...XYZ",
    amount: 1000000000000,
    description: "Invoice payment"
);
Console.WriteLine($"Payment URI: {uri.Uri}");
```

### transfer

Send Monero to an address.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "method": "transfer",
  "params": {
    "destinations": [
      {
        "amount": 1000000000000,
        "address": "4ABC...XYZ"
      }
    ],
    "account_index": 0,
    "subaddr_indices": [0],
    "priority": 0,
    "ring_size": 16,
    "get_tx_key": true
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "result": {
    "tx_hash": "abc123...",
    "tx_key": "def456...",
    "amount": 1000000000000,
    "fee": 1000000,
    "tx_blob": "",
    "tx_metadata": "",
    "multisig_txset": ""
  }
}
```

### get_fee_estimate

Get fee estimate for transactions.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "method": "get_fee_estimate"
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "result": {
    "fee": 1000000,
    "quantization_mask": 10000
  }
}
```

## Error Handling

### Common Error Codes

| Code | Meaning | Description |
|------|---------|-------------|
| -1 | Generic error | General RPC error |
| -2 | Invalid params | Invalid parameters provided |
| -4 | Wallet not found | Wallet file doesn't exist |
| -13 | Wallet already exists | Cannot create wallet (file exists) |
| -21 | Wallet already opened | Cannot open wallet (already open) |
| -17 | Invalid address | Address format is invalid |

### Error Response Example

```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "error": {
    "code": -4,
    "message": "Cannot open wallet: wallet not found"
  }
}
```

### C# Error Handling

```csharp
public class MoneroRPCException : Exception
{
    public int ErrorCode { get; set; }
    
    public MoneroRPCException(string message, int errorCode) 
        : base(message)
    {
        ErrorCode = errorCode;
    }
}
```

**Usage:**
```csharp
try
{
    await moneroRPCProvider.OpenWallet("nonexistent_wallet", "password");
}
catch (MoneroRPCException ex)
{
    if (ex.ErrorCode == -4)
    {
        Console.WriteLine("Wallet not found. Creating new wallet...");
        await moneroRPCProvider.CreateWallet("nonexistent_wallet", "password");
    }
    else
    {
        throw;
    }
}
```

## RPC Models

### Request Models

Located in `RPC/Models/`:

```csharp
// CreateWalletRequest.cs
public class CreateWalletRequest
{
    [JsonPropertyName("filename")]
    public string Filename { get; set; }
    
    [JsonPropertyName("password")]
    public string Password { get; set; }
    
    [JsonPropertyName("language")]
    public string Language { get; set; } = "English";
}

// GetTransfersRequest.cs
public class GetTransfersRequest
{
    [JsonPropertyName("in")]
    public bool In { get; set; }
    
    [JsonPropertyName("out")]
    public bool Out { get; set; }
    
    [JsonPropertyName("pending")]
    public bool Pending { get; set; }
    
    [JsonPropertyName("pool")]
    public bool Pool { get; set; }
    
    [JsonPropertyName("account_index")]
    public uint AccountIndex { get; set; }
}
```

### Response Models

```csharp
// GetBalanceResponse.cs
public class GetBalanceResponse
{
    [JsonPropertyName("balance")]
    public long Balance { get; set; }
    
    [JsonPropertyName("unlocked_balance")]
    public long UnlockedBalance { get; set; }
    
    [JsonPropertyName("per_subaddress")]
    public List<SubaddressBalance> PerSubaddress { get; set; }
}

// CreateAddressResponse.cs
public class CreateAddressResponse
{
    [JsonPropertyName("address")]
    public string Address { get; set; }
    
    [JsonPropertyName("address_index")]
    public uint AddressIndex { get; set; }
}
```

## Testing RPC Calls

### Manual Testing with curl

Test daemon RPC:
```bash
curl -X POST http://localhost:18081/json_rpc \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":"0","method":"get_height"}'
```

Test wallet RPC:
```bash
curl -X POST http://localhost:18082/json_rpc \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":"0","method":"get_balance","params":{"account_index":0}}'
```

With authentication:
```bash
curl -X POST http://localhost:18081/json_rpc \
  -u username:password \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":"0","method":"get_info"}'
```

### Unit Testing

Example unit test for RPC provider:

```csharp
[Fact]
public async Task CanCreateAndOpenWallet()
{
    // Arrange
    var walletName = $"test_wallet_{Guid.NewGuid()}";
    var password = "test_password";
    
    // Act
    await _moneroRPCProvider.CreateWallet(walletName, password);
    await _moneroRPCProvider.OpenWallet(walletName, password);
    
    // Assert
    var balance = await _moneroRPCProvider.GetBalance(0);
    Assert.NotNull(balance);
    Assert.Equal(0, balance.Balance);
}
```

## Common Issues

### Connection Refused

**Error**: `Connection refused` or `No connection could be made`

**Solutions**:
- Verify monerod/wallet-rpc is running: `docker ps`
- Check port numbers (18081 for daemon, 18082 for wallet)
- Verify firewall rules
- Check Docker network configuration

### Authentication Failed

**Error**: `401 Unauthorized`

**Solutions**:
- Verify username/password in configuration
- Check daemon/wallet RPC authentication settings
- Ensure credentials are properly encoded

### Wallet Not Found

**Error**: `Cannot open wallet: wallet not found`

**Solutions**:
- Verify wallet file exists in wallet directory
- Check `BTCPAY_XMR_WALLET_DAEMON_WALLETDIR` setting
- Ensure wallet was created successfully
- Check file permissions

### Timeout Errors

**Error**: `The operation has timed out`

**Solutions**:
- Increase HTTP client timeout
- Check network connectivity
- Verify daemon/wallet is not overloaded
- Consider reducing RPC call frequency

### Invalid Address Format

**Error**: `Invalid address`

**Solutions**:
- Verify address is for correct network (mainnet/testnet)
- Check address length (95 characters for standard address)
- Ensure no extra whitespace
- Validate checksum

---

For more information, see:
- [Monero RPC Documentation](https://getmonero.dev/interacting/monero-wallet-rpc.html)
- [Monero Daemon RPC](https://getmonero.dev/interacting/daemon-rpc.html)
