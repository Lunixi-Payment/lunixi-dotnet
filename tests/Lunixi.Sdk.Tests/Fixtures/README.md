# Fixture provenance

- `webhook-v2-vectors.json` is copied unchanged from Lunixi's existing Node SDK fixture and is shared with the PHP interoperability checks. Secrets in this file are synthetic test values.
- `signing-vector.json` was generated with Node.js `crypto.createPrivateKey`, `crypto.sign` and `JSON.stringify`. The private key is deliberately synthetic: an Ed25519 seed consisting of 32 bytes whose value is `0x07`, encoded as PKCS#8. It is not a merchant credential and must never be used outside tests.

The signing fixture checks property insertion/numeric-index order, escaped versus raw Unicode, slash handling, finite fractional numbers, the safe-integer boundary, SHA-256 digest bytes, Ed25519 signature and derived public key. Tests verify the fixture with NSec/libsodium and never contact a gateway.
- `physical-pos-parity.json` is copied byte-for-byte from the Node SDK (`node/test/fixtures/physical-pos-parity.json`; the same file is used by the PHP, Java and Python SDKs). It carries the physical POS card-data key rule and cases, the card-number value scan, the `Idempotency-Key` and path-segment rules, the closed request-field and filter lists, the "not started" codes and the webhook event, original-payment, cashier and category cases. All values are synthetic.
