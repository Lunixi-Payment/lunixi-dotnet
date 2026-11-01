# Changelog

## 1.0.0 — 2026-11-01

- Reject explicit null capture/refund amounts; full remaining operations use omission or the dedicated API described in MIGRATION-1.0.md.

- Breaking: public money request/response fields require canonical minor-unit strings; JSON numbers are rejected.
- Exact signed 64-bit fiat arithmetic and currency-aware decimal text conversion preserve values above JavaScript’s safe-integer limit.
- See [MIGRATION-1.0.md](MIGRATION-1.0.md) before upgrading.


## 0.1.1 - 2026-10-31

- Provide a concrete merchant-site renderer/example using the existing official Lunixi checkout form by default, with an explicit optional hosted presentation.
- Use only the validated backend paymentPageUrl for redirects; its opaque token is independent of the embedded token. Missing URL keeps embedded available and requires reconciliation for hosted mode without inventing another key.
- Preserve authentication, customer ownership/CSRF integration boundaries, no-store/token-redaction rules and raw response compatibility. Existing published tags are unchanged.

## 0.1.0 — 2026-10-31

- Initial .NET 8 SDK with Ed25519 PKCS#8 signing, gateway-compatible compact JSON, token caching and origin-constrained HTTP transport.
- Validated hosted intents, payment status/list/capture/refund/void/methods, split DTOs/actions and fraud clients.
- Raw-body v2 webhook verification, rotated signatures, timestamp and envelope binding, durable replay-store interface.
- ASP.NET Core example with actual paymentId/token response binding, configured merchant validation, authenticated status reconciliation, complete capture/refund balance checks, SQLite attempt persistence and atomic webhook-inbox/order commits.
- Offline cryptographic, transport, validation and host integration tests; NuGet packaging and CI.

This source version does not claim NuGet.org availability or activation of gateway features whose deployment is pending.
