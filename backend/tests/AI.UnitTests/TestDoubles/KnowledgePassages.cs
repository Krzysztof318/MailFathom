// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
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
    /// <param name="senderVerification">
    /// What was established about the message's author, or <see langword="null" /> for the stored default. A test about
    /// what reaches a model states it, because the guarantee there is that the verdict reaches no provider — which a
    /// passage carrying the default could not tell apart from one whose verdict was dropped.
    /// </param>
    /// <param name="machineAuthorship">
    /// How much the message's own text read as machine written, or <see langword="null" /> for the stored default. It is
    /// stated for the reason the verdict above is: the reading reaches no provider either, and a passage carrying the
    /// default could not tell that guarantee from a reading that was dropped.
    /// </param>
    /// <returns>The passage.</returns>
    public static EmailKnowledgePassage Create(
        string text,
        Guid? storedEmailId = null,
        string accountId = "primary",
        string folderAlias = "INBOX",
        string? subject = null,
        SenderVerification? senderVerification = null,
        MachineAuthorshipAssessment? machineAuthorship = null) => new()
        {
            StoredEmailId = StoredEmailId.Create(storedEmailId ?? Guid.CreateVersion7()),
            AccountId = MailAccountId.Create(accountId),
            FolderAlias = MailFolderAlias.Create(folderAlias),
            Subject = subject,
            ReceivedAt = null,
            SenderVerification = senderVerification ?? SenderVerification.NotEstablished,
            MachineAuthorship = machineAuthorship ?? MachineAuthorshipAssessment.NotAssessed,
            Text = text,
        };
}
