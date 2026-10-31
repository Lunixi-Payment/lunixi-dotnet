# Security Policy

Please do not report vulnerabilities in public issues, pull requests or discussions.

Email **developer@lunixi.com** with subject `Security: Lunixi .NET SDK` and include the SDK/runtime version, impact, prerequisites and a minimal reproduction. Do not include live API keys, private keys, webhook secrets, tokens or customer data. Use synthetic or revocable TEST credentials.

Use the latest release. Fixes are issued as new versions and recorded in [CHANGELOG.md](CHANGELOG.md).

Keep signing credentials server-side, disable request/header/body logging, preserve raw webhook bytes and commit event deduplication with domain changes. A successful browser return never authorizes fulfillment. See the [README](README.md) for timeout, idempotency and replay-storage requirements.
