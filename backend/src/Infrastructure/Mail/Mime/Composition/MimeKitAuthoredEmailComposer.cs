// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers;
using System.Text;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
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
/// <para>
/// A send and a draft run through that same sequence, which is the point of composing both here. The one thing that
/// differs is whether a message addressed to nobody is refused: a draft is written before its author has decided who
/// reads it, and a send is not. Everything else — the sending address, the minted identity, the injection refusals, and
/// every bound — is one answer, so a draft that is promoted is a message this deployment had already agreed to compose.
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
    /// and a further mention of an address already named in the same header adds nobody to the envelope, however it
    /// narrows what the placement records about how the address came to be there. That is what lets the
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

        return AsOutgoing(
            accountId,
            requester,
            this.ComposeMessage(accountId, authored, capabilities, requireRecipients: true));
    }

    /// <inheritdoc />
    public MailDraftComposition ComposeDraft(
        MailAccountId accountId,
        AuthoredEmail authored,
        MailDeliveryCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(authored);
        ArgumentNullException.ThrowIfNull(capabilities);

        return this.ComposeMessage(accountId, authored, capabilities, requireRecipients: false);
    }

    /// <summary>Runs the whole composition, from the refusals that need nothing built to the bytes that were built.</summary>
    /// <param name="accountId">The account the message is composed as.</param>
    /// <param name="authored">What somebody wrote.</param>
    /// <param name="capabilities">What the servers involved are known to support.</param>
    /// <param name="requireRecipients">Whether a message addressed to nobody is refused, which a send is and a draft is not.</param>
    private MailDraftComposition ComposeMessage(
        MailAccountId accountId,
        AuthoredEmail authored,
        MailDeliveryCapabilities capabilities,
        bool requireRecipients)
    {
        if (senderIdentities.FindSenderIdentity(accountId) is not { } sender)
        {
            return Refused(AuthoredEmailRefusalReason.SenderUnconfigured, AuthoredEmailField.Sender);
        }

        if (RefuseInternationalizedSender(sender, capabilities) is { } senderRefusal)
        {
            return MailDraftComposition.Refused(senderRefusal);
        }

        if (RefuseInjectedHeaders(authored) is { } injectionRefusal)
        {
            return MailDraftComposition.Refused(injectionRefusal);
        }

        if (RefuseBodiesNobodyWrote(authored) is { } emptyBodyRefusal)
        {
            return MailDraftComposition.Refused(emptyBodyRefusal);
        }

        if (this.RefuseBodiesBeyondBounds(authored) is { } bodyRefusal)
        {
            return MailDraftComposition.Refused(bodyRefusal);
        }

        if (this.RefuseAttachmentsBeyondBounds(authored) is { } attachmentRefusal)
        {
            return MailDraftComposition.Refused(attachmentRefusal);
        }

        var recipientRefusal = this.ResolveRecipients(
            authored.Recipients,
            capabilities,
            requireRecipients,
            out var placements);

        return recipientRefusal is { } refusal
            ? MailDraftComposition.Refused(refusal)
            : this.Build(authored, sender, placements, capabilities);
    }

    /// <inheritdoc />
    public AuthoredEmailComposition RecomposeAsOccurrence(
        MailAccountId accountId,
        OutgoingEmailRequester requester,
        IReadOnlyList<OutgoingRecipient> recipients,
        ReadOnlyMemory<byte> draftMime,
        MailDeliveryCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(recipients);
        ArgumentNullException.ThrowIfNull(capabilities);

        if (draftMime.IsEmpty)
        {
            throw new ArgumentException("A recurring send's occasion is composed from the stored draft.", nameof(draftMime));
        }

        if (senderIdentities.FindSenderIdentity(accountId) is not { } sender)
        {
            return AuthoredEmailComposition.Refused(
                AuthoredEmailRefusalReason.SenderUnconfigured,
                AuthoredEmailField.Sender);
        }

        if (RefuseInternationalizedSender(sender, capabilities) is { } senderRefusal)
        {
            return AuthoredEmailComposition.Refused(senderRefusal);
        }

        using var draft = new MemoryStream(draftMime.ToArray(), writable: false);

        MimeMessage message;
        try
        {
            message = MimeMessage.Load(draft);
        }
        catch (FormatException)
        {
            // The draft is this system's own composition rather than anything a remote server sent, so a draft that no
            // longer parses is a stored payload that was damaged. The occasion is refused rather than repaired: a
            // partial message sent to somebody is worse than an occasion an operator is told about.
            return AuthoredEmailComposition.Refused(
                AuthoredEmailRefusalReason.FieldUnusable,
                AuthoredEmailField.Message);
        }

        using (message)
        {
            var messageId = InternetMessageId.Mint(sender.Domain);

            // Replaced rather than kept, because a message is sent as the account that sends it and the address that
            // account writes as may have been reconfigured since the declaration was made.
            message.From.Clear();
            message.From.Add(MailboxOf(sender.Address.DisplayName, sender.Address.Address));

            // The two headers that make this occasion a message of its own. A draft transmitted unchanged every week
            // would carry one identity for every occasion, which threads a year of them as one message in a recipient's
            // client and reads to a server as the same message arriving again.
            message.Date = timeProvider.GetUtcNow();
            message.MessageId = messageId.Value;

            // The provenance is the strict default and nothing reads it here: an occasion is composed as a send, whose
            // request keeps the addresses alone. What governs a recurring declaration is the declaration's own
            // question rather than this one.
            return AsOutgoing(
                accountId,
                requester,
                this.Serialize(
                    message,
                    messageId,
                    sender,
                    [.. recipients.Select(static recipient => new MailDraftRecipient(
                        recipient,
                        AuthoredRecipientProvenance.NamedByCaller))],
                    capabilities));
        }
    }

    /// <summary>Refuses a sending address the submission server cannot carry.</summary>
    /// <remarks>
    /// The account's own address is checked beside the recipients rather than trusted, because a deployment may
    /// configure an internationalized mailbox and reach a relay that carries none. The refusal names the sender so the
    /// operator reads it as their configuration meeting that server, not as an author's mistake.
    /// </remarks>
    private static AuthoredEmailRefusal? RefuseInternationalizedSender(
        OutgoingSenderIdentity sender,
        MailDeliveryCapabilities capabilities) =>
        CarriesInternationalizedAddresses(capabilities) || Ascii.IsValid(sender.Address.Address)
            ? null
            : new AuthoredEmailRefusal(
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
    private static AuthoredEmailRefusal? RefuseInjectedHeaders(AuthoredEmail authored)
    {
        if (CarriesLineBreak(authored.Subject))
        {
            return new AuthoredEmailRefusal(
                AuthoredEmailRefusalReason.HeaderInjected,
                AuthoredEmailField.Subject);
        }

        var injectedRecipient = authored.Recipients.FirstOrDefault(
            static recipient => CarriesLineBreak(recipient.Address) || CarriesLineBreak(recipient.DisplayName));
        if (injectedRecipient is not null)
        {
            return new AuthoredEmailRefusal(
                AuthoredEmailRefusalReason.HeaderInjected,
                FieldOf(injectedRecipient.Role));
        }

        return authored.Attachments.Any(
            static attachment => CarriesLineBreak(attachment.FileName) || CarriesLineBreak(attachment.MediaType))
            ? new AuthoredEmailRefusal(
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
    private static AuthoredEmailRefusal? RefuseBodiesNobodyWrote(AuthoredEmail authored)
    {
        if (string.IsNullOrWhiteSpace(authored.PlainTextBody))
        {
            return new AuthoredEmailRefusal(
                AuthoredEmailRefusalReason.FieldUnusable,
                AuthoredEmailField.PlainTextBody);
        }

        return authored.HtmlBody is { } htmlBody && string.IsNullOrWhiteSpace(htmlBody)
            ? new AuthoredEmailRefusal(
                AuthoredEmailRefusalReason.FieldUnusable,
                AuthoredEmailField.HtmlBody)
            : null;
    }

    /// <summary>Refuses a body larger than this deployment composes.</summary>
    private AuthoredEmailRefusal? RefuseBodiesBeyondBounds(AuthoredEmail authored)
    {
        if (authored.PlainTextBody.Length > bounds.MaxBodyCharacters)
        {
            return new AuthoredEmailRefusal(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.PlainTextBody,
                bounds.MaxBodyCharacters);
        }

        return authored.HtmlBody is { } htmlBody && htmlBody.Length > bounds.MaxBodyCharacters
            ? new AuthoredEmailRefusal(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.HtmlBody,
                bounds.MaxBodyCharacters)
            : null;
    }

    /// <summary>Refuses more files, or a larger one, than this deployment composes, and a file declared as no media type.</summary>
    private AuthoredEmailRefusal? RefuseAttachmentsBeyondBounds(AuthoredEmail authored)
    {
        if (authored.Attachments.Count > bounds.MaxAttachmentCount)
        {
            return new AuthoredEmailRefusal(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.Attachment,
                bounds.MaxAttachmentCount);
        }

        if (authored.Attachments.Any(attachment => attachment.Content.Length > bounds.MaxAttachmentBytes))
        {
            return new AuthoredEmailRefusal(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.Attachment,
                bounds.MaxAttachmentBytes);
        }

        // The whole-message bound is measured on what the message became, which means assembling every part and
        // transfer-encoding it first. Content already past that bound before any of it happens is refused here instead,
        // because encoding only ever makes the octets more numerous, never fewer.
        if (authored.Attachments.Sum(static attachment => (long)attachment.Content.Length) > bounds.MaxMessageBytes)
        {
            return new AuthoredEmailRefusal(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.Message,
                bounds.MaxMessageBytes);
        }

        return authored.Attachments.Any(static attachment =>
            string.IsNullOrWhiteSpace(attachment.FileName) || !ContentType.TryParse(attachment.MediaType, out _))
            ? new AuthoredEmailRefusal(
                AuthoredEmailRefusalReason.FieldUnusable,
                AuthoredEmailField.Attachment)
            : null;
    }

    /// <summary>Parses every authored recipient into the mailbox the envelope will offer, or refuses naming the header.</summary>
    /// <remarks>
    /// <para>
    /// One mailbox is offered once, so a person an author named in two headers is placed in the first of them. The
    /// later mention is not discarded, though: it narrows the placement's recorded provenance where the caller named
    /// the address itself, because that is the reading the sending governance weighs. The order is the header order — <c>To</c>, then <c>Cc</c>, then <c>Bcc</c> — because
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
    /// <para>
    /// An empty result is refused only where a message is being sent. A draft addressed to nobody is composed, because
    /// it is offered to nobody either; what refuses it is the promotion that would put it on its way.
    /// </para>
    /// </remarks>
    private AuthoredEmailRefusal? ResolveRecipients(
        IReadOnlyList<AuthoredEmailRecipient> authoredRecipients,
        MailDeliveryCapabilities capabilities,
        bool requireRecipients,
        out IReadOnlyList<PlacedRecipient> placements)
    {
        placements = [];

        if (authoredRecipients.Count > bounds.MaxRecipientCount * AddressableHeaderCount)
        {
            return new AuthoredEmailRefusal(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.Recipients,
                bounds.MaxRecipientCount);
        }

        var placed = new List<PlacedRecipient>(authoredRecipients.Count);
        var placementsByAddress = new Dictionary<EmailAddress, int>();

        foreach (var authoredRecipient in authoredRecipients.OrderBy(static recipient => recipient.Role))
        {
            var field = FieldOf(authoredRecipient.Role);

            if (!EmailAddress.TryCreate(authoredRecipient.DisplayName, authoredRecipient.Address, out var address)
                || address.Address.Length > OutgoingRecipient.MaximumAddressLength)
            {
                return new AuthoredEmailRefusal(AuthoredEmailRefusalReason.FieldUnusable, field);
            }

            if (!CarriesInternationalizedAddresses(capabilities) && !Ascii.IsValid(address.Address))
            {
                return new AuthoredEmailRefusal(
                    AuthoredEmailRefusalReason.InternationalizationUnsupported,
                    field);
            }

            if (placementsByAddress.TryGetValue(address, out var placementIndex))
            {
                // A mailbox named twice is offered once, and the mention that decides how it is judged is the strictest
                // of them. A caller that also wrote down an address this system derived has named that address, and
                // naming it is exactly what the sending governance weighs — so the redundant mention narrows the
                // placement rather than being dropped whole. Keeping the derived reading would let a caller buy an
                // address the vouching would otherwise judge, by mentioning somebody the answered message already
                // named; the direct send judges the list before this dedup and would refuse the same message.
                if (authoredRecipient.Provenance is AuthoredRecipientProvenance.NamedByCaller)
                {
                    placed[placementIndex] = placed[placementIndex] with
                    {
                        Provenance = AuthoredRecipientProvenance.NamedByCaller,
                    };
                }

                continue;
            }

            placementsByAddress.Add(address, placed.Count);
            placed.Add(new PlacedRecipient(
                OutgoingRecipient.Create(address, authoredRecipient.Role, authoredRecipient.Contact),
                address.DisplayName,
                authoredRecipient.Provenance));
        }

        if (requireRecipients && placed.Count == 0)
        {
            return new AuthoredEmailRefusal(
                AuthoredEmailRefusalReason.FieldUnusable,
                AuthoredEmailField.Recipients);
        }

        if (placed.Count > bounds.MaxRecipientCount)
        {
            return new AuthoredEmailRefusal(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.Recipients,
                bounds.MaxRecipientCount);
        }

        placements = placed;

        return null;
    }

    /// <summary>Assembles the message and measures what it became.</summary>
    private MailDraftComposition Build(
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

        return this.Serialize(
            message,
            messageId,
            sender,
            [.. placements.Select(static placement => new MailDraftRecipient(
                placement.Recipient,
                placement.Provenance))],
            capabilities);
    }

    /// <summary>Writes an assembled message out, measures what it became, and refuses it against every size bound.</summary>
    /// <remarks>
    /// It is the half of composing that is the same for a message somebody has just written and for one occasion of a
    /// message they wrote weeks ago: what a submission server will read is decided by the server rather than by where
    /// the message came from, and a second copy of these decisions would be a second set of answers about blind
    /// recipients, line endings, and transfer encoding.
    /// </remarks>
    private MailDraftComposition Serialize(
        MimeMessage message,
        InternetMessageId messageId,
        OutgoingSenderIdentity sender,
        IReadOnlyList<MailDraftRecipient> recipients,
        MailDeliveryCapabilities capabilities)
    {
        var format = FormatOptions.Default.Clone();

        // Every address was refused above unless the server carries it, so the only question left is whether the
        // message needs the format at all: an all-ASCII message is written the way every server understands.
        format.International = !Ascii.IsValid(sender.Address.Address)
            || recipients.Any(static recipient => !Ascii.IsValid(recipient.Recipient.Address.Address));

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
            return Refused(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.Message,
                bounds.MaxMessageBytes);
        }

        if (!capabilities.PermitsMessageOfSize(composed.Length))
        {
            return Refused(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.Message,
                capabilities.MaxMessageBytes);
        }

        return MailDraftComposition.Composed(
            new ComposedMailDraft(recipients, messageId, composed.ToArray()));
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

    /// <summary>Turns a composed message into the send it will be transmitted as, or carries its refusal through.</summary>
    /// <remarks>
    /// The request is built here rather than inside the composition, because it is the one part of a send a draft has
    /// no counterpart for: it carries the idempotency identity a delivery is protected by, and a draft has none.
    /// </remarks>
    private static AuthoredEmailComposition AsOutgoing(
        MailAccountId accountId,
        OutgoingEmailRequester requester,
        MailDraftComposition composition)
    {
        if (composition.Draft is not { } message)
        {
            return AuthoredEmailComposition.Refused(composition.Refusal!);
        }

        var request = OutgoingEmailRequest.Create(
            accountId,
            requester,
            [.. message.Recipients.Select(static recipient => recipient.Recipient)]);

        return AuthoredEmailComposition.Composed(
            new ComposedOutgoingEmail(request, message.MessageId, message.RawMime));
    }

    /// <summary>Writes one refusal in the shape the whole composition answers with.</summary>
    private static MailDraftComposition Refused(
        AuthoredEmailRefusalReason reason,
        AuthoredEmailField field,
        long? bound = null) =>
        MailDraftComposition.Refused(new AuthoredEmailRefusal(reason, field, bound));

    /// <summary>Holds one recipient the envelope will offer, with what the composition still owes about them.</summary>
    /// <remarks>
    /// The three travel together only as far as this composition. The record keeps the address because a send cannot be
    /// resumed without it, the name stays in the stored MIME, and the provenance reaches a draft's own rows so that the
    /// promotion can be judged by where each address came from.
    /// </remarks>
    private readonly record struct PlacedRecipient(
        OutgoingRecipient Recipient,
        string? DisplayName,
        AuthoredRecipientProvenance Provenance);
}
