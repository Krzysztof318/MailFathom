// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Delivery.Submission;

/// <summary>The failure raised when a message somebody asked to send is not written down at all.</summary>
/// <remarks>
/// <para>
/// Addressing and composition each answer with a refusal rather than an exception, because both are steps inside a
/// submission and a step tells the step above it what happened. This is the boundary of the submission itself, and a
/// caller that reached it asked for one thing: a message to be queued. So the whole of what it needs back is that the
/// message was not queued and what to do about it, which is a coded failure — the same shape every other refusal a
/// protocol boundary publishes already takes, and one no adapter has to translate a second time.
/// </para>
/// <para>
/// The code is chosen per refusal rather than fixed for the type, because the three families call for different acts: a
/// field is rewritten, a bound is written under, somebody is named differently, and an account that cannot send at all
/// is an operator's to configure. Each message names the field, the number, or the count that decided it, and none of
/// them carries an address, a subject, a body, or anybody the contact book was searched for.
/// </para>
/// </remarks>
public sealed class MailSubmissionRefusedException : MailFathomException
{
    private MailSubmissionRefusedException(MailFathomErrorCode errorCode, string clientSafeMessage)
        : base(clientSafeMessage) => this.ErrorCode = errorCode;

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode { get; }

    /// <summary>Reports a message that was not composed, in the terms the author can act on.</summary>
    /// <param name="refusal">What the composition refused and where.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusal" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the refusal names a reason this system does not declare, which a refusal built from a cast integer is.</exception>
    public static MailSubmissionRefusedException From(AuthoredEmailRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);

        var field = Published(refusal.Field);

