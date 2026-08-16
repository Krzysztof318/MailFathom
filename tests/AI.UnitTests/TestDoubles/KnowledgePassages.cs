// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.AI.UnitTests.TestDoubles;

/// <summary>Builds the passages a retrieval hands over, so a test states only what it is about.</summary>
internal static class KnowledgePassages
{
    /// <summary>Builds one passage.</summary>
    /// <param name="text">The extract itself.</param>
    /// <param name="storedEmailId">The stable identity, or <see langword="null" /> to generate one.</param>
    /// <param name="accountId">The account the message belongs to.</param>
    /// <param name="folderAlias">The folder alias the message belongs to.</param>
    /// <param name="subject">The subject, or <see langword="null" /> for a message that carried none.</param>
    /// <returns>The passage.</returns>
    public static EmailKnowledgePassage Create(
        string text,
        Guid? storedEmailId = null,
        string accountId = "primary",
        string folderAlias = "INBOX",
        string? subject = null) => new()
        {
            StoredEmailId = StoredEmailId.Create(storedEmailId ?? Guid.CreateVersion7()),
            AccountId = MailAccountId.Create(accountId),
            FolderAlias = MailFolderAlias.Create(folderAlias),
            Subject = subject,
            ReceivedAt = null,
            SenderVerification = SenderVerification.NotEstablished,
            Text = text,
        };
}
