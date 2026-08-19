// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Submission;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;

namespace MailFathom.Mcp.Tools;

/// <summary>Reads the arguments every tool that asks for mail to be sent shares, in the one place they are read.</summary>
/// <remarks>
/// <para>
/// Three tools send, and each of them takes an idempotency key and a list of addresses in the same shape and refuses
/// the same values. Reading them once is what keeps the three from drifting into three answers to one question: a key
/// a fourth character longer, a blank address admitted by one tool and refused by the next, a recipient ceiling
/// applied after the list was expanded rather than before it.
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
    /// The refusal is the malformed-identifier one rather than the answer a missing email gets, and the two are
    /// deliberately different: this one says the request never named an email at all, which is true whatever this
    /// deployment holds, while an email that was named and cannot be answered is answered identically whether it is
    /// absent, withheld, or unreadable. The empty UUID is refused here with everything else, because it is what a
    /// client sends when it holds no identifier.
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

    /// <summary>Collects the three headers into the one recipient list every author writes.</summary>
    /// <param name="to">The addresses named in the <c>To</c> header, or <see langword="null" /> where the act names none.</param>
    /// <param name="cc">The addresses named in the <c>Cc</c> header, or <see langword="null" />.</param>
    /// <param name="bcc">The addresses named in the <c>Bcc</c> header, or <see langword="null" />.</param>
    /// <returns>The recipients the author named, in the order the headers are read in.</returns>
    /// <remarks>
    /// The order is the order the headers are read in, which is the order the composition writes them in. Nothing is
    /// deduplicated or parsed here: whether text names a mailbox is the composition's question, and how many people a
    /// message may actually reach is the deployment's number, both asked once for every way a message is authored.
    /// What is answered here is only how long the caller's own lists are, because that is what decides whether they are
    /// expanded at all, and the check therefore belongs in front of the expansion rather than after it.
    /// </remarks>
    /// <exception cref="MailSubmissionRefusedException">Thrown when the three headers name more people than a record holds, or an entry carries no address.</exception>
    public static IReadOnlyList<NamedRecipient> NamedRecipients(
        IReadOnlyList<string>? to,
        IReadOnlyList<string>? cc,
        IReadOnlyList<string>? bcc)
    {
        var count = (to?.Count ?? 0) + (cc?.Count ?? 0) + (bcc?.Count ?? 0);

        if (count > OutgoingEmailRequest.MaximumRecipientCount)
        {
            throw MailSubmissionRefusedException.TooManyRecipients();
        }

        var named = new List<NamedRecipient>(count);

        AddNamed(named, to, OutgoingRecipientRole.To, AuthoredEmailField.To);
        AddNamed(named, cc, OutgoingRecipientRole.Cc, AuthoredEmailField.Cc);
        AddNamed(named, bcc, OutgoingRecipientRole.Bcc, AuthoredEmailField.Bcc);

        return named;
    }

    /// <summary>Adds one header's addresses, refusing an entry that names nobody at all.</summary>
    /// <remarks>
    /// Blank text is refused here because an authored recipient is built from an address and a blank one names nothing
    /// to build from — a defect in whoever called rather than an author's mistake, and this is the boundary that keeps
    /// it from becoming one. Everything else the text may be wrong about travels unparsed to the composition, which is
    /// the single place an address is read and refused.
    /// </remarks>
    /// <exception cref="MailSubmissionRefusedException">Thrown when an entry carries no address.</exception>
    private static void AddNamed(
        List<NamedRecipient> named,
        IReadOnlyList<string>? addresses,
        OutgoingRecipientRole role,
        AuthoredEmailField field)
    {
        if (addresses is null)
        {
            return;
        }

        foreach (var address in addresses)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw MailSubmissionRefusedException.From(
                    new AuthoredEmailRefusal(AuthoredEmailRefusalReason.FieldUnusable, field));
            }

            named.Add(NamedRecipient.AtAddress(role, address));
        }
    }
}