        return refusal.Reason switch
        {
            AuthoredEmailRefusalReason.SenderUnconfigured => new MailSubmissionRefusedException(
                MailFathomErrorCode.MailSendingUnavailable,
                "The account this message would be sent as configures no address to send from, so this deployment cannot send from it."),
            AuthoredEmailRefusalReason.HeaderInjected => new MailSubmissionRefusedException(
                MailFathomErrorCode.AuthoredMailFieldRefused,
                $"The {field} of a message cannot carry a line break, because it is written into a header."),
            AuthoredEmailRefusalReason.FieldUnusable => new MailSubmissionRefusedException(
                MailFathomErrorCode.AuthoredMailFieldRefused,
                $"The {field} of a message carries a value no message can be composed from."),
            AuthoredEmailRefusalReason.InternationalizationUnsupported => new MailSubmissionRefusedException(
                MailFathomErrorCode.AuthoredMailFieldRefused,
                $"The {field} of a message names an address outside ASCII, which this deployment does not compose."),
            AuthoredEmailRefusalReason.BoundExceeded => new MailSubmissionRefusedException(
                MailFathomErrorCode.AuthoredMailBoundExceeded,
                BoundMessage(field, refusal.Bound)),
            _ => throw new InvalidOperationException(
                "The composition refusal reason is not one this system declares."),
        };
    }

    /// <summary>Reports a recipient that resolved to nobody, naming what was counted and never who.</summary>
    /// <param name="refusal">What the resolution refused.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusal" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the refusal names a reason this system does not declare.</exception>
    public static MailSubmissionRefusedException From(RecipientResolutionRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);

        var described = refusal.Reason switch
        {
            RecipientResolutionRefusalReason.ContactUnknown => UnknownContactMessage,
            RecipientResolutionRefusalReason.ContactNameAmbiguous =>
                AmbiguousContactMessage(refusal.MatchedContactCount),
            RecipientResolutionRefusalReason.ContactAddressNotHeld => ContactAddressNotHeldMessage,
            _ => throw new InvalidOperationException(
                "The recipient resolution refusal reason is not one this system declares."),
        };

        return new MailSubmissionRefusedException(
            MailFathomErrorCode.AuthoredMailRecipientUnresolved,
            described);
    }

    /// <summary>Reports an answer to a stored email that was not authored at all, in the terms the caller can act on.</summary>
    /// <param name="refusal">What the authoring refused.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusal" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the refusal names a reason this system does not declare, which a refusal built from a cast integer is.</exception>
    /// <remarks>
    /// The email that cannot be found and the email whose content cannot be read arrive as one answer on purpose, and
    /// this is where the two are joined: the authoring tells them apart because it records a repair request for the
    /// second, and a caller that could tell them apart would learn which mail exists by asking to reply to it. Whether
    /// the local copy is being repaired is the deployment's business rather than the sender's.
    /// </remarks>
    public static MailSubmissionRefusedException From(AuthoredResponseRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);

        return refusal.Reason switch
        {
            AuthoredResponseRefusalReason.AnsweredEmailNotFound
                or AuthoredResponseRefusalReason.AnsweredEmailContentUnavailable =>
                new MailSubmissionRefusedException(
                    MailFathomErrorCode.AnsweredEmailUnavailable,
                    "No email this deployment can answer is held under that identifier."),
            AuthoredResponseRefusalReason.SenderUnconfigured => new MailSubmissionRefusedException(
                MailFathomErrorCode.MailSendingUnavailable,
                "The account the answered email was stored from configures no address to send from, so this deployment cannot answer mail in it."),
            AuthoredResponseRefusalReason.BoundExceeded => new MailSubmissionRefusedException(
                MailFathomErrorCode.AuthoredMailBoundExceeded,
                CarriedFileBoundMessage(refusal.Bound)),
            AuthoredResponseRefusalReason.RecipientContactUnknown => new MailSubmissionRefusedException(
                MailFathomErrorCode.AuthoredMailRecipientUnresolved,
                UnknownContactMessage),
            AuthoredResponseRefusalReason.RecipientContactNameAmbiguous => new MailSubmissionRefusedException(
                MailFathomErrorCode.AuthoredMailRecipientUnresolved,
                AmbiguousContactMessage(refusal.MatchedContactCount)),
            AuthoredResponseRefusalReason.RecipientContactAddressNotHeld => new MailSubmissionRefusedException(
                MailFathomErrorCode.AuthoredMailRecipientUnresolved,
                ContactAddressNotHeldMessage),
            _ => throw new InvalidOperationException(
                "The authoring refusal reason is not one this system declares."),
        };
    }

    /// <summary>Reports a list of recipients longer than any outgoing record could be written for.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// It is refused before the contact book is read, because the reads carry what the caller supplied. The number
    /// named is what a record holds rather than what this deployment composes: the second is smaller and is the
    /// composition's to state, and a caller told the larger one first still learns the smaller one on its next attempt.
    /// </remarks>
    public static MailSubmissionRefusedException TooManyRecipients() => new(
        MailFathomErrorCode.AuthoredMailBoundExceeded,
        string.Create(
            CultureInfo.InvariantCulture,
            $"A message names at most {OutgoingEmailRequest.MaximumRecipientCount} recipients across its to, cc, and bcc headers."));

    /// <summary>Reports text that names no account this deployment could look for.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// It is separate from an account that was named and is not served, because the two are different mistakes and the
    /// second is not a mistake a caller can always avoid. Nothing about the served accounts is revealed by either: this
    /// one says the text is not a name at all, which is true whatever this deployment holds.
    /// </remarks>
    public static MailSubmissionRefusedException AccountNotNamed() => new(
        MailFathomErrorCode.AuthoredMailFieldRefused,
        string.Create(
            CultureInfo.InvariantCulture,
            $"The account a message is sent as is named by 1 to {MailAccountSelector.MaximumLength} characters and no control character."));

    /// <summary>Reports an idempotency key no outgoing record could be written under.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// The rules are the record's own and are checked again where its column is bounded. Refusing here is what lets a
    /// caller meet a statement about the key it sent rather than an argument failure naming a parameter it never wrote.
    /// </remarks>
    public static MailSubmissionRefusedException IdempotencyKeyUnusable() => new(
        MailFathomErrorCode.AuthoredMailFieldRefused,
        string.Create(
            CultureInfo.InvariantCulture,
            $"The idempotency key a send is asked under carries 1 to {OutgoingEmailRequester.MaximumIdentityLength} characters and no control character."));

    /// <summary>Reports a message asked to leave at a time that has already gone.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// It is refused rather than sent at once, because the two readings of a time in the past are opposite — somebody
    /// who meant tomorrow and wrote yesterday's date wants to fix it, and somebody who meant now would not have named a
    /// time — and a system that guessed would sometimes send a message the author was still writing. The instant the
    /// caller sent is not repeated back: it is theirs, and a message stating a clock reading would say nothing they do
    /// not already know.
    /// </remarks>
    public static MailSubmissionRefusedException DueTimeAlreadyPassed() => new(
        MailFathomErrorCode.AuthoredMailScheduleRefused,
        "A message is held until a time still to come; the time this send names has already passed.");

    /// <summary>Reports a repetition written in a form the schedule syntax does not name.</summary>
    /// <param name="described">What is wrong with the declaration, in the words the syntax itself states its rules in.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="described" /> is blank.</exception>
    /// <remarks>
    /// The description is the schedule syntax's own, carried through rather than restated, so a caller reads one
    /// account of the forms a repetition may be written in wherever they meet the refusal. It names the forms and the
    /// bounds and never the message, which is the same rule every refusal here obeys.
    /// </remarks>
    public static MailSubmissionRefusedException ScheduleUnreadable(string described)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(described);

        return new MailSubmissionRefusedException(
            MailFathomErrorCode.AuthoredMailScheduleRefused,
            $"The repetition a send declares is unusable: {described}");
    }

    /// <summary>Reports a reply whose audience names neither of the two acts a reply may be.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// It is unreachable from a client that sent a name the schema declares, because the protocol library refuses an
    /// unknown one before a boundary is entered. What reaches it is a numeric value outside the set, which is the
    /// caller's own input and therefore an argument to state a rule about rather than an internal fault to collapse.
    /// </remarks>
    public static MailSubmissionRefusedException ReplyAudienceUnknown() => new(
        MailFathomErrorCode.AuthoredMailFieldRefused,
        "A reply states whether it answers the sender alone or everybody the message was between, and it named neither.");

    /// <summary>The answer a contact nobody holds produces, written once because both authored acts meet it.</summary>
    /// <remarks>
    /// The three contact refusals read identically whether the author was writing a new message or answering one, and
    /// they say so from one text apiece rather than from two that could drift: what the author does about a name the
    /// book cannot settle is the same in both.
    /// </remarks>
    private const string UnknownContactMessage =
        "One recipient names a contact this deployment's contact book does not hold.";

    /// <summary>The answer an address the named contact does not use produces.</summary>
    private const string ContactAddressNotHeldMessage =
        "One recipient chooses an address the contact it names does not hold.";

    /// <summary>Writes the ambiguous-name refusal, which names how many contacts carried the name and nothing about any of them.</summary>
    private static string AmbiguousContactMessage(int? matchedContactCount) => string.Create(
        CultureInfo.InvariantCulture,
        $"One recipient names a contact by a name {matchedContactCount} contacts carry, so it names nobody.");

    /// <summary>Writes the refusal a forward carrying more than this deployment composes produces.</summary>
    /// <remarks>
    /// The files belong to the message being forwarded rather than to whoever is forwarding it, so the field a
    /// composition refusal would name says nothing the author can act on and the number is the whole of the remedy:
    /// the message is too large to forward from here, and forwarding fewer of somebody else's files is not something a
    /// caller can do. Which of the three bounds it was — the count, one file, or the whole message — is deliberately
    /// unstated, because stating it would describe the message being forwarded.
    /// </remarks>
    private static string CarriedFileBoundMessage(long? bound) => bound is { } declared
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"The files the answered message carries exceed a bound this deployment composes, which is {declared}.")
        : "The files the answered message carries exceed a bound this deployment composes.";

    /// <summary>Writes the bound refusal, which names a number only where the refusal carried one.</summary>
    /// <remarks>
    /// Every bound the composition refuses carries its number, so the second form is unreachable from a refusal this
    /// system produced. It exists because the number is optional on the refusal record, and a message reading
    /// <c>exceeds the bound of</c> followed by nothing is worse than one that simply does not state it.
    /// </remarks>
    private static string BoundMessage(string field, long? bound) => bound is { } declared
        ? string.Create(CultureInfo.InvariantCulture, $"The {field} of a message exceeds the bound this deployment composes, which is {declared}.")
        : $"The {field} of a message exceeds a bound this deployment composes.";

    /// <summary>Names the part of the message a refusal is about, in the spelling a caller wrote it under.</summary>
    /// <remarks>
    /// The names are the authored message's own rather than any one boundary's argument names, so the same refusal
    /// reads identically whichever entrypoint asked for the send. Nothing that was in the field is named beside it.
    /// </remarks>
    private static string Published(AuthoredEmailField field) => field switch
    {
        AuthoredEmailField.Recipients => "recipients",
        AuthoredEmailField.To => "to recipients",
        AuthoredEmailField.Cc => "cc recipients",
        AuthoredEmailField.Bcc => "bcc recipients",
        AuthoredEmailField.Subject => "subject",
        AuthoredEmailField.PlainTextBody => "plainTextBody",
        AuthoredEmailField.HtmlBody => "htmlBody",
        AuthoredEmailField.Attachment => "attachment",
        AuthoredEmailField.Sender => "sending address",
        AuthoredEmailField.Message => "whole message",
        _ => throw new InvalidOperationException("The authored email field is not one this system declares."),
    };
}
