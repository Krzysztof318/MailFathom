// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Exports;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One export of a mailbox: what was asked for, where it stands, and where its archive is kept.</summary>
/// <remarks>
/// <para>
/// The row outlives the job that writes the archive, which is why it exists at all: a job is claimed, retried, and
/// eventually forgotten, while an archive has to be findable by an operator holding nothing but the export's identity
/// for as long as the retention period allows.
/// </para>
/// <para>
/// The locator is this deployment's own key for the archive object and never reaches a caller. It is cleared when the
/// archive goes, so a row pointing at an object and an object nothing points at are the same two states here as
/// everywhere else content is stored.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailboxExportEntity
{
    /// <summary>The greatest length the folder path column holds, which is the local hierarchy's own bound on a path.</summary>
    internal const int MaximumFolderPathLength = 1024;

    /// <summary>The greatest length an object key reaches, which matches every other locator column in this schema.</summary>
    internal const int MaximumObjectLocatorLength = 512;

    public Guid Id { get; set; }

    /// <summary>Gets or sets the account's generated identifier, which every read is scoped by.</summary>
    public required string MailboxAccountId { get; set; }

    /// <summary>Gets or sets the one folder the export covers, or <see langword="null" /> for the whole mailbox.</summary>
    public string? FolderPath { get; set; }

    public MailboxExportState State { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    public long MessageCount { get; set; }

    public long ByteCount { get; set; }

    /// <summary>Gets or sets how large the finished archive is, absent while there is none.</summary>
    public long? ArchiveByteLength { get; set; }

    /// <summary>Gets or sets the content store's key for the archive, absent once there is nothing to serve.</summary>
    public string? ObjectLocator { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Gets or sets when the archive is deleted if nobody deletes it first, absent until one exists.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Gets or sets the stable code of what stopped a failed export, absent for every other state.</summary>
    public int? FailureCode { get; set; }

    public MailboxAccountEntity? MailboxAccount { get; set; }

    /// <summary>Gets or sets the optimistic concurrency token, which is PostgreSQL's own <c>xmin</c> rather than a column.</summary>
    /// <remarks>
    /// An operator cancelling and the writing job completing reach this row at the same moment. Every write states the
    /// state it expects to find, and the token is what makes the losing one a conflict rather than a silent overwrite.
    /// </remarks>
    public uint ConcurrencyVersion { get; set; }
}
