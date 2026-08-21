// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.Mail.Delivery.Composition;
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
/// is an operator's to configure. Which code and which sentence each refusal produces is
/// <see cref="AuthoredMailRefusalPublication" />'s answer rather than this type's, because saving a draft refuses the
/// same set for the same reasons and the two must not tell one author two different things about one mistake. Each
/// message names the field, the number, or the count that decided it, and none of them carries an address, a subject, a
/// body, or anybody the contact book was searched for.
/// </para>
/// </remarks>
public sealed class MailSubmissionRefusedException : MailFathomException
{
    private MailSubmissionRefusedException(PublishedMailRefusal refusal)
        : base(refusal.Message) => this.ErrorCode = refusal.Code;

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode { get; }

    /// <summary>Reports a message that was not composed, in the terms the author can act on.</summary>
    /// <param name="refusal">What the composition refused and where.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusal" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the refusal names a reason this system does not declare, which a refusal built from a cast integer is.</exception>
    public static MailSubmissionRefusedException From(AuthoredEmailRefusal refusal) =>
        new(AuthoredMailRefusalPublication.Of(refusal));

    /// <summary>Reports a recipient that resolved to nobody, naming what was counted and never who.</summary>
    /// <param name="refusal">What the resolution refused.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusal" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the refusal names a reason this system does not declare.</exception>
    public static MailSubmissionRefusedException From(RecipientResolutionRefusal refusal) =>
        new(AuthoredMailRefusalPublication.Of(refusal));

    /// <summary>Reports an answer to a stored email that was not authored at all, in the terms the caller can act on.</summary>
    /// <param name="refusal">What the authoring refused.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusal" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the refusal names a reason this system does not declare, which a refusal built from a cast integer is.</exception>
    /// <remarks>
    /// The email that cannot be found and the email whose content cannot be read arrive as one answer on purpose, and
    /// the publication is where the two are joined: the authoring tells them apart because it records a repair request
    /// for the second, and a caller that could tell them apart would learn which mail exists by asking to reply to it.
    /// </remarks>
    public static MailSubmissionRefusedException From(AuthoredResponseRefusal refusal) =>
        new(AuthoredMailRefusalPublication.Of(refusal));

    /// <summary>Reports a list of recipients longer than any outgoing record could be written for.</summary>
    /// <returns>The failure to raise.</returns>
    public static MailSubmissionRefusedException TooManyRecipients() =>
        new(AuthoredMailRefusalPublication.TooManyRecipients());

    /// <summary>Reports text that names no account this deployment could look for.</summary>
    /// <returns>The failure to raise.</returns>
    public static MailSubmissionRefusedException AccountNotNamed() =>
        new(AuthoredMailRefusalPublication.AccountNotNamed());

    /// <summary>Reports an idempotency key no outgoing record could be written under.</summary>
    /// <returns>The failure to raise.</returns>
    public static MailSubmissionRefusedException IdempotencyKeyUnusable() =>
        new(AuthoredMailRefusalPublication.IdempotencyKeyUnusable());

    /// <summary>Reports a message asked to leave at a time that has already gone.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// It is refused rather than sent at once, because the two readings of a time in the past are opposite — somebody
    /// who meant tomorrow and wrote yesterday's date wants to fix it, and somebody who meant now would not have named a
    /// time — and a system that guessed would sometimes send a message the author was still writing. The instant the
    /// caller sent is not repeated back: it is theirs, and a message stating a clock reading would say nothing they do
    /// not already know.
    /// </remarks>
    public static MailSubmissionRefusedException DueTimeAlreadyPassed() =>
        new(AuthoredMailRefusalPublication.DueTimeAlreadyPassed());

    /// <summary>Reports a repetition written in a form the schedule syntax does not name.</summary>
    /// <param name="described">What is wrong with the declaration, in the words the syntax itself states its rules in.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="described" /> is blank.</exception>
    /// <remarks>
    /// The description is the schedule syntax's own, carried through rather than restated, so a caller reads one
    /// account of the forms a repetition may be written in wherever they meet the refusal. It names the forms and the
    /// bounds and never the message, which is the same rule every refusal here obeys.
    /// </remarks>
    public static MailSubmissionRefusedException ScheduleUnreadable(string described) =>
        new(AuthoredMailRefusalPublication.ScheduleUnreadable(described));

    /// <summary>Reports a reply whose audience names neither of the two acts a reply may be.</summary>
    /// <returns>The failure to raise.</returns>
    public static MailSubmissionRefusedException ReplyAudienceUnknown() =>
        new(AuthoredMailRefusalPublication.ReplyAudienceUnknown());
}
