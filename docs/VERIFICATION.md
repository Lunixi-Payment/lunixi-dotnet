# Verification record — 0.1.0

Local verification performed on 2026-10-31, macOS arm64, .NET SDK 8.0.427/runtime 8.0.33. The library also compiled under SDK 10.0.403. These are local results; the configured Linux/Windows/macOS GitHub Actions matrix has not been executed by this record.

| Check | Result |
| --- | --- |
| Locked NuGet restore, all three projects | Passed |
| Release build, SDK + ASP.NET Core sample + tests | Passed, zero warnings/errors |
| xUnit offline suite | 67 passed, zero failures/skips |
| `dotnet format --verify-no-changes --no-restore`, all three projects | Passed |
| NuGet vulnerability query with transitive dependencies | No reported advisories in the configured NuGet.org data at verification time |
| `dotnet pack` | `artifacts/Lunixi.Sdk.0.1.0.nupkg` produced |
| Package contents | DLL, XML API docs, MIT license, original logos and EN/TR guides; no test fixtures, credentials or database |
| Real Kestrel startup | Health 200, anonymous checkout 401, SQLite schema initialized; no gateway call |
| Documentation route/amount checks | `npm run check` passed in the documentation worktree |

## Security behavior exercised

- Node.js `JSON.stringify` / Ed25519 cross-check: Unicode, line separators, unescaped slashes, numeric index keys, compact JSON, digest and PKCS#8 signatures.
- Shared cross-SDK webhook v2 vectors, rotated signatures, body/timestamp/event-id/type tampering, stale/future delivery, duplicated/missing headers and legacy-only signature rejection.
- Token caching under concurrency, expiry renewal, one bounded 401 refresh, malformed token expiry rejection and bodyless signed authentication.
- Same-body/key retry with a fresh nonce, no network retry for writes without a key, timeout/cancellation and ambiguous-outcome metadata.
- HTTPS/origin restriction, redirect rejection, traversal/absolute-route rejection, bounded/malformed responses, sanitized exception messages and secret serialization exclusion.
- Minor-unit bounds, basket/split totals, removed root amount fields, duplicate split cards/providers, method-field safety and pending-versus-failed split handling.
- Real ASP.NET Core host with server-priced orders, persisted attempt keys, authenticated checkout, untrusted browser return, configured merchant binding, locally bound payment lookup followed by authenticated GET, exact amount and complete captured/refunded balance matching, hold/refund states, stale-read protection, durable duplicate/conflict rejection and atomic SQLite inbox/order commit.

The suite uses synthetic keys and fixtures. No live charge, capture, refund or void was executed. Automated checks do not establish gateway feature rollout, merchant entitlements, registry publication or a complete independent penetration assessment. Product-specific generic JSON calls still require the documented DTO/scopes, and production merchants must implement their own fulfillment/reconciliation policy.

## Pre-publication review correction

The initial sample incorrectly expected create-intent `intentId` and trusted signed event settlement fields without an authenticated status read. Review corrected it to the actual `paymentId` + `token` response, rejects conflicting aliases/missing tokens, and requires `LUNIXI_MERCHANT_ID`. Twenty-five additional regressions cover the actual event `data.intentId`, unknown payment lookup, merchant/order/payment/currency/amount mismatches, missing or fractional balances, declined/pending/partial states, stale reads after refund, held authorization, string minor-unit balances and missing merchant configuration. All 67 tests above were rerun after the correction.

## 0.1.1 merchant-page presentation patch

The new ASP.NET Core form route and presentation helpers passed12targeted native TestServer/URL/HTML tests on.NET8, with no skips; formatting/build/pack passed. Tests cover authenticated default embedded HTML, backend-only optional redirect with an independent token, missing/unsafe URL rejection, no-store, same attempt key and invalid mode rejection before a financial request. The existing67-test baseline above is retained; the full base suite was not repeated for this example-only patch. The backend API token is not customer login; merchant ownership and native browser CSRF remain caller responsibilities. No live/full-store acceptance or registry publication is claimed.
