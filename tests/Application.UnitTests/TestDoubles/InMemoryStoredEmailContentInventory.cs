// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Stands in for the local content store's occupancy and the gaps a run is expected to close.</summary>
/// <remarks>
/// The occupancy is a value a test sets rather than one derived from what it stored, because the production reader
/// reports physical storage that no arithmetic over payload lengths reproduces. What a test drives here is the answer
/// the run reads, which is the whole of what the run's behavior depends on.
/// </remarks>
internal sealed class InMemoryStoredEmailContentInventory : IStoredEmailContentInventory
{
    private readonly List<EmailAwaitingContent> awaitingContent = [];

    /// <summary>Gets or sets how much local storage the stored content is reported to occupy.</summary>
    public long StoredContentBytes { get; set; }

    /// <summary>Gets how many times a run asked how much room is left.</summary>
    public int StoredContentReadCount { get; private set; }

    /// <summary>Records that one occurrence is stored without its content and is waiting for room.</summary>
    /// <param name="metadata">What the deferring run stored the occurrence from.</param>
    /// <param name="isFiledCopy">Whether the stored row is joined to an outgoing send this deployment filed a copy of.</param>
    public void AddAwaitingContent(RemoteEmailMetadata metadata, bool isFiledCopy = false) =>
        this.awaitingContent.Add(new EmailAwaitingContent(metadata, isFiledCopy));

    /// <inheritdoc />
    public Task<long> GetStoredContentBytesAsync(CancellationToken cancellationToken)
    {
        this.StoredContentReadCount++;

        return Task.FromResult(this.StoredContentBytes);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EmailAwaitingContent>> GetEmailsAwaitingContentAsync(
        MailAccountId accountId,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        int maxEmailCount,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<EmailAwaitingContent> awaiting =
        [
            .. this.awaitingContent
                .Where(candidate => candidate.Metadata.OccurrenceId.AccountId == accountId
                    && candidate.Metadata.OccurrenceId.FolderResolutionId == folderResolutionId
                    && candidate.Metadata.OccurrenceId.UidValidity == uidValidity)
                .OrderBy(candidate => candidate.Metadata.OccurrenceId.Uid.Value)
                .Take(maxEmailCount),
        ];

        return Task.FromResult(awaiting);
    }
}
