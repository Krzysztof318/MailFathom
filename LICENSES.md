# MailMcp third-party license register

This register records the license review for third-party software and services planned in the architecture draft. MailMcp must remain compatible with both contemplated distribution models:

1. commercial closed-source distribution; and
2. open-source publication of the MailMcp project.

This document is not legal advice. Before adding, upgrading, replacing, bundling, or distributing a third-party component, verify the current upstream license and update this file in the same change set.

## License acceptance policy

MailMcp may use third-party components only when their licenses permit both commercial use and open-source redistribution of MailMcp without forcing MailMcp itself to be distributed under a copyleft license.

Allowed licenses include permissive OSI-approved licenses such as MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, PostgreSQL License, and similarly permissive licenses after review.

Licenses that require review before use include LGPL, MPL, EPL, Unicode, custom commercial licenses, source-available licenses, dual licenses, preview license terms, and any license with field-of-use, redistribution, network-service, data-use, patent, trademark, telemetry, or attribution conditions that are not already understood for this project.

Prohibited without explicit owner approval are strong copyleft or source-available licenses that could conflict with closed-source commercial distribution or require relicensing MailMcp, including GPL, AGPL, SSPL, BUSL, Commons Clause, PolyForm Noncommercial, and similar restrictions.

## Planned dependency review

