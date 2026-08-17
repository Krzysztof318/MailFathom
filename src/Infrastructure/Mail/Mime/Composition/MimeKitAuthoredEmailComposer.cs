// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers;
using System.Text;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MimeKit;

namespace MailFathom.Infrastructure.Mail.Mime.Composition;

/// <summary>Builds the MIME one authored message is transmitted as, and refuses everything that must not become one.</summary>
/// <remarks>
/// <para>
/// This is the only place in MailFathom where a message is assembled rather than parsed, so it is where every decision
/// about an outgoing message is made once: who it is from, what identity it carries, when it was written, which
/// recipients the envelope offers, and what is refused before a connection is worth opening. Callers hand over what an
/// author wrote and receive bytes or a refusal; no MIME type crosses back.
/// </para>
/// <para>
/// The order of the checks is deliberate and is the cheapest-first order that still refuses the most dangerous thing
/// early. Header injection is checked before anything is parsed, because a value carrying a line break is a value the
/// rest of the composition must never handle; the recipients follow, because a message addressed to nobody usable has
/// nothing to compose; and the size of the assembled message is last, because it is the only bound that cannot be known
/// before the message exists.
/// </para>
/// </remarks>
/// <param name="senderIdentities">Resolves who the sending account writes as, which no caller supplies.</param>
/// <param name="bounds">What this deployment is willing to compose.</param>
/// <param name="timeProvider">Supplies the <c>Date</c> header, as it supplies every timestamp in this system.</param>
internal sealed class MimeKitAuthoredEmailComposer(
    IOutgoingSenderIdentityReader senderIdentities,
    OutgoingEmailBounds bounds,
    TimeProvider timeProvider) : IAuthoredEmailComposer
{
    /// <summary>The number of headers one mailbox can be named in, which is the most a resolution can reduce away.</summary>
    /// <remarks>
    /// Resolution drops a repeated mention rather than a person, so the resolved count is never the authored one. It
    /// can only fall this far, though: naming somebody in all three headers is the whole of what redundancy means here,
    /// and a further mention of an address already named in the same header says nothing at all. That is what lets the
    /// authored list be measured before it is parsed — the deployment's number times this one is the largest list any
    /// acceptable message could have been written as.
    /// </remarks>
    private const int AddressableHeaderCount = 3;

    /// <summary>The two characters that end a header, and which therefore may not appear inside one.</summary>
    /// <remarks>
    /// A folded continuation needs no member of its own: folding is a line break followed by whitespace, so refusing
    /// the break refuses the fold with it.
    /// </remarks>
    private static readonly SearchValues<char> LineBreakCharacters = SearchValues.Create(['\r', '\n']);

    /// <inheritdoc />
    public AuthoredEmailComposition Compose(
        MailAccountId accountId,
        OutgoingEmailRequester requester,
        AuthoredEmail authored,
        MailDeliveryCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(authored);
        ArgumentNullException.ThrowIfNull(capabilities);

        if (senderIdentities.FindSenderIdentity(accountId) is not { } sender)
        {
            return AuthoredEmailComposition.Refused(
                AuthoredEmailRefusalReason.SenderUnconfigured,
                AuthoredEmailField.Sender);
        }

        if (RefuseInternationalizedSender(sender, capabilities) is { } senderRefusal)
        {
            return senderRefusal;
        }

        if (RefuseInjectedHeaders(authored) is { } injectionRefusal)
        {
            return injectionRefusal;
        }

        if (RefuseBodiesNobodyWrote(authored) is { } emptyBodyRefusal)
        {
            return emptyBodyRefusal;
        }

        if (this.RefuseBodiesBeyondBounds(authored) is { } bodyRefusal)
        {
            return bodyRefusal;
        }

        if (this.RefuseAttachmentsBeyondBounds(authored) is { } attachmentRefusal)
        {
            return attachmentRefusal;
        }

        if (this.ResolveRecipients(authored.Recipients, capabilities, out var placements) is { } recipientRefusal)
        {
            return recipientRefusal;
        }

        return this.Build(accountId, requester, authored, sender, placements, capabilities);
    }

    /// <summary>Refuses a sending address the submission server cannot carry.</summary>
    /// <remarks>
    /// The account's own address is checked beside the recipients rather than trusted, because a deployment may
    /// configure an internationalized mailbox and reach a relay that carries none. The refusal names the sender so the
    /// operator reads it as their configuration meeting that server, not as an author's mistake.
    /// </remarks>
    private static AuthoredEmailComposition? RefuseInternationalizedSender(
        OutgoingSenderIdentity sender,
        MailDeliveryCapabilities capabilities) =>
        CarriesInternationalizedAddresses(capabilities) || Ascii.IsValid(sender.Address.Address)
            ? null
            : AuthoredEmailComposition.Refused(
                AuthoredEmailRefusalReason.InternationalizationUnsupported,
                AuthoredEmailField.Sender);

    /// <summary>Reports whether the server can actually carry an address outside ASCII, which takes both of its answers.</summary>
    /// <remarks>
    /// An internationalized address is written as raw UTF-8 in the header block, so a server that advertised
    /// <c>SMTPUTF8</c> and no eight-bit support has not said it will accept one — there is no transfer encoding that
    /// makes an address seven-bit, unlike a subject or a body. RFC 6531 requires the two to be advertised together, so
    /// a server offering only the first is stating something it cannot honour, and composing for it would spend a
    /// transmission to be refused. Both are therefore asked, and the refusal is the same one either way.
    /// </remarks>
    private static bool CarriesInternationalizedAddresses(MailDeliveryCapabilities capabilities) =>
        capabilities.AcceptsInternationalizedAddresses && capabilities.AcceptsEightBitContent;

    /// <summary>Refuses every author-supplied value that would end its header early.</summary>
    /// <remarks>
    /// The bodies are deliberately absent: a line break in a body is what a body is made of, and a body is transmitted
    /// as content rather than as a header. Everything else an author writes becomes one — the subject, the name beside
    /// each address, and the name and media type of each file.
    /// </remarks>
    private static AuthoredEmailComposition? RefuseInjectedHeaders(AuthoredEmail authored)
    {
        if (CarriesLineBreak(authored.Subject))
        {
            return AuthoredEmailComposition.Refused(
                AuthoredEmailRefusalReason.HeaderInjected,
                AuthoredEmailField.Subject);
        }

        var injectedRecipient = authored.Recipients.FirstOrDefault(
            static recipient => CarriesLineBreak(recipient.Address) || CarriesLineBreak(recipient.DisplayName));
        if (injectedRecipient is not null)
        {
            return AuthoredEmailComposition.Refused(
                AuthoredEmailRefusalReason.HeaderInjected,
                FieldOf(injectedRecipient.Role));
        }

        return authored.Attachments.Any(
            static attachment => CarriesLineBreak(attachment.FileName) || CarriesLineBreak(attachment.MediaType))
            ? AuthoredEmailComposition.Refused(
                AuthoredEmailRefusalReason.HeaderInjected,
                AuthoredEmailField.Attachment)
            : null;
    }

    /// <summary>Reports whether a value carries a character that would end the header it is written into.</summary>
    private static bool CarriesLineBreak(string? value) =>
        value is not null && value.AsSpan().ContainsAny(LineBreakCharacters);

    /// <summary>Names the header a recipient of the given role is written in.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the role is not one this system declares.</exception>
    private static AuthoredEmailField FieldOf(OutgoingRecipientRole role) => role switch
    {
        OutgoingRecipientRole.To => AuthoredEmailField.To,
        OutgoingRecipientRole.Cc => AuthoredEmailField.Cc,
        OutgoingRecipientRole.Bcc => AuthoredEmailField.Bcc,
        _ => throw new ArgumentOutOfRangeException(
            nameof(role),
            role,
            "An outgoing recipient is named in one of the declared headers."),
    };

    /// <summary>Refuses a message whose text nobody wrote, and an alternative that would be offered empty.</summary>
    /// <remarks>
    /// The type requires a plain-text body, and a blank string satisfies the compiler rather than the requirement. What
    /// composing one would produce is the outcome the plain text exists to prevent, only worse: a recipient whose client
    /// prefers plain text is shown nothing at all instead of the message, and a body derived from the markup is exactly
    /// what this system refuses to invent for them. An alternative present but blank is the same failure read from the
    /// other side, so it is refused rather than offered to the clients that would choose it.
    /// </remarks>
    private static AuthoredEmailComposition? RefuseBodiesNobodyWrote(AuthoredEmail authored)
    {
        if (string.IsNullOrWhiteSpace(authored.PlainTextBody))
        {
            return AuthoredEmailComposition.Refused(
                AuthoredEmailRefusalReason.FieldUnusable,
                AuthoredEmailField.PlainTextBody);
        }

        return authored.HtmlBody is { } htmlBody && string.IsNullOrWhiteSpace(htmlBody)
            ? AuthoredEmailComposition.Refused(
                AuthoredEmailRefusalReason.FieldUnusable,
                AuthoredEmailField.HtmlBody)
            : null;
    }

    /// <summary>Refuses a body larger than this deployment composes.</summary>
    private AuthoredEmailComposition? RefuseBodiesBeyondBounds(AuthoredEmail authored)
    {
        if (authored.PlainTextBody.Length > bounds.MaxBodyCharacters)
        {
            return AuthoredEmailComposition.Refused(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.PlainTextBody,
                bounds.MaxBodyCharacters);
        }

        return authored.HtmlBody is { } htmlBody && htmlBody.Length > bounds.MaxBodyCharacters
            ? AuthoredEmailComposition.Refused(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.HtmlBody,
                bounds.MaxBodyCharacters)
            : null;
    }

    /// <summary>Refuses more files, or a larger one, than this deployment composes, and a file declared as no media type.</summary>
    private AuthoredEmailComposition? RefuseAttachmentsBeyondBounds(AuthoredEmail authored)
    {
        if (authored.Attachments.Count > bounds.MaxAttachmentCount)
        {
            return AuthoredEmailComposition.Refused(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.Attachment,
                bounds.MaxAttachmentCount);
        }

        if (authored.Attachments.Any(attachment => attachment.Content.Length > bounds.MaxAttachmentBytes))
        {
            return AuthoredEmailComposition.Refused(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.Attachment,
                bounds.MaxAttachmentBytes);
        }

        // The whole-message bound is measured on what the message became, which means assembling every part and
        // transfer-encoding it first. Content already past that bound before any of it happens is refused here instead,
        // because encoding only ever makes the octets more numerous, never fewer.
        if (authored.Attachments.Sum(static attachment => (long)attachment.Content.Length) > bounds.MaxMessageBytes)
        {
            return AuthoredEmailComposition.Refused(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.Message,
                bounds.MaxMessageBytes);
        }

        return authored.Attachments.Any(static attachment =>
            string.IsNullOrWhiteSpace(attachment.FileName) || !ContentType.TryParse(attachment.MediaType, out _))
            ? AuthoredEmailComposition.Refused(
                AuthoredEmailRefusalReason.FieldUnusable,
                AuthoredEmailField.Attachment)
            : null;
    }

    /// <summary>Parses every authored recipient into the mailbox the envelope will offer, or refuses naming the header.</summary>
    /// <remarks>
    /// <para>
    /// One mailbox is offered once, so a person an author named in two headers is placed in the first of them and the
    /// later mention is dropped. The order is the header order — <c>To</c>, then <c>Cc</c>, then <c>Bcc</c> — because
    /// that is the order of decreasing visibility: somebody named both as a primary recipient and as a blind one was
    /// meant to be seen, and the reverse choice would hide them from the other recipients against what the author
    /// wrote. Within one header the author's own order is kept.
    /// </para>
    /// <para>
    /// The count is measured after that resolution, because what it bounds is the number of people the server is asked
    /// to accept rather than the number of times they were written down. The authored list is measured against
    /// <see cref="AddressableHeaderCount"/> times that bound before any of it is parsed, so the parsing itself stays
    /// bounded by the deployment's own number rather than by whatever the author supplied.
    /// </para>
    /// </remarks>
    private AuthoredEmailComposition? ResolveRecipients(
        IReadOnlyList<AuthoredEmailRecipient> authoredRecipients,
        MailDeliveryCapabilities capabilities,
        out IReadOnlyList<PlacedRecipient> placements)
    {
        placements = [];

        if (authoredRecipients.Count > bounds.MaxRecipientCount * AddressableHeaderCount)
        {
            return AuthoredEmailComposition.Refused(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.Recipients,
                bounds.MaxRecipientCount);
        }

        var placed = new List<PlacedRecipient>(authoredRecipients.Count);
        var alreadyPlaced = new HashSet<EmailAddress>();

        foreach (var authoredRecipient in authoredRecipients.OrderBy(static recipient => recipient.Role))
        {
            var field = FieldOf(authoredRecipient.Role);

            if (!EmailAddress.TryCreate(authoredRecipient.DisplayName, authoredRecipient.Address, out var address)
                || address.Address.Length > OutgoingRecipient.MaximumAddressLength)
            {
                return AuthoredEmailComposition.Refused(AuthoredEmailRefusalReason.FieldUnusable, field);
            }

            if (!CarriesInternationalizedAddresses(capabilities) && !Ascii.IsValid(address.Address))
            {
                return AuthoredEmailComposition.Refused(
                    AuthoredEmailRefusalReason.InternationalizationUnsupported,
                    field);
            }

            if (alreadyPlaced.Add(address))
            {
                placed.Add(new PlacedRecipient(
                    OutgoingRecipient.Create(address, authoredRecipient.Role),
                    address.DisplayName));
            }
        }

        if (placed.Count == 0)
        {
            return AuthoredEmailComposition.Refused(
                AuthoredEmailRefusalReason.FieldUnusable,
                AuthoredEmailField.Recipients);
        }

        if (placed.Count > bounds.MaxRecipientCount)
        {
            return AuthoredEmailComposition.Refused(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.Recipients,
                bounds.MaxRecipientCount);
        }

        placements = placed;

        return null;
    }

    /// <summary>Assembles the message and measures what it became.</summary>
    private AuthoredEmailComposition Build(
        MailAccountId accountId,
        OutgoingEmailRequester requester,
        AuthoredEmail authored,
        OutgoingSenderIdentity sender,
        IReadOnlyList<PlacedRecipient> placements,
        MailDeliveryCapabilities capabilities)
    {
        var messageId = InternetMessageId.Mint(sender.Domain);

        using var message = new MimeMessage();

        message.From.Add(MailboxOf(sender.Address.DisplayName, sender.Address.Address));
        foreach (var placement in placements)
        {
            AddresseesOf(message, placement.Recipient.Role)
                .Add(MailboxOf(placement.DisplayName, placement.Recipient.Address.Address));
        }

        message.Subject = authored.Subject;
        message.Date = timeProvider.GetUtcNow();
        message.MessageId = messageId.Value;
        PlaceInThread(message, authored.Threading);
        message.Body = BuildBody(authored);

        var format = FormatOptions.Default.Clone();

        // Every address was refused above unless the server carries it, so the only question left is whether the
        // message needs the format at all: an all-ASCII message is written the way every server understands.
        format.International = !Ascii.IsValid(sender.Address.Address)
            || placements.Any(static placement => !Ascii.IsValid(placement.Recipient.Address.Address));

        // The bytes are stored and transmitted verbatim rather than re-serialized per attempt, so they carry the line
        // ending SMTP requires rather than the platform's.
        format.NewLineFormat = NewLineFormat.Dos;
        format.EnsureNewLine = true;

        // A blind recipient is offered exactly as any other is, and what makes them blind is that the transmitted
        // headers do not name them.
        format.HiddenHeaders.Add(HeaderId.Bcc);
        format.HiddenHeaders.Add(HeaderId.ResentBcc);

        message.Prepare(
            capabilities.AcceptsEightBitContent ? EncodingConstraint.EightBit : EncodingConstraint.SevenBit,
            format.MaxLineLength);

        using var composed = new MemoryStream();
        message.WriteTo(format, composed);

        if (composed.Length > bounds.MaxMessageBytes)
        {
            return AuthoredEmailComposition.Refused(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.Message,
                bounds.MaxMessageBytes);
        }

        if (!capabilities.PermitsMessageOfSize(composed.Length))
        {
            return AuthoredEmailComposition.Refused(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.Message,
                capabilities.MaxMessageBytes);
        }

        var request = OutgoingEmailRequest.Create(
            accountId,
            requester,
            [.. placements.Select(static placement => placement.Recipient)]);

        return AuthoredEmailComposition.Composed(new ComposedOutgoingEmail(request, messageId, composed.ToArray()));
    }

    /// <summary>Writes the two headers a receiving client threads the message by, for a message that answers another.</summary>
    /// <remarks>
    /// <para>
    /// Both headers are written or neither is, because they answer one question between them and a client reading only
    /// one of them is ordinary. A message answering nothing writes neither, which is what a mail parser reads as a
    /// conversation's first message rather than as a message whose ancestry is unknown.
    /// </para>
    /// <para>
    /// The identifiers were reduced to what a header can carry before they arrived, so nothing here repairs one. What
    /// this adds is the delimiters: the angle brackets belong to the header rather than to the identity, and are
    /// written by the mail library at the one place that knows the header's own syntax.
    /// </para>
    /// </remarks>
    private static void PlaceInThread(MimeMessage message, OutgoingThreadPlacement threading)
    {
        if (!threading.IsThreaded)
        {
            return;
        }

        message.InReplyTo = threading.InReplyTo;

        foreach (var ancestor in threading.References)
        {
            message.References.Add(ancestor);
        }
    }

    /// <summary>Builds the body an author wrote, as the plain text alone or as an alternative of both representations.</summary>
    /// <remarks>
    /// The plain text is the author's own and is never derived from the markup. A body produced by stripping tags is
    /// text nobody wrote, and every recipient whose client prefers plain text reads that instead of the message.
    /// </remarks>
    private static MimeEntity BuildBody(AuthoredEmail authored)
    {
        var body = new BodyBuilder { TextBody = authored.PlainTextBody, HtmlBody = authored.HtmlBody };

        foreach (var attachment in authored.Attachments)
        {
            body.Attachments.Add(
                attachment.FileName,
                attachment.Content.ToArray(),
                ContentType.Parse(attachment.MediaType));
        }

        return body.ToMessageBody();
    }

    /// <summary>Builds one addressee, letting the mail library encode a name outside ASCII as the standard requires.</summary>
    private static MailboxAddress MailboxOf(string? displayName, string address) =>
        new(Encoding.UTF8, displayName, address);

    /// <summary>Gets the header list a recipient of the given role is written into.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the role is not one this system declares.</exception>
    private static InternetAddressList AddresseesOf(MimeMessage message, OutgoingRecipientRole role) => role switch
    {
        OutgoingRecipientRole.To => message.To,
        OutgoingRecipientRole.Cc => message.Cc,
        OutgoingRecipientRole.Bcc => message.Bcc,
        _ => throw new ArgumentOutOfRangeException(
            nameof(role),
            role,
            "An outgoing recipient is named in one of the declared headers."),
    };

    /// <summary>Holds one recipient the envelope will offer, with the name the composed message writes beside them.</summary>
    /// <remarks>
    /// The two travel together only as far as this composition. The record keeps the address because a send cannot be
    /// resumed without it, and the name stays in the stored MIME.
    /// </remarks>
    private readonly record struct PlacedRecipient(OutgoingRecipient Recipient, string? DisplayName);
}
