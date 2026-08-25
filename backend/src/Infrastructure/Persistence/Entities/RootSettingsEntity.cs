// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>The deployment's persisted configuration: one row, holding one sparse settings document.</summary>
/// <remarks>
/// <para>
/// This is the root settings layer the host composes between its own files and the operator's overrides. The document
/// is sparse by design: a key it does not carry is inherited from the source beneath it rather than read as an empty
/// value, which is what lets one persisted setting change without restating everything a deployment provisioned.
/// </para>
/// <para>
/// The relational envelope is the whole of what this type declares, for the reason the owner record's is. The
/// singleton key, the version, and the two instants are what identity, concurrency, and update metadata are decided
/// by; the document beside them is ordinary .NET configuration and is opaque here.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class RootSettingsEntity
{
    /// <summary>The only identifier this table ever holds, which is what makes the row a singleton.</summary>
    public const int SingletonId = 1;

    /// <summary>The singleton key, always <see cref="SingletonId" />.</summary>
    public int Id { get; set; }

    /// <summary>The persisted settings, as one sparse <c>jsonb</c> document.</summary>
    /// <remarks>
    /// Held as text because nothing here reads into it: the document is flattened into ordinary colon-delimited
    /// configuration keys by the host's configuration provider, and this table exists to give that provider one
    /// document rather than to interpret one.
    /// </remarks>
    public required string Document { get; set; }

    /// <summary>The version a write is accepted against, so two writers cannot both commit over one document.</summary>
    public long Version { get; set; }

    /// <summary>When the row was provisioned, which is when the deployment first applied this schema.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the document last changed, which is the provisioning instant until one does.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