| Planned component | Planned use | License or terms observed during review | Compatibility decision | Source reviewed |
|---|---|---|---|---|
| .NET runtime, ASP.NET Core, Microsoft.Extensions libraries, EF Core, JWT bearer, Data Protection, JSON console logging | Runtime, host, framework, authentication middleware, configuration, and persistence APIs | MIT for the .NET platform packages in the dotnet repositories and NuGet package metadata | Allowed. Permissive license supports commercial closed-source use and open-source distribution with notices. | <https://github.com/dotnet/runtime>, <https://github.com/dotnet/aspnetcore>, <https://github.com/dotnet/efcore>, <https://www.nuget.org/packages/Microsoft.Extensions.AI/> |
| ModelContextProtocol.AspNetCore | MCP server SDK and Streamable HTTP transport | MIT according to the official C# SDK documentation; repository metadata has also shown Apache-2.0, so the exact package license must be rechecked when pinned | Allowed if the pinned NuGet package resolves to MIT or Apache-2.0. Update this row with the package license expression before adding the dependency. | <https://csharp.sdk.modelcontextprotocol.io/>, <https://github.com/modelcontextprotocol/csharp-sdk> |
| MailKit and MimeKit | IMAP, SMTP, MIME parsing, SASL, IDLE, NOTIFY, and TLS modes | MIT | Allowed. Permissive license supports both target distribution models with notice preservation. | <https://github.com/jstedfast/MailKit/blob/master/LICENSE>, <https://github.com/jstedfast/MailKit/blob/master/FAQ.md> |
| PostgreSQL | Database server for metadata, raw MIME content, full-text search, synchronization state, chunks, and embeddings | PostgreSQL License, described by PostgreSQL as liberal and similar to BSD or MIT; PostgreSQL FAQ states there is no fee even for commercial software products | Allowed. Permissive database license supports commercial and open-source deployment. Verify packaging obligations for any bundled server image or installer. | <https://www.postgresql.org/about/licence/>, <https://www.postgresql.org/about/press/faq/> |
| pgvector PostgreSQL extension | Vector search extension | PostgreSQL License in upstream release metadata | Allowed. Do not bundle a pgvector binary or container image until the exact release version and notices are recorded. | <https://github.com/pgvector/pgvector> |
| Npgsql.EntityFrameworkCore.PostgreSQL and Npgsql | PostgreSQL ADO.NET provider and EF Core provider | PostgreSQL-style permissive license in upstream project metadata and package sources | Allowed. Recheck NuGet license expression when the package is pinned centrally. | <https://github.com/npgsql/efcore.pg>, <https://github.com/npgsql/npgsql>, <https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL/> |
| Pgvector.EntityFrameworkCore | EF Core vector mapping | MIT in NuGet package metadata and upstream repository metadata | Allowed. Update this row with the pinned package version when introduced. | <https://github.com/pgvector/pgvector-dotnet>, <https://www.nuget.org/packages/Pgvector.EntityFrameworkCore/> |
| Microsoft.Agents.AI and related Microsoft Agent Framework packages | Agent and RAG orchestration, including ChatClientAgent and TextSearchProvider integration | MIT according to Microsoft Agent Framework public materials; NuGet package license must be rechecked at the pinned version | Allowed if the pinned package remains MIT or Apache-2.0. Preview or prerelease package terms require explicit review on every update. | <https://github.com/microsoft/agent-framework>, <https://devblogs.microsoft.com/agent-framework/microsoft-agent-framework-version-1-0/>, <https://www.nuget.org/packages/Microsoft.Agents.AI/> |
| Microsoft.Extensions.AI and Microsoft.Extensions.AI.Abstractions | Provider-neutral chat and embedding abstractions | MIT in NuGet package metadata | Allowed. Provider-specific packages remain separately reviewable dependencies. | <https://www.nuget.org/packages/Microsoft.Extensions.AI/>, <https://www.nuget.org/packages/Microsoft.Extensions.AI.Abstractions/>, <https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai> |
| Semantic Kernel | Optional adapter when Agent Framework lacks a required capability | MIT in upstream repository | Allowed only behind an adapter and only when a concrete missing capability justifies the dependency. | <https://github.com/microsoft/semantic-kernel> |
| OpenTelemetry .NET SDK and contrib packages | Traces, metrics, instrumentation, and exporters | Apache-2.0 | Allowed. Preserve required license and notice files, especially for exporters and contrib packages. | <https://github.com/open-telemetry/opentelemetry-dotnet>, <https://github.com/open-telemetry/opentelemetry-dotnet-contrib> |
| xUnit.net v3 | Unit testing framework | Apache-2.0 | Allowed for test projects. | <https://github.com/xunit/xunit>, <https://www.nuget.org/packages/xunit.v3> |
| NSubstitute | Test doubles for unit tests | BSD-style permissive license | Allowed for test projects. Verify exact NuGet license expression when pinned. | <https://github.com/nsubstitute/NSubstitute/blob/master/LICENSE.txt>, <https://github.com/nsubstitute/NSubstitute> |
| Auth0 service and Auth0 SDKs | Default external OAuth 2.1 identity provider choice and optional administration/API SDK use | Auth0 SDKs are MIT; the hosted Auth0 service is governed by commercial service terms, not an open-source software license | SDKs are allowed if MIT. Hosted-service use is not a redistributable software dependency, but production use must comply with Auth0 terms, pricing, data-processing terms, and trademark rules. | <https://github.com/auth0/auth0.net>, <https://github.com/auth0/auth0-aspnetcore-authentication>, <https://auth0.com/docs/libraries> |
| Chat and embedding providers | Deployment-selected AI providers for chat and embeddings | Not fixed in the architecture draft | Not yet approved. Each provider SDK, model license, service terms, data-use policy, retention policy, and commercial-use terms must be reviewed before it becomes a supported profile. | Architecture draft only; no provider selected. |
| MinIO or S3-compatible object storage | Future raw-content storage migration | Not included in the first release | Deferred. Review server, SDK, client, container image, and trademark terms before introducing the migration dependency. | Architecture draft states this dependency is not included initially. |

## Operational rules for future changes

- Keep package versions pinned centrally in `Directory.Packages.props`; do not use floating versions.
- Add or update a row in this register whenever a dependency, service, protocol SDK, container image, generated asset, or externally sourced code sample is introduced or upgraded.
- Record the exact package name, version, license expression, upstream URL, and any required attribution or NOTICE handling once a dependency is actually pinned.
- Do not merge dependencies with unknown, missing, custom, source-available, copyleft, or service-only terms until the project owner explicitly approves the risk.
- Treat model provider terms and cloud service terms as separate from SDK licenses. A permissive SDK license does not approve use of the hosted service or model output for MailMcp.
- Preserve third-party copyright notices and license files in source and binary distributions whenever the upstream license requires it.
- If MailMcp is published as open source, include this register and any generated third-party notices in the release artifacts.
- If MailMcp is distributed as closed-source commercial software, include the required third-party notices with binaries, installers, containers, and documentation.
