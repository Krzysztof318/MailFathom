// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>The picture one person is drawn by: one row per owner, holding the octets they supplied.</summary>
/// <remarks>
/// <para>
/// It hangs off the owner row, which is what makes erasing an owner take their portrait with everything else derived
/// from them, without an erasure naming this table. It sits beside the preferences document rather than inside one,
/// because a megabyte of image octets is not a small closed JSON document and a read of a switch should not carry one.
/// </para>
/// <para>
/// What it does not hold is what kind of image the octets are. That is read from the octets themselves wherever it is
/// needed, so a stored media type cannot come to disagree with what is stored under it — and the write already proved
/// the kind before the row existed.
/// </para>
/// <para>
/// The row carries no version, and that is the contract rather than an omission: a write is last-write-wins, because
/// the only writers are one person's own devices.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class OwnerPortraitEntity
{
    /// <summary>The owner whose portrait this is, which is the key and the foreign key at once.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>The octets the person supplied, unchanged.</summary>
    /// <remarks>Bounded before it reaches here by the transport, which refuses a body over the portrait's limit before a handler is entered.</remarks>
    public required byte[] Content { get; set; }

    /// <summary>When the person first supplied a portrait.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When they last replaced it, which is the first instant until they do.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
