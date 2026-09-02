// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>What one person set about their own client: one row per owner, holding one sparse preferences document.</summary>
/// <remarks>
/// <para>
/// It hangs off the owner row and holds a document beside a relational envelope, which is the arrangement the owner
/// record and the deployment's own settings row already use. Being keyed onto that row is what makes erasing an owner
/// take their preferences with everything else derived from them, without an erasure naming this table.
/// </para>
/// <para>
/// It is a table of its own rather than a second column on <c>settings_accounts</c> because what it holds is not
/// configuration: nothing binds it against a deployment's files, nothing about it decides which mailboxes are read,
/// and a person whose mail accounts an administrator still maintains writes here freely.
/// </para>
/// <para>
/// The envelope carries no version, and that is the contract rather than an omission. A write is last-write-wins,
/// because the only writers are one person's own devices and there is nobody a lost update could belong to.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class ClientPreferencesEntity
{
    /// <summary>The owner whose preferences these are, which is the key and the foreign key at once.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>The preferences, as one sparse <c>jsonb</c> document.</summary>
    /// <remarks>
    /// Held as text because nothing here reads into it. A key the document does not carry reads as that preference's
    /// default rather than as an empty value, which is what lets a build that publishes one more preference read a
    /// document written before it existed.
    /// </remarks>
    public required string Document { get; set; }

    /// <summary>When the person first set anything about their client.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When they last changed it, which is the first instant until they do.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
