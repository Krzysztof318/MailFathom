// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>One email an answer was drawn from, named so the reader can go and read it.</summary>
/// <param name="StoredEmailId">The stable local identity, which is the same one every other read names an email by.</param>
/// <param name="AccountId">The account whose mailbox it was read from.</param>
/// <param name="FolderAlias">The folder alias it was read from.</param>
/// <param name="Subject">The subject it carried, or <see langword="null" /> when it carried none.</param>
/// <param name="ReceivedAt">When the last receiving hop recorded it, or <see langword="null" /> when no header carried a usable date.</param>
/// <param name="SenderVerification">What was established about the author it displays, and what this deployment made of them.</param>
/// <param name="MachineAuthorship">How much the email's own text read as machine written.</param>
/// <remarks>
/// <para>
/// A citation is what turns an answer into a starting point rather than something to be believed: the identifier
/// resolves through the single-email read, so every claim can be checked against the message it came from.
/// </para>
/// <para>
/// It deliberately carries no extract. The passage the run retrieved is bounded mail content that has already reached a
/// provider, and republishing it here would put mail into a second response that nobody asked to read; the subject and
/// the received time are what let a reader recognize the message before fetching it.
/// </para>
/// <para>
/// One per email rather than one per passage. A run makes several lookups and one message can answer more than one of
/// them, and a caller reading a list of sources wants the messages rather than the number of times each was found.
/// </para>
/// <para>
/// It carries the sender verdict for the reason it carries the subject: an answer drawn from mail is worth exactly what
/// the mail behind it is worth, and a reader deciding whether to act on a claim needs to know whether the message it
/// came from had an author anybody established. The evidence behind that verdict stays with the single-email read, and
/// the authorship reading beside it travels on the same terms and for the same reason.
/// </para>
/// </remarks>
public sealed record MailAnswerCitation(
    StoredEmailId StoredEmailId,
    MailAccountId AccountId,
    MailFolderAlias FolderAlias,
    string? Subject,
    DateTimeOffset? ReceivedAt,
    SenderVerification SenderVerification,
    MachineAuthorshipAssessment MachineAuthorship);
