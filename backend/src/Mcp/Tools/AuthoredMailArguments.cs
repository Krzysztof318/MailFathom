// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Submission;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;

namespace MailFathom.Mcp.Tools;

/// <summary>Reads the arguments the tools that author mail, the tools that ask about a send, and the tools that anchor a request to one stored email share.</summary>
/// <remarks>
/// <para>
/// Three tools send and two write a draft, and each of them takes a list of addresses in the same shape and refuses the
/// same values, as the three that send take an idempotency key. Reading them once is what keeps the five from drifting
/// into five answers to one question: a key a fourth character longer, a blank address admitted by one tool and refused
/// by the next, a recipient ceiling applied after the list was expanded rather than before it. The same holds of the
/// identifiers: an email named for a read, for a flag change, or for an answer is the same identifier, so it is read
/// here rather than at each tool that takes one.
/// </para>
/// <para>
/// Everything here is checked in front of the use case rather than instead of it. The domain bounds the same key where
/// its column is bounded and the composition parses the same addresses where a message is built, so what these
/// refusals buy is a caller meeting a statement about the argument it sent rather than an argument failure naming a
/// parameter it never wrote.
/// </para>
/// </remarks>
internal static class AuthoredMailArguments
{
    /// <summary>The greatest length text naming an email may carry before anything tries to read an identity out of it.</summary>
    /// <remarks>The bound and the reason are <c>get_email_content</c>'s: the longest form <see cref="Guid.TryParse(string, out Guid)" /> accepts is 68 characters, and the parse scans whatever it is handed.</remarks>
    private const int MaximumIdentifierLength = 68;

    /// <summary>Reads the identity of the stored email an answer is anchored to.</summary>
    /// <param name="storedEmailId">The text the caller named the email by.</param>
    /// <returns>The email identity.</returns>
    /// <remarks>
    /// <para>
    /// The refusal is the malformed-identifier one rather than the answer a missing email gets, and the two are
    /// deliberately different: this one says the request never named an email at all, which is true whatever this
    /// deployment holds, while an email that was named and cannot be answered is answered identically whether it is
    /// absent, withheld, or unreadable. The empty UUID is refused here with everything else, because it is what a
    /// client sends when it holds no identifier.
    /// </para>
    /// <para>
    /// The refused text is deliberately absent from the failure. It is the caller's own input on its way into a
    /// client-readable result and the log line beside it, and an identifier a caller invented says nothing an operator
    /// needs that the code does not already say. Which entry of a list was refused is absent for the same reason it is
    /// unnecessary: the caller holds the list it sent.
    /// </para>
    /// <para>
    /// Every tool that anchors a request to one stored email reads it here — the read, the flag change, and the three
    /// that author an answer — so what a well-formed identifier is cannot be one thing at one tool and another at the
    /// next.
    /// </para>
    /// </remarks>
    /// <exception cref="StoredEmailIdentifierMalformedException">Thrown when the text is not an identifier this system issues.</exception>
    public static StoredEmailId AnsweredEmail(string storedEmailId)
    {
        if (storedEmailId is null
            || storedEmailId.Length > MaximumIdentifierLength
            || !Guid.TryParse(storedEmailId, out var parsed)
            || parsed == Guid.Empty)
        {
            throw new StoredEmailIdentifierMalformedException();
        }

        return StoredEmailId.Create(parsed);
    }

    /// <summary>Reads the identity of the send a caller is asking about.</summary>
    /// <param name="outgoingEmailId">The text the caller named the queued message by.</param>
    /// <returns>The record identity.</returns>
    /// <exception cref="QueuedSendRefusedException">Thrown when the text is not an identifier this system issues.</exception>
    /// <remarks>
    /// The bound and the parse are the stored email's, for the same reason: the longest form
    /// <see cref="Guid.TryParse(string, out Guid)" /> accepts is 68 characters and the parse scans whatever it is
    /// handed. What differs is the refusal, because the two identify different things and a caller told its send
    /// identifier is not an email identifier would be looking for the wrong mistake.
    /// </remarks>
    public static OutgoingEmailId QueuedSend(string outgoingEmailId)
    {
        if (outgoingEmailId is null
            || outgoingEmailId.Length > MaximumIdentifierLength
            || !Guid.TryParse(outgoingEmailId, out var parsed)
            || parsed == Guid.Empty)
        {
            throw QueuedSendRefusedException.IdentifierMalformed();
        }

        return OutgoingEmailId.Create(parsed);
    }

    /// <summary>Reads the identity of the draft a caller is naming.</summary>
    /// <param name="draftId">The text the caller named the draft by.</param>
    /// <returns>The draft identity.</returns>
    /// <remarks>
    /// Text that is not an identifier at all is answered as a draft this deployment does not hold, which is the answer
    /// a draft of another account, a draft already given up, and a draft nobody ever wrote all get. That is deliberate
    /// where it is deliberate for those three: telling them apart is how a caller would learn which drafts exist by
    /// asking about them, and a draft carries no published identifier scheme a caller could be told it violated. The
    /// bound and the parse are the stored email's, because the longest form <see cref="Guid.TryParse(string, out Guid)" />
    /// accepts is 68 characters and the parse scans whatever it is handed.
    /// </remarks>
    /// <exception cref="MailDraftRefusedException">Thrown when the text is not an identifier this system issues.</exception>
    public static MailDraftId HeldDraft(string draftId)
    {
        if (draftId is null
            || draftId.Length > MaximumIdentifierLength
            || !Guid.TryParse(draftId, out var parsed)
            || parsed == Guid.Empty)
        {
            throw MailDraftRefusedException.NotFound();
        }

        return MailDraftId.Create(parsed);
    }

    /// <summary>Names the invocation asking, from the key the caller supplied for it.</summary>
    /// <param name="idempotencyKey">The caller's own identity for this send.</param>
    /// <returns>The requester the record is written under.</returns>
    /// <exception cref="MailSubmissionRefusedException">Thrown when the key is not one a record can be written under.</exception>
    public static OutgoingEmailRequester Requester(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)
            || idempotencyKey.Length > OutgoingEmailRequester.MaximumIdentityLength
            || idempotencyKey.Any(char.IsControl))
        {
            throw MailSubmissionRefusedException.IdempotencyKeyUnusable();
        }

        return OutgoingEmailRequester.Command(idempotencyKey);
    }

    /// <summary>Reports whether text could name an account at all, before anything looks for one it names.</summary>
    /// <param name="account">The text the caller named the account by.</param>
    /// <returns><see langword="true" /> when an account could be named by that text.</returns>
    /// <remarks>
    /// Whether an account answers to the text is the use case's question, asked against the accounts this deployment
    /// serves. What is answered here is whether the text could name one at all, so a caller that sent nothing meets a
    /// statement about its own argument rather than a refusal implying the account exists somewhere. Each caller
    /// raises its own refusal, because a send and a draft say different words about the same mistake.
    /// </remarks>
    public static bool CouldNameAnAccount(string? account) =>
        !string.IsNullOrWhiteSpace(account)
        && account.Length <= MailAccountSelector.MaximumLength
        && !account.Any(char.IsControl);
}
