# NuGet publication

## Verified 0.1.1 release

**[Lunixi.Sdk 0.1.1 is published on NuGet](https://www.nuget.org/packages/Lunixi.Sdk/0.1.1)**. The [manual OIDC release workflow](https://github.com/Lunixi-Payment/lunixi-dotnet/actions/runs/41228274829) completed successfully on 31 October 2026. The public 79,012-byte package passed ZIP CRC checks and contains the expected package ID/version and repository commit `793887b5778137d52a1426a7493961f9ab0c7d22`. Its SHA-512 matches the [public NuGet catalog](https://api.nuget.org/v3/catalog0/data/2026.10.31.18.16.08/lunixi.sdk.0.1.1.json).

```bash
dotnet add package Lunixi.Sdk --version 0.1.1 --source https://api.nuget.org/v3/index.json
```

Downloaded package SHA-256: `91b39eb2e2ff2752dafcf2174a246adcad26a7042d4e49c97e2e359e796ee776`.

## Maintainer release workflow

The manual `publish-nuget.yml` workflow requires completed successful public push CI for the exact reviewed immutable 0.1.1 source, then packs and verifies the package ID, version and checksum before requesting a short-lived NuGet credential. The public CI check is unauthenticated, bounded and fails closed if the proof is unavailable; it adds no token permission. It does not rerun the functional suite. No long-lived API key or repository secret is needed.

The package-specific Trusted Publishing policy used for this release has the following scope:

| Setting | Value |
| --- | --- |
| Owner | The confirmed NuGet account or its already-authorized organization |
| Provider | GitHub Actions |
| Repository owner / repository | `Lunixi-Payment` / `lunixi-dotnet` |
| Workflow filename | `publish-nuget.yml` |
| Environment | Blank |
| Scope | Push new packages and package versions |
| Exact package subject | `Lunixi.Sdk` |

The account owner authorized this policy for the named workflow and exact package. Any later policy change remains an account-owner decision. Do not add wildcard subjects or unlist permissions. A first package can use a policy when the confirmed account has the required owner permissions; inspect the actual registry UI rather than creating a placeholder package.

The verified release ran from `main` with `nuget_user=lunixi`, the policy creator's public NuGet username, rather than an email or organization name. The artifact builder has read-only repository permissions; only the separate publisher requests an OIDC identity. The ephemeral key is masked and confined to the push step environment.

For future releases, verify the exact package version and downloaded contents after NuGet validation/indexing completes. A source tag or locally built nupkg alone does not prove registry publication; the 0.1.1 release above was verified from the public feed.

- [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
- [Official NuGet login action](https://github.com/NuGet/login)

The workflow is pinned to the verified public [v0.1.1 source](https://github.com/Lunixi-Payment/lunixi-dotnet/tree/v0.1.1), commit `793887b5778137d52a1426a7493961f9ab0c7d22`, and records that exact commit in the package repository metadata. This source adds merchant-page rendering in the ASP.NET Core sample. The subsequent OIDC publication and public package verification are recorded above. Source, workflow and registry verification remain separate release checks.
