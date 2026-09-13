// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One binary file a user supplied, which a record links to rather than holding the octets itself.</summary>
/// <remarks>
/// <para>
/// It hangs off the user row, so erasing a user takes their files with everything else derived from them. Its octets
/// are held the way a mail payload's are: in <see cref="Content" /> when the database holds them, or in the object the
/// <see cref="ObjectLocator" /> names, and <see cref="Backend" /> says which. The check constraint the configuration
/// declares is the same one every content table carries, so the move and the release treat a file as they treat mail.
/// </para>
/// <para>
/// The row carries no version: a file is written once and removed, never rewritten, so replacing what a record shows
/// is a new file and a new link rather than an update of this row.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class StoredFileEntity
{
    /// <summary>The longest media type a file is recorded under.</summary>
    public const int MaximumMediaTypeLength = 255;

    /// <summary>The identifier the deployment minted when the file was written.</summary>
    public Guid Id { get; set; }

    /// <summary>The user the file belongs to.</summary>
    public Guid UserId { get; set; }

    /// <summary>The media type the file is served under.</summary>
    public required string MediaType { get; set; }

    /// <summary>How many octets the file is.</summary>
    public long ByteLength { get; set; }

    /// <summary>The SHA-256 digest of the octets.</summary>
    public required byte[] Sha256Hash { get; set; }

    /// <summary>Which store holds the octets.</summary>
    public ContentStorageBackend Backend { get; set; }

    /// <summary>The octets, when the database holds them or still holds the copy a move left behind.</summary>
    public byte[]? Content { get; set; }

    /// <summary>The whole key of the object holding the octets, when the object backend holds them.</summary>
    public string? ObjectLocator { get; set; }

    /// <summary>When a move verified the object, which is what a release of the database copy is measured from.</summary>
    public DateTimeOffset? ObjectVerifiedAt { get; set; }

    /// <summary>When the file was written, which is what the sweep's age floor is measured from.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
