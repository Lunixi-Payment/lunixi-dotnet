# NuGet publication

The manual `publish-nuget.yml` workflow packs the reviewed immutable 0.1.0 source and verifies the package ID, version and checksum before requesting a short-lived NuGet credential. It does not rerun the functional suite. No long-lived API key or repository secret is needed.

Before running it, the confirmed NuGet account must create a Trusted Publishing policy:

| Setting | Value |
| --- | --- |
| Owner | The confirmed NuGet account or its already-authorized organization |
| Provider | GitHub Actions |
| Repository owner / repository | `Lunixi-Payment` / `lunixi-dotnet` |
| Workflow filename | `publish-nuget.yml` |
| Environment | Blank |
| Scope | Push new packages and package versions |
| Exact package subject | `Lunixi.Sdk` |

This policy grants publishing access to that workflow; have the account owner review and authorize it. Do not add wildcard subjects or unlist permissions. A first package can use a policy when the confirmed account has the required owner permissions; inspect the actual registry UI rather than creating a placeholder package.

Run the workflow from `main` with `nuget_user` equal to the individual policy creator's public NuGet username, not their email or organization name. The artifact builder has read-only repository permissions; only the separate publisher requests an OIDC identity. The ephemeral key is masked and confined to the push step environment.

Verify the exact 0.1.0 version in NuGet after package validation/indexing completes. A source tag or locally built nupkg alone does not prove registry publication.

- [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
- [Official NuGet login action](https://github.com/NuGet/login)
