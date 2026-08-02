// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.UnitTests;

/// <summary>Builds the read models a mailbox listing returns, so a test states only what it is about.</summary>
/// <remarks>
/// Every default here is deliberately uninteresting: one account, one folder, no attachments, flags nobody has
/// observed. A test that cares about a value passes it and a reader of that test sees the filter under test rather than
/// twenty properties of arrangement.
/// </remarks>
internal static class SyntheticEmailSummaries
{
    /// <summary>The account every summary belongs to unless a test names another.</summary>
    public const string DefaultAccountId = "primary";

    /// <summary>The folder alias every summary belongs to unless a test names another.</summary>
    public const string DefaultFolderAlias = "INBOX";

    /// <summary>Builds one summary.</summary>
    /// <param name="receivedAt">When the message was received, or <see langword="null" /> for undated mail.</param>
    /// <param name="storedEmailId">The stable identity, or <see langword="null" /> to generate one.</param>
    /// <param name="accountId">The account the message belongs to.</param>
    /// <param name="folderAlias">The folder alias the message belongs to.</param>
    /// <param name="subject">The subject, or <see langword="null" /> for a message that carried none.</param>
    /// <param name="senderAddress">The sender's address as the message wrote it.</param>
    /// <param name="toAddresses">The comparison forms of the <c>To</c> addresses.</param>
    /// <param name="isRemotelySeen">Whether the last observed flag snapshot reported <c>\Seen</c>.</param>
    /// <param name="remoteFlagsObservedAt">When the flags were observed, or <see langword="null" /> when they never were.</param>
    /// <param name="attachmentCount">How many attachments the classification counted.</param>
    /// <param name="inlineResourceCount">How many inline resources the classification counted.</param>
    /// <returns>The summary.</returns>
    public static EmailSummary Create(
        DateTimeOffset? receivedAt = null,
        Guid? storedEmailId = null,
        string accountId = DefaultAccountId,
        string folderAlias = DefaultFolderAlias,
        string? subject = null,
        string? senderAddress = null,
        IReadOnlyList<string>? toAddresses = null,
        bool isRemotelySeen = false,
        DateTimeOffset? remoteFlagsObservedAt = null,
        int attachmentCount = 0,
        int inlineResourceCount = 0) => new()
        {
            StoredEmailId = StoredEmailId.Create(storedEmailId ?? Guid.CreateVersion7()),
            AccountId = MailAccountId.Create(accountId),
            FolderAlias = MailFolderAlias.Create(folderAlias),
            InternetMessageId = null,
            Subject = subject,
            SentAt = receivedAt,
            ReceivedAt = receivedAt,
            SizeOctets = 2048,
            SenderDisplayName = null,
            SenderAddress = senderAddress,
            ToAddresses = toAddresses ?? [],
            Attachments = new StoredEmailAttachmentSummary(
                attachmentCount,
                TotalSizeOctets: attachmentCount * 1024L,
                inlineResourceCount,
                IsEncrypted: false,
                CarriesUnverifiedSignature: false,
                ContainsUnexpandedTnefPart: false),
            ContentAvailability = StoredEmailContentAvailability.Available,
            RemoteFlags = new RemoteEmailFlagSnapshot(
                remoteFlagsObservedAt,
                isRemotelySeen,
                IsAnswered: false,
                IsFlagged: false,
                IsDraft: false,
                IsDeleted: false),
        };

    /// <summary>Builds a run of summaries one day apart, oldest first, so a test can page over a known order.</summary>
    /// <param name="count">How many summaries to build.</param>
    /// <param name="firstReceivedAt">When the first of them was received.</param>
    /// <returns>The summaries, in ascending received order.</returns>
    public static IReadOnlyList<EmailSummary> CreateDailyRun(int count, DateTimeOffset firstReceivedAt) =>
    [
        .. Enumerable.Range(0, count).Select(dayOffset => Create(firstReceivedAt.AddDays(dayOffset))),
    ];
}
