# Documentation Index

Welcome to the BTCPay Server Monero Plugin documentation!

## Quick Links

- [Main README](../README.md) - User-facing documentation and quick start
- [DEVELOPMENT.md](../DEVELOPMENT.md) - **Start here if you're a developer!**

## Developer Documentation

### Core Documentation

1. **[DEVELOPMENT.md](../DEVELOPMENT.md)**
   - Complete development guide
   - Setup instructions
   - Development workflow
   - Testing strategies
   - Code formatting and standards
   - Debugging tips
   - Contributing guidelines

2. **[ARCHITECTURE.md](ARCHITECTURE.md)**
   - System architecture overview
   - Component diagrams
   - Data flow
   - Service architecture
   - Plugin lifecycle
   - Database schema
   - Security considerations
   - Performance considerations

3. **[RPC.md](RPC.md)**
   - Monero RPC integration
   - JSON-RPC protocol details
   - Daemon RPC methods
   - Wallet RPC methods
   - Error handling
   - RPC models reference
   - Testing RPC calls
   - Common issues and solutions

4. **[PAYMENT_FLOW.md](PAYMENT_FLOW.md)**
   - Complete payment lifecycle
   - Invoice creation process
   - Payment address generation
   - Payment monitoring
   - Payment detection and matching
   - Confirmation tracking
   - Payment completion
   - Edge cases handling
   - Sequence diagrams

## Getting Started

### For New Developers

1. Read [DEVELOPMENT.md](../DEVELOPMENT.md) first for setup instructions
2. Review [ARCHITECTURE.md](ARCHITECTURE.md) to understand the system
3. Check [PAYMENT_FLOW.md](PAYMENT_FLOW.md) to understand payment processing
4. Reference [RPC.md](RPC.md) when working with Monero RPC

### For Contributors

1. Follow the setup in [DEVELOPMENT.md](../DEVELOPMENT.md)
2. Read the Contributing section in [DEVELOPMENT.md](../DEVELOPMENT.md)
3. Check existing issues and pull requests on GitHub
4. Join the Matrix chat: [#btcpay-monero:matrix.org](https://matrix.to/#/#btcpay-monero:matrix.org)

### For Reviewers

1. Understand the [ARCHITECTURE.md](ARCHITECTURE.md)
2. Review [PAYMENT_FLOW.md](PAYMENT_FLOW.md) for payment logic
3. Check [RPC.md](RPC.md) for RPC implementation details
4. Use the testing guide in [DEVELOPMENT.md](../DEVELOPMENT.md)

## Documentation Structure

```
btcpayserver-monero-plugin/
├── README.md                    # User documentation
├── DEVELOPMENT.md              # Main developer guide
├── LICENSE.md                  # MIT License
├── CODE_OF_CONDUCT.md         # Community guidelines
├── SECURITY.md                # Security policies
└── docs/                      # Additional documentation
    ├── README.md              # This file
    ├── ARCHITECTURE.md        # System architecture
    ├── RPC.md                 # RPC documentation
    └── PAYMENT_FLOW.md        # Payment flow details
```

## Key Concepts

### Plugin Architecture

The plugin follows BTCPay Server's plugin architecture:
- Implements `BaseBTCPayServerPlugin`
- Registers services via dependency injection
- Extends BTCPay Server UI with custom views
- Integrates with BTCPay Server's payment system

### Monero Integration

The plugin integrates with:
- **monerod**: Blockchain access and daemon operations
- **monero-wallet-rpc**: Wallet management and transaction monitoring

### Payment Processing

Key components:
- **Payment Method Handler**: Creates payment details
- **MoneroListener**: Monitors blockchain for payments
- **RPC Provider**: Communicates with Monero services

## Common Tasks

### Development

- **Build**: `dotnet build btcpay-monero-plugin.sln`
- **Test**: `dotnet test BTCPayServer.Plugins.UnitTests`
- **Format**: `dotnet format btcpay-monero-plugin.sln --exclude submodules/*`
- **Integration Tests**: `docker compose -f BTCPayServer.Plugins.IntegrationTests/docker-compose.yml run tests`

### Debugging

- Set breakpoints in your IDE
- Run BTCPay Server in debug mode
- Check logs for errors
- Use RPC testing tools

### Contributing

1. Fork the repository
2. Create a feature branch
3. Make changes following coding standards
4. Add tests for new functionality
5. Run tests and ensure they pass
6. Format code
7. Create pull request

## Additional Resources

### External Documentation

- [BTCPay Server Documentation](https://docs.btcpayserver.org)
- [BTCPay Server Plugin Development](https://docs.btcpayserver.org/Development/Plugins/)
- [Monero RPC Documentation](https://getmonero.dev/interacting/monero-wallet-rpc.html)
- [Monero Daemon RPC](https://getmonero.dev/interacting/daemon-rpc.html)
- [Monero Developer Guide](https://github.com/monero-project/monero)

### Community

- **Matrix Chat**: [#btcpay-monero:matrix.org](https://matrix.to/#/#btcpay-monero:matrix.org)
- **GitHub Issues**: Report bugs and request features
- **GitHub Discussions**: Ask questions and share ideas

### Blog Posts

- [Accepting Monero via BTCPay Server](https://sethforprivacy.com/guides/accepting-monero-via-btcpay-server/) - Setup guide for users

## Contributing to Documentation

Found an issue with the documentation? Want to improve it?

1. Documentation is written in Markdown
2. Follow existing structure and style
3. Include code examples where appropriate
4. Add diagrams for complex flows (use ASCII art or Mermaid)
5. Keep explanations clear and concise
6. Submit a pull request

### Documentation Style Guide

- Use clear, simple language
- Include code examples
- Provide context and motivation
- Link to related documentation
- Keep technical accuracy high
- Update documentation when code changes

## License

This project is licensed under the MIT License. See [LICENSE.md](../LICENSE.md) for details.

## Security

For security concerns, please review [SECURITY.md](../SECURITY.md) and report vulnerabilities privately to maintainers.
