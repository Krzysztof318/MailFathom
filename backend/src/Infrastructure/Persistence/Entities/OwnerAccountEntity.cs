// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>The owner a deployment serves: one row per person, whichever mail accounts they own.</summary>
/// <remarks>
/// <para>
/// This is the owner axis the mail graph hangs on. A mail account names exactly one of these rows, and everything
/// derived from that account's mail reaches this row through the foreign keys beneath it, which is what makes erasing
/// an owner a delete rather than a question asked of every repository.
/// </para>
/// <para>
/// The relational envelope is deliberately the whole of what this type declares. The identifier, the version, and the
/// two instants are what ownership, lookup, concurrency, and cascade are decided by, so none of them depends on
/// reading a document; the document beside them is the owner's configurable record and is opaque here.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class OwnerAccountEntity
{
    /// <summary>The stable owner identity every mail account of theirs points at.</summary>
    public Guid Id { get; set; }

    /// <summary>The owner's configurable record, as one <c>jsonb</c> document.</summary>
    /// <remarks>
    /// Held as text because nothing here reads into it: what the document contains — the owner's mail-account
    /// declarations and their owner-level settings — is written and projected by the configuration layer, and this
    /// table exists to give that layer a row per owner rather than to interpret one.
    /// </remarks>
    public required string Document { get; set; }

    /// <summary>The version a write is accepted against, so two writers cannot both commit over one document.</summary>
    public long Version { get; set; }

    /// <summary>When the owner record was provisioned.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the document last changed, which is the provisioning instant until one does.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
