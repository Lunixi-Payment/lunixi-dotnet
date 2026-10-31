# Contributing

This public repository is published from Lunixi's integration source. Report bugs or suggest changes here; maintainers apply accepted patches in the source repository and credit contributions in the changelog.

Use synthetic fixtures and a .NET 8 SDK. Run the build, tests, formatting and vulnerability commands in [README.md](README.md) before proposing a patch. Changes to financial DTOs, signing, retries or webhook handling need meaningful regression coverage against the documented public gateway contract. Do not invent routes or simulate successful payments.

Include the SDK/runtime version and a sanitized `RequestId` with bug reports. Never paste credentials, tokens, card data or buyer details. Report vulnerabilities privately using [SECURITY.md](SECURITY.md).

Documentation must explain financial outcome, required scopes, stable idempotency keys and webhook authority in clear English and Turkish. Preserve Lunixi's original logo and server-side credential boundary.
