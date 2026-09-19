// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.Evaluations.Corpus;

namespace MailFathom.Evaluations.RelevanceFilter;

/// <summary>One passage of the corpus, and whether it answers the lookup it is labelled against.</summary>
/// <param name="MessagePosition">Where the message falls in delivery order, from zero.</param>
/// <param name="Evidence">A phrase the labelled passage carries, which is what picks it out of its message.</param>
/// <param name="Answers">Whether the passage answers the lookup.</param>
internal sealed record LabelledCandidate(int MessagePosition, string Evidence, bool Answers)
{
    /// <summary>Reads the passage out of the corpus, as retrieval would hand it to the filter.</summary>
    /// <returns>The first passage of the message carrying the evidence.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the phrase, when no passage of the message carries it.</exception>
    /// <remarks>
    /// The identity is derived from the message's position, so the same message is the same stored email wherever it is
    /// labelled. Nothing about the sender is established, which is what a judgement is shown for mail nobody verified.
    /// </remarks>
    public EmailKnowledgePassage Resolve()
    {
        var message = CorpusMessage.At(this.MessagePosition);

        var passage = message.Passages.FirstOrDefault(passage => passage.Text.Contains(this.Evidence, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"No passage of message {this.MessagePosition} carries \"{this.Evidence}\", so the label names nothing.");

        return new EmailKnowledgePassage
        {
            StoredEmailId = StoredEmailId.Create(new Guid(this.MessagePosition + 1, 0, 0, new byte[8])),
            AccountId = MailAccountId.Create("evaluation"),
            FolderAlias = MailFolderAlias.Create("INBOX"),
            Subject = message.Subject,
            ReceivedAt = message.ReceivedAt,
            SenderVerification = SenderVerification.NotEstablished,
            MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
            Text = passage.Text,
        };
    }
}
