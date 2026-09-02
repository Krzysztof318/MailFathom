// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Retrieval;

/// <summary>One bounded extract of mail, with the identity and the coordinates an answer is traced back through.</summary>
/// <remarks>
/// <para>
/// This is the only shape mail content travels in on its way to a model. It carries an extract rather than a message:
/// the bound is applied before the passage exists, so nothing downstream can widen it, and a retrieval that matched a
/// long thread hands over what was relevant rather than the thread.
/// </para>
/// <para>
/// The identity travels with the text because an answer that cannot say where a claim came from cannot be checked. The
/// stored identifier is the same one every other read names an email by, so a reader given a passage can fetch the whole
/// message; the account and the folder alias are the deployment's own names for where it was read from.
/// </para>
/// <para>
/// A passage is mail content and inherits the classification of the message it was cut from. It is never logged, never
/// attached to a span, and never exported.
/// </para>
/// </remarks>
public sealed record EmailKnowledgePassage
{
    /// <summary>Gets the stable local identity of the email the extract came from.</summary>
    public required StoredEmailId StoredEmailId { get; init; }

    /// <summary>Gets the account whose mailbox the email was read from.</summary>
    public required MailAccountId AccountId { get; init; }

    /// <summary>Gets the folder alias the email was read from.</summary>
    public required MailFolderAlias FolderAlias { get; init; }

    /// <summary>Gets the subject the email carried, or <see langword="null" /> when it carried none.</summary>
    public string? Subject { get; init; }

    /// <summary>Gets when the last receiving hop recorded the message, or <see langword="null" /> when no header carried a usable date.</summary>
    public DateTimeOffset? ReceivedAt { get; init; }

    /// <summary>Gets what was established about the author of the message the extract came from.</summary>
    /// <remarks>
    /// It travels with the passage so that a citation can state it without a second read of the message. Nothing in the
    /// retrieval path acts on it and nothing puts it in front of a model: what a provider receives is the extract, and
    /// this reaches the caller instead, as part of saying where a claim came from.
    /// </remarks>
    public required SenderVerification SenderVerification { get; init; }

    /// <summary>Gets how much the message's own text read as machine written.</summary>
    /// <remarks>
    /// It travels with the passage for the reason the sender verdict does, and is put to the same use: a citation states
    /// it without a second read of the message, and nothing in the retrieval path acts on it or puts it in front of a
    /// model.
    /// </remarks>
    public required MachineAuthorshipAssessment MachineAuthorship { get; init; }

    /// <summary>Gets the extract itself, already cut to the size one passage may carry.</summary>
    public required string Text { get; init; }
}
