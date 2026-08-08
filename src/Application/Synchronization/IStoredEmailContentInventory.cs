// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Synchronization;

/// <summary>Reports how much local content storage holds and which occurrences it is still missing.</summary>
/// <remarks>
/// <para>
/// Both questions belong to one port because one caller asks them for one decision: a run reads how much room is left
/// before it stores anything, and reads the gaps so it can close them once there is room again. Splitting them would
/// leave two ports over the same table answering halves of the same question.
/// </para>
/// <para>
/// The port is deliberately read-only. Closing a gap is an ordinary store of a discovered occurrence, so it goes
/// through <see cref="IEmailMetadataRepository" /> and <see cref="EmailContent.Storage.IEmailContentStore" /> exactly
/// as a first discovery does, which is what keeps one write path for content rather than two that could drift.
/// </para>
/// </remarks>
public interface IStoredEmailContentInventory
{
    /// <summary>Gets how many bytes of local storage the stored raw MIME currently occupies.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The bytes local content storage occupies.</returns>
    /// <remarks>
    /// It is the quantity a storage ceiling is about — what the mail content costs the operator's disk — rather than
    /// the sum of the payload lengths, which understates it by whatever the storage layer spends on overhead. An
    /// implementation therefore reports physical occupancy where it can, and the two differ enough that a caller must
    /// not treat this as an arithmetic total it can reconcile against its own byte counters.
    /// </remarks>
    Task<long> GetStoredContentBytesAsync(CancellationToken cancellationToken);

    /// <summary>Gets the occurrences of one folder that are recorded without their content and are waiting for room.</summary>
    /// <param name="accountId">The account owning the folder.</param>
    /// <param name="folderResolutionId">The alias binding whose occurrences are read.</param>
    /// <param name="uidValidity">The UID space the caller's open session is selected under.</param>
    /// <param name="maxEmailCount">The greatest number of occurrences to report, oldest UID first.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>What each waiting occurrence was stored with, which is what a fetch of it has to be recorded under.</returns>
    /// <remarks>
    /// Only occurrences of the supplied UID space are reported. A folder the server recreated is a different UID
    /// space whose stored occurrences name emails the current one says nothing about, so fetching one by its recorded
    /// UID would retrieve a different message.
    /// </remarks>
    Task<IReadOnlyList<RemoteEmailMetadata>> GetEmailsAwaitingContentAsync(
        MailAccountId accountId,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        int maxEmailCount,
        CancellationToken cancellationToken);
}
