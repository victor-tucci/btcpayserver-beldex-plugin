# Developer Documentation

This document provides comprehensive guidance for developers working on the BTCPay Server Monero plugin.

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Prerequisites](#prerequisites)
4. [Development Setup](#development-setup)
5. [Project Structure](#project-structure)
6. [Development Workflow](#development-workflow)
7. [Testing Strategy](#testing-strategy)
8. [Code Formatting and Standards](#code-formatting-and-standards)
9. [Debugging](#debugging)
10. [Payment Flow](#payment-flow)
11. [RPC Communication](#rpc-communication)
12. [Building and Packaging](#building-and-packaging)
13. [Deployment](#deployment)
14. [Troubleshooting](#troubleshooting)
15. [Contributing](#contributing)

## Overview

The BTCPay Server Monero plugin extends BTCPay Server to enable Monero (XMR) payment processing. It integrates with:

- **monerod**: The Monero daemon for blockchain access
- **monero-wallet-rpc**: The Monero wallet RPC service for wallet operations
- **BTCPay Server**: The core payment processing system

### Key Features

- Accepts Monero payments on BTCPay Server
- Provides wallet management through BTCPay Server UI
- Monitors blockchain for payment confirmations
- Generates payment addresses and invoices
- Supports testnet and mainnet configurations

### Important Warning

⚠️ **This plugin shares a single Monero wallet across all stores in the BTCPay Server instance.** Use this plugin only if you are not sharing your BTCPay Server instance with others.

## Architecture

### System Components

```
┌─────────────────┐         ┌──────────────────┐         ┌────────────────┐
│                 │         │                  │         │                │
│  BTCPay Server  │◄───────►│  Monero Plugin   │◄───────►│   monerod      │
│                 │         │                  │         │  (Daemon RPC)  │
└─────────────────┘         └──────────────────┘         └────────────────┘
                                     ▲
                                     │
                                     ▼
                            ┌──────────────────┐
                            │ monero-wallet-rpc│
                            │  (Wallet RPC)    │
                            └──────────────────┘
```

### Plugin Components

1. **MoneroPlugin** (`MoneroPlugin.cs`)
   - Main entry point for the plugin
   - Registers services and configures dependency injection
   - Sets up payment method handlers and UI extensions

2. **Services Layer**
   - `MoneroRPCProvider`: Manages communication with Monero RPC services
   - `MoneroListener`: Listens for blockchain events and payment confirmations
   - `MoneroLikeSummaryUpdaterHostedService`: Updates sync status periodically
   - `MoneroSyncSummaryProvider`: Provides wallet sync information

3. **Payment Handlers**
   - `MoneroLikePaymentMethodHandler`: Core payment processing logic
   - `MoneroPaymentLinkExtension`: Generates payment links and QR codes
   - `MoneroCheckoutModelExtension`: Extends checkout UI with Monero-specific data

4. **RPC Layer** (`RPC/`)
   - `JsonRpcClient`: Generic JSON-RPC client implementation
   - `Models/`: Request/response models for Monero RPC calls
   - `MoneroEvent`: Handles blockchain event notifications

5. **Controllers**
   - `MoneroLikeStoreController`: Manages store-level Monero wallet configuration
   - `MoneroDaemonCallbackController`: Receives notifications from monerod

6. **Views** (`Views/Monero/`)
   - Razor views for wallet management UI
   - Payment data display components
   - Navigation extensions

## Prerequisites

### Required Software

- **.NET 8.0 SDK** or later
  ```bash
  # Verify installation
  dotnet --version
  ```

- **Git** with submodules support
  ```bash
  git --version
  ```

- **Docker** and **Docker Compose** (for integration tests and local development)
  ```bash
  docker --version
  docker compose version
  ```

### Recommended IDEs

- **JetBrains Rider** (recommended)
  - Supports hot reload for `.cshtml` files
  - Better C# development experience
  - Built-in debugging tools

- **Visual Studio 2022** or later
  - Full .NET development support
  - Note: Does not support hot reload for plugin development

- **Visual Studio Code** with C# extension
  - Lightweight alternative
  - Requires manual configuration

### Optional Tools

- **JetBrains dotCover** (for code coverage)
  ```bash
  dotnet tool install --global JetBrains.dotCover.CommandLineTools --version 2025.1.6
  ```

## Development Setup

### 1. Clone Repositories

Clone the plugin repository with submodules (BTCPay Server core is included as a submodule):

```bash
git clone --recurse-submodules https://github.com/btcpay-monero/btcpayserver-monero-plugin
cd btcpayserver-monero-plugin
```

If you already cloned without submodules:

```bash
git submodule update --init --recursive
```

### 2. Restore Dependencies

```bash
dotnet restore
```

### 3. Build the Plugin

```bash
dotnet build btcpay-monero-plugin.sln
```

### 4. Set Up Local Development Environment

#### Start Development Services

The plugin requires monerod and monero-wallet-rpc services. Use Docker Compose to start them:

```bash
cd BTCPayServer.Plugins.IntegrationTests/
docker compose up -d dev
```

This starts:
- PostgreSQL database
- NBXplorer (Bitcoin block explorer)
- bitcoind (Bitcoin regtest node)
- monerod (Monero regtest daemon)
- monero-wallet-rpc (Monero wallet service)

To reset the environment:

```bash
docker compose down -v
docker compose up -d dev
```

#### Configure BTCPay Server for Plugin Development

For **Rider** or **Visual Studio**, you need to set up BTCPay Server to load the plugin.

1. Clone BTCPay Server alongside the plugin repository:

```bash
cd ..
git clone https://github.com/btcpayserver/btcpayserver
```

2. Create `appsettings.dev.json` in `btcpayserver/BTCPayServer/`:

```json
{
  "DEBUG_PLUGINS": "..\\..\\btcpayserver-monero-plugin\\Plugins\\Monero\\bin\\Debug\\net8.0\\BTCPayServer.Plugins.Monero.dll",
  "XMR_DAEMON_URI": "http://127.0.0.1:18081",
  "XMR_WALLET_DAEMON_URI": "http://127.0.0.1:18082"
}
```

**Note**: Adjust the path based on your directory structure. Use forward slashes on Linux/macOS:

```json
{
  "DEBUG_PLUGINS": "../../btcpayserver-monero-plugin/Plugins/Monero/bin/Debug/net8.0/BTCPayServer.Plugins.Monero.dll",
  "XMR_DAEMON_URI": "http://127.0.0.1:18081",
  "XMR_WALLET_DAEMON_URI": "http://127.0.0.1:18082"
}
```

#### For Visual Studio Code

Create or update `.vscode/launch.json`:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Launch BTCPay Server with Monero Plugin",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "${workspaceFolder}/../btcpayserver/BTCPayServer/bin/Debug/net8.0/BTCPayServer.dll",
      "args": [],
      "cwd": "${workspaceFolder}/../btcpayserver/BTCPayServer",
      "stopAtEntry": false,
      "serverReadyAction": {
        "action": "openExternally",
        "pattern": "\\bNow listening on:\\s+(https?://\\S+)"
      },
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ASPNETCORE_URLS": "https://localhost:14142;http://localhost:14143"
      },
      "sourceFileMap": {
        "/Views": "${workspaceFolder}/../btcpayserver/BTCPayServer/Views"
      }
    }
  ]
}
```

#### For Rider

Open `launch.json` in `.vscode` folder and set the `launchSettingsProfile` to `Altcoins-HTTPS`.

### 5. Run BTCPay Server with Plugin

1. **Build the plugin** first (important: changes to the plugin require rebuilding):
   ```bash
   cd /path/to/btcpayserver-monero-plugin
   dotnet build
   ```

2. **Set BTCPay Server as startup project** in your IDE

3. **Run in debug mode**

4. **Access BTCPay Server** at `https://localhost:14142` (or the port configured in your launch settings)

### Hot Reload Support

- **Rider**: Supports hot reload for `.cshtml` files. Edit, save, and refresh the browser.
- **Visual Studio**: Does not support hot reload for plugins.

## Project Structure

```
btcpayserver-monero-plugin/
├── Plugins/
│   └── Monero/                          # Main plugin project
│       ├── Configuration/                # Configuration classes
│       │   └── MoneroLikeConfiguration.cs
│       ├── Controllers/                  # MVC controllers
│       │   ├── MoneroLikeStoreController.cs
│       │   └── MoneroDaemonCallbackController.cs
│       ├── Payments/                     # Payment processing
│       │   ├── MoneroLikePaymentMethodHandler.cs
│       │   ├── MoneroLikePaymentData.cs
│       │   ├── MoneroPaymentLinkExtension.cs
│       │   └── MoneroCheckoutModelExtension.cs
│       ├── RPC/                          # RPC communication
│       │   ├── JsonRpcClient.cs
│       │   ├── MoneroEvent.cs
│       │   └── Models/                   # RPC request/response models
│       ├── Services/                     # Background services
│       │   ├── MoneroRPCProvider.cs
│       │   ├── MoneroListener.cs
│       │   ├── MoneroLikeSummaryUpdaterHostedService.cs
│       │   └── MoneroSyncSummaryProvider.cs
│       ├── Utils/                        # Utility classes
│       │   └── MoneroMoney.cs
│       ├── ViewModels/                   # View models
│       ├── Views/                        # Razor views
│       │   └── Monero/
│       ├── MoneroPlugin.cs               # Plugin entry point
│       ├── MoneroLikeSpecificBtcPayNetwork.cs
│       └── BTCPayServer.Plugins.Monero.csproj
├── BTCPayServer.Plugins.UnitTests/       # Unit tests
│   └── Monero/
│       ├── Configuration/
│       ├── Payments/
│       ├── RPC/
│       ├── Utils/
│       └── ViewModels/
├── BTCPayServer.Plugins.IntegrationTests/ # Integration tests
│   ├── Monero/
│   ├── docker-compose.yml                # Test environment
│   └── Dockerfile
├── submodules/
│   └── btcpayserver/                     # BTCPay Server submodule
├── .editorconfig                          # Code formatting rules
├── .globalconfig                          # Analyzer configuration
├── btcpay-monero-plugin.sln              # Solution file
├── global.json                            # .NET SDK version
├── README.md                              # User documentation
└── DEVELOPMENT.md                         # This file
```

### Key Files

- **MoneroPlugin.cs**: Plugin registration and service configuration
- **MoneroLikePaymentMethodHandler.cs**: Core payment processing logic
- **MoneroRPCProvider.cs**: Wrapper for Monero RPC calls
- **MoneroListener.cs**: Monitors blockchain for payment events
- **JsonRpcClient.cs**: Generic JSON-RPC client for Monero communication

## Development Workflow

### Making Changes

1. **Identify the component** you need to modify based on the feature or bug fix

2. **Create a feature branch**:
   ```bash
   git checkout -b feature/your-feature-name
   ```

3. **Make your changes** following the coding standards

4. **Build the plugin**:
   ```bash
   dotnet build
   ```

5. **Run unit tests**:
   ```bash
   dotnet test BTCPayServer.Plugins.UnitTests --verbosity normal
   ```

6. **Test manually** by running BTCPay Server with the plugin loaded

7. **Format your code**:
   ```bash
   dotnet format btcpay-monero-plugin.sln --exclude submodules/* --verbosity diagnostic
   ```

8. **Run integration tests** (if applicable):
   ```bash
   docker compose -f BTCPayServer.Plugins.IntegrationTests/docker-compose.yml run tests
   ```

9. **Commit your changes**:
   ```bash
   git add .
   git commit -m "Description of changes"
   ```

10. **Push and create a pull request**:
    ```bash
    git push origin feature/your-feature-name
    ```

### Common Development Tasks

#### Adding a New RPC Method

1. Create request/response models in `RPC/Models/`:
   ```csharp
   public class MyNewRequest
   {
       [JsonPropertyName("param1")]
       public string Param1 { get; set; }
   }
   
   public class MyNewResponse
   {
       [JsonPropertyName("result")]
       public string Result { get; set; }
   }
   ```

2. Add method to `MoneroRPCProvider.cs`:
   ```csharp
   public async Task<MyNewResponse> MyNewMethod(string param1)
   {
       var request = new MyNewRequest { Param1 = param1 };
       return await SendRPCRequest<MyNewRequest, MyNewResponse>(
           "my_new_method", request, WalletClient);
   }
   ```

3. Add unit tests in `BTCPayServer.Plugins.UnitTests/Monero/RPC/`

#### Modifying Payment Flow

1. Update logic in `MoneroLikePaymentMethodHandler.cs`
2. Modify corresponding view models if needed
3. Update Razor views in `Views/Monero/` if UI changes are required
4. Add or update tests in `BTCPayServer.Plugins.UnitTests/Monero/Payments/`

#### Adding UI Components

1. Create or modify Razor views in `Views/Monero/`
2. Update view models in `ViewModels/`
3. Register UI extensions in `MoneroPlugin.cs` if adding new pages:
   ```csharp
   services.AddUIExtension("extension-point", "/Views/Monero/MyView.cshtml");
   ```

## Testing Strategy

### Unit Tests

Unit tests are located in `BTCPayServer.Plugins.UnitTests/` and test individual components in isolation.

#### Running Unit Tests

```bash
# Run all unit tests
dotnet test BTCPayServer.Plugins.UnitTests --verbosity normal

# Run specific test class
dotnet test BTCPayServer.Plugins.UnitTests --filter FullyQualifiedName~MoneroMoneyTests

# Run with detailed output
dotnet test BTCPayServer.Plugins.UnitTests --verbosity detailed
```

#### Running with Code Coverage

```bash
# Install dotCover if not already installed
dotnet tool install --global JetBrains.dotCover.CommandLineTools --version 2025.1.6

# Run tests with coverage
dotCover cover-dotnet \
  --TargetArguments="test BTCPayServer.Plugins.UnitTests --no-build" \
  --ReportType=HTML \
  --Output=coverage/dotCover.UnitTests.output.html \
  --ReportType=detailedXML \
  --Output=coverage/dotCover.UnitTests.output.xml \
  --filters="-:Assembly=BTCPayServer.Plugins.UnitTests;-:Assembly=testhost;-:Assembly=BTCPayServer;-:Class=AspNetCoreGeneratedDocument.*"

# View HTML report
open coverage/dotCover.UnitTests.output.html  # macOS
xdg-open coverage/dotCover.UnitTests.output.html  # Linux
start coverage/dotCover.UnitTests.output.html  # Windows
```

#### Writing Unit Tests

Follow the existing test patterns. Example:

```csharp
using Xunit;

namespace BTCPayServer.Plugins.UnitTests.Monero.Utils
{
    public class MoneroMoneyTests
    {
        [Fact]
        public void CanParseMoneroAmount()
        {
            // Arrange
            var amount = "1.234567890123";
            
            // Act
            var moneroMoney = MoneroMoney.Parse(amount);
            
            // Assert
            Assert.Equal(1234567890123, moneroMoney.Piconero);
        }
    }
}
```

### Integration Tests

Integration tests are located in `BTCPayServer.Plugins.IntegrationTests/` and test the plugin with real services.

#### Running Integration Tests

```bash
# Build first
dotnet build btcpay-monero-plugin.sln

# Run integration tests
docker compose -f BTCPayServer.Plugins.IntegrationTests/docker-compose.yml run tests

# Clean up after tests
docker compose -f BTCPayServer.Plugins.IntegrationTests/docker-compose.yml down -v
```

#### Integration Test Environment

The `docker-compose.yml` sets up:
- BTCPay Server test instance
- PostgreSQL database
- NBXplorer
- bitcoind (regtest)
- monerod (regtest)
- monero-wallet-rpc

Tests run in a containerized environment that mimics production.

#### Writing Integration Tests

Integration tests should test end-to-end scenarios:

```csharp
[Fact]
public async Task CanCreateMoneroWalletAndReceivePayment()
{
    // Test complete payment flow
    // 1. Create wallet
    // 2. Generate invoice
    // 3. Send payment
    // 4. Verify confirmation
}
```

### Test Best Practices

1. **Unit tests should be fast** - No external dependencies
2. **Integration tests should be realistic** - Use real services in containers
3. **Tests should be isolated** - Each test should be independent
4. **Use descriptive names** - Test names should explain what is being tested
5. **Follow AAA pattern** - Arrange, Act, Assert
6. **Clean up resources** - Ensure tests don't leave artifacts

## Code Formatting and Standards

### Code Style

The project uses the **unmodified** standardized `.editorconfig` from .NET SDK.

#### Apply Latest .editorconfig

```bash
dotnet new editorconfig --force
```

#### Format Code

Format the entire solution:

```bash
dotnet format btcpay-monero-plugin.sln --exclude submodules/* --verbosity diagnostic
```

Format and verify:

```bash
dotnet format btcpay-monero-plugin.sln --exclude submodules/* --verify-no-changes
```

### Code Analysis

The project uses `.globalconfig` for custom analyzer configuration.

Key rules:
- Null reference handling
- Code quality analyzers
- Security analyzers

### Naming Conventions

- **Classes**: PascalCase (e.g., `MoneroRPCProvider`)
- **Methods**: PascalCase (e.g., `GetBalance`)
- **Properties**: PascalCase (e.g., `WalletAddress`)
- **Private fields**: camelCase (e.g., `_logger`)
- **Parameters**: camelCase (e.g., `paymentId`)
- **Constants**: PascalCase (e.g., `DefaultTimeout`)

### File Organization

- One class per file
- File name matches class name
- Related files grouped in folders by feature

### Comments and Documentation

- XML documentation for public APIs
- Comments for complex logic only
- Avoid obvious comments
- Keep comments up to date with code

Example:

```csharp
/// <summary>
/// Retrieves the current balance of the Monero wallet.
/// </summary>
/// <returns>The wallet balance in atomic units (piconero).</returns>
/// <exception cref="MoneroRPCException">Thrown when RPC call fails.</exception>
public async Task<long> GetBalance()
{
    // Implementation
}
```

## Debugging

### Debugging the Plugin in Rider

1. Set breakpoints in plugin code
2. Start BTCPay Server in debug mode
3. Trigger the code path (e.g., create invoice, modify settings)
4. Debug as normal

### Debugging the Plugin in Visual Studio Code

1. Ensure `.vscode/launch.json` is configured correctly
2. Set breakpoints in plugin code
3. Press F5 to start debugging
4. Navigate to the feature in BTCPay Server UI

### Common Debugging Scenarios

#### Debug Payment Processing

1. Set breakpoint in `MoneroLikePaymentMethodHandler.cs`
2. Create an invoice with Monero payment method
3. Step through payment creation logic

#### Debug RPC Communication

1. Set breakpoint in `JsonRpcClient.cs` or `MoneroRPCProvider.cs`
2. Trigger an RPC call (e.g., wallet creation, balance check)
3. Inspect request/response

#### Debug Wallet Listener

1. Set breakpoint in `MoneroListener.cs`
2. Send a test transaction to a monitored address
3. Step through payment detection logic

### Logging

The plugin uses standard .NET logging:

```csharp
_logger.LogInformation("Processing payment: {PaymentId}", paymentId);
_logger.LogWarning("Payment not found: {PaymentId}", paymentId);
_logger.LogError(ex, "Failed to process payment: {PaymentId}", paymentId);
```

View logs in:
- Console output when running locally
- BTCPay Server logs in production

### Troubleshooting Development Issues

#### Plugin Not Loading

- Verify `DEBUG_PLUGINS` path in `appsettings.dev.json`
- Ensure plugin is built before starting BTCPay Server
- Check BTCPay Server logs for plugin loading errors

#### RPC Connection Errors

- Verify monerod is running: `docker ps | grep monerod`
- Check connection URLs in `appsettings.dev.json`
- Test RPC endpoint: `curl http://127.0.0.1:18081/json_rpc`

#### Database Issues

- Reset database: `docker compose down -v && docker compose up -d`
- Check PostgreSQL logs: `docker logs postgres`

## Payment Flow

### Invoice Creation

1. User creates invoice in BTCPay Server
2. `MoneroLikePaymentMethodHandler` is invoked
3. Plugin generates Monero address for payment
4. Payment details are saved to database
5. Invoice displays Monero payment information

### Payment Monitoring

1. `MoneroListener` runs as background service
2. Polls `monero-wallet-rpc` for new transactions
3. Matches transactions to pending invoices
4. Updates payment status when funds received
5. Monitors confirmations until threshold met

### Payment Confirmation

1. Transaction detected in mempool (0 confirmations)
2. Transaction included in block (1+ confirmations)
3. Sufficient confirmations reached (invoice marked paid)
4. BTCPay Server triggers webhooks/notifications

### Address Management

- Each invoice gets a unique subaddress
- Addresses are derived from a single master wallet
- Subaddress indices are tracked per store

## RPC Communication

### JSON-RPC Protocol

The plugin uses JSON-RPC 2.0 to communicate with Monero services.

#### Request Format

```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "method": "method_name",
  "params": {
    "param1": "value1"
  }
}
```

#### Response Format

```json
{
  "jsonrpc": "2.0",
  "id": "0",
  "result": {
    "data": "value"
  }
}
```

### MoneroRPCProvider

The `MoneroRPCProvider` class wraps RPC communication:

```csharp
public class MoneroRPCProvider
{
    // Daemon RPC (blockchain queries)
    public Task<GetHeightResponse> GetHeight();
    public Task<GetInfoResponse> GetInfo();
    
    // Wallet RPC (wallet operations)
    public Task<CreateWalletResponse> CreateWallet(string filename, string password);
    public Task<OpenWalletResponse> OpenWallet(string filename, string password);
    public Task<GetBalanceResponse> GetBalance(uint accountIndex);
    public Task<CreateAddressResponse> CreateAddress(uint accountIndex);
    public Task<GetTransfersResponse> GetTransfers(GetTransfersRequest request);
}
```

### Adding New RPC Methods

1. Define request/response models in `RPC/Models/`
2. Add method to `MoneroRPCProvider`
3. Use appropriate client (daemon or wallet)
4. Handle errors and timeouts

Example:

```csharp
public async Task<MyResponse> MyMethod(MyRequest request)
{
    try
    {
        return await SendRPCRequest<MyRequest, MyResponse>(
            "my_method", request, WalletClient);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to call my_method");
        throw;
    }
}
```

## Building and Packaging

### Build Configuration

The project supports two build configurations:
- **Debug**: For development, includes debug symbols
- **Release**: For production, optimized

### Build Plugin

```bash
# Debug build
dotnet build btcpay-monero-plugin.sln

# Release build
dotnet build btcpay-monero-plugin.sln -c Release
```

### Package as NuGet

```bash
dotnet pack Plugins/Monero/BTCPayServer.Plugins.Monero.csproj \
  -c Release \
  /p:PackageVersion=1.0.0 \
  -o nuget-packages
```

### Deterministic Build

The project uses deterministic builds for reproducibility:

```bash
# Validate deterministic build
dotnet tool install --global dotnet-validate --version 0.0.1-preview.537
dotnet validate package local nuget-packages/BTCPayServer.Plugins.Monero.1.0.0.nupkg
```

### Build Output

- Debug: `Plugins/Monero/bin/Debug/net8.0/`
- Release: `Plugins/Monero/bin/Release/net8.0/`
- NuGet: `nuget-packages/`

## Deployment

### Plugin Installation

Users can install the plugin in BTCPay Server:

1. Navigate to **Server Settings** > **Plugins**
2. Click **Install** next to BTCPay Server Monero Plugin
3. Restart BTCPay Server
4. Configure Monero daemon settings

### Docker Deployment

For BTCPay Server Docker deployments:

1. Configure environment variables:
   ```bash
   export BTCPAY_XMR_DAEMON_URI=http://monerod:18081
   export BTCPAY_XMR_WALLET_DAEMON_URI=http://xmr_wallet:18082
   export BTCPAY_XMR_WALLET_DAEMON_WALLETDIR=/wallet
   ```

2. Enable Monero in docker-compose:
   ```bash
   export BTCPAYGEN_CRYPTO2="xmr"
   . btcpay-setup.sh -i
   ```

3. Update deployment:
   ```bash
   btcpay-update.sh
   ```

### Manual Deployment

For manual deployments, copy the plugin DLL to BTCPay Server's plugins directory and configure environment variables.

## Troubleshooting

### Common Issues

#### Submodule Not Initialized

**Error**: Build fails with missing BTCPay Server references

**Solution**:
```bash
git submodule update --init --recursive
```

#### Plugin Not Detected

**Error**: Plugin doesn't appear in BTCPay Server

**Solution**:
- Check `DEBUG_PLUGINS` path
- Rebuild plugin
- Restart BTCPay Server
- Verify BTCPay Server version compatibility

#### RPC Connection Failure

**Error**: Cannot connect to monerod or monero-wallet-rpc

**Solution**:
- Check Docker containers: `docker ps`
- Verify environment variables
- Check firewall settings
- Review container logs: `docker logs monerod`

#### Database Migration Errors

**Error**: Database schema mismatch

**Solution**:
- Reset development database: `docker compose down -v`
- Run migrations manually
- Check BTCPay Server version compatibility

#### Test Failures

**Error**: Integration tests fail

**Solution**:
- Clean build: `dotnet clean && dotnet build`
- Reset test environment: `docker compose down -v`
- Check Docker resources (memory, disk space)
- Review test logs

### Getting Help

- **GitHub Issues**: Report bugs or request features
- **Matrix Chat**: Real-time help in `#btcpay-monero:matrix.org`
- **BTCPay Server Docs**: https://docs.btcpayserver.org
- **Monero RPC Docs**: https://getmonero.dev

## Contributing

### Contribution Process

1. **Fork the repository** on GitHub
2. **Create a feature branch** from `master`
3. **Make your changes** following coding standards
4. **Add tests** for new functionality
5. **Run all tests** to ensure nothing breaks
6. **Format your code** using `dotnet format`
7. **Commit with clear messages** describing the changes
8. **Push to your fork** and create a pull request
9. **Respond to review feedback** promptly

### Pull Request Guidelines

- **Title**: Clear, concise description of changes
- **Description**: Explain what, why, and how
- **Tests**: Include unit and/or integration tests
- **Documentation**: Update docs if needed
- **Code quality**: Passes all checks (format, build, tests)

### Commit Message Format

```
Short summary (50 chars or less)

More detailed explanation if needed. Wrap at 72 characters.

- Bullet points are okay
- Use present tense: "Add feature" not "Added feature"
- Reference issues: Fixes #123
```

### Code Review

All pull requests require:
- Code review by maintainer
- Passing CI/CD checks
- No merge conflicts

### Security

Report security vulnerabilities privately to maintainers. See [SECURITY.md](SECURITY.md).

## Additional Resources

- [BTCPay Server Documentation](https://docs.btcpayserver.org)
- [BTCPay Server Plugin Development](https://docs.btcpayserver.org/Development/Plugins/)
- [Monero RPC Documentation](https://getmonero.dev/interacting/monero-wallet-rpc.html)
- [Monero Developer Guide](https://github.com/monero-project/monero)
- [.NET 8 Documentation](https://docs.microsoft.com/en-us/dotnet/)

## License

This project is licensed under the MIT License. See [LICENSE.md](LICENSE.md) for details.
