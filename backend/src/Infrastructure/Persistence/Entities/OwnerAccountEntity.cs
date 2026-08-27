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
/// The relational envelope is deliberately the whole of what this type declares. The identifier, the label, the
/// version, the marker, and the two instants are what ownership, lookup, concurrency, and cascade are decided by, so
/// none of them depends on reading a document; the document beside them is the owner's configurable record and is
/// opaque here.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class OwnerAccountEntity
{
    /// <summary>The longest label an owner is told apart by, which is what a mail account's identifier is bounded at.</summary>
    public const int MaximumDisplayNameLength = 128;

    /// <summary>The stable owner identity every mail account of theirs points at.</summary>
    /// <remarks>
    /// A version 4 identifier rather than one of the version 7 values the rest of persistence mints, and the
    /// difference is deliberate rather than incidental. An owner identifier reaches administrative APIs, audit
    /// records, and logs, and a time-ordered one published there would say when each owner was provisioned and in what
    /// order relative to every other — which is a fact about people rather than about rows. Nothing reads these rows
    /// in identifier order, so the locality a version 7 value buys is worth nothing to pay for with that.
    /// </remarks>
    public Guid Id { get; set; }

    /// <summary>The label an operator tells this owner apart by, which is unique across the deployment.</summary>
    /// <remarks>
    /// A label rather than an identity: nothing resolves an owner by it, and every reference to an owner is the
    /// identifier above. What the uniqueness buys is a list of owners an administrator can read — two rows carrying
    /// one label would leave them choosing between owners with nothing to choose on — and it is exact rather than
    /// case-folded, because a comparison rule is owed by a value something resolves by and nothing resolves by this.
    /// </remarks>
    public required string DisplayName { get; set; }

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

    /// <summary>Whether anything has written this owner's document while the deployment was running.</summary>
    /// <remarks>
    /// An owner a deployment declares in configuration carries the envelope alone, so the mail graph's foreign key
    /// resolves while the document column stays the empty object it was provisioned with. An owner whose document was
    /// written and then emptied carries the same octets and is not the same fact: the first is a row waiting for the
    /// import that fills it, the second a record its owner emptied on purpose. This is what tells the two apart, so
    /// nothing has to read a deployment's files to find out which of them a row is.
    /// </remarks>
    public bool DocumentWrittenAtRuntime { get; set; }
}
