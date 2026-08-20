// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers;
using System.Globalization;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Delivery.Scheduling;
using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Delivery;

/// <summary>Names the authored act that asked for a message to be sent, in a form two requests can be compared by.</summary>
/// <remarks>
/// <para>
/// This is the half of an outgoing email's idempotency identity that says who asked; the sending account is the other.
/// It has to answer one question: would asking again be the same request or a new one. A rule answers it with its own
/// name and the revision it was evaluated at, so re-evaluating an unchanged rule sends nothing a second time and
/// changing the rule asks afresh. Somebody present answers it with a key of their own, so a retried command is the same
/// request and a second command is a second one. A recurring send answers it with the declaration and the occasion, so
/// every Monday is a request of its own and one Monday reached twice is not.
/// </para>
/// <para>
/// A duplicated delivery cannot be withdrawn, so the key is what the caller is answerable for rather than something
/// derived here: a caller that generates a fresh key per attempt has asked twice, and this type cannot tell that apart
/// from two genuine sends. What it does guarantee is that one key sends once.
/// </para>
/// <para>
/// Only MailFathom's own configured names and a caller's own key reach here. A rule name is chosen by the operator and
/// a command key by whoever invoked it; no recipient, subject, or other mail content is part of a requester, which is
/// what keeps the identity of an outgoing record free of the message it is about.
/// </para>
/// </remarks>
public sealed record OutgoingEmailRequester
{
    /// <summary>The greatest length an identity may have, which bounds the column it is stored in and the index over it.</summary>
    public const int MaximumIdentityLength = 128;

    /// <summary>The two characters a rule's identity is composed with, and which therefore cannot appear inside its parts.</summary>
    private static readonly SearchValues<char> ComposedIdentitySeparators = SearchValues.Create(['@', '#']);

    private OutgoingEmailRequester(OutgoingEmailOrigin origin, string identity)
    {
        this.Origin = origin;
        this.Identity = identity;
    }

    /// <summary>Gets what kind of authored act asked.</summary>
    public OutgoingEmailOrigin Origin { get; }

    /// <summary>Gets the identity that decides whether asking again is the same request.</summary>
    public string Identity { get; }

    /// <summary>Names a rule together with the revision of it that asked, and the email it acted on.</summary>
    /// <param name="ruleName">The operator's own name for the rule.</param>
    /// <param name="revision">The identity of the rule set revision the request was produced from.</param>
    /// <param name="actedOn">The local email the rule matched, which is what makes one rule's two sends two requests.</param>
    /// <returns>A requester naming that revision of that rule acting on that email.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="ruleName" /> or <paramref name="revision" /> is blank, carries a control character, carries one of the <c>@</c> and <c>#</c> characters the identity is composed with, or is long enough that the composed identity exceeds <see cref="MaximumIdentityLength" />.</exception>
    /// <remarks>
    /// The email is part of the identity here and is not part of a mutation's, because the two records are keyed
    /// differently: a mutation is recorded against the occurrence it changes, so its requester never has to name one,
    /// while an outgoing email is recorded against an account and would otherwise let one rule send once for a whole
    /// mailbox. The value is MailFathom's own local identifier rather than anything the message said.
    /// </remarks>
    public static OutgoingEmailRequester Rule(string ruleName, string revision, StoredEmailId actedOn)
    {
        var trimmedRuleName = ValidComposedPart(ruleName, nameof(ruleName));
        var trimmedRevision = ValidComposedPart(revision, nameof(revision));

        var identity = string.Create(
            CultureInfo.InvariantCulture,
            $"{trimmedRuleName}@{trimmedRevision}#{actedOn}");

        // Both parts are the caller's and either can be the one that overflowed, so the refusal names whichever no
        // longer fits beside the other rather than always the first. The email is MailFathom's own identifier and is
        // fixed in length, so it is never the part somebody can shorten.
        if (identity.Length > MaximumIdentityLength)
        {
            throw new ArgumentException(
                $"An outgoing email requester identity may be at most {MaximumIdentityLength} characters long.",
                trimmedRuleName.Length >= trimmedRevision.Length ? nameof(ruleName) : nameof(revision));
        }

        return new OutgoingEmailRequester(OutgoingEmailOrigin.Rule, identity);
    }

    /// <summary>Names one occasion of a recurring send the owner declared, which is the declaration and the occasion together.</summary>
    /// <param name="declaration">The recurring send whose occasion came round.</param>
    /// <param name="occurrence">The occasion itself, which is what makes one Monday's message a different request from the next.</param>
    /// <returns>A requester naming that occasion of that declaration.</returns>
    /// <remarks>
    /// Nobody is present when an occurrence is composed, so there is no key for a caller to supply and the identity is
    /// derived instead. The occasion is written to the second in UTC, so two instances reaching one occasion compose
    /// the same identity and the unique index answers the second with the message the first wrote — the same reasoning
    /// the recurring dispatch itself is keyed by, restated where a duplicate would be a second message in somebody's
    /// mailbox rather than a second row.
    /// </remarks>
    public static OutgoingEmailRequester Schedule(RecurringSendId declaration, DateTimeOffset occurrence) => new(
        OutgoingEmailOrigin.Schedule,
        string.Create(
            CultureInfo.InvariantCulture,
            $"{declaration.Value:D}@{occurrence.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}"));

    /// <summary>Names one act somebody asked for, by the key they supplied for it.</summary>
    /// <param name="invocationIdentity">The key that decides whether asking again is the same request: the same for a retry of one act and different for a second act.</param>
    /// <returns>A requester naming that act.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="invocationIdentity" /> is blank, carries a control character, or is longer than <see cref="MaximumIdentityLength" />.</exception>
    public static OutgoingEmailRequester Command(string invocationIdentity) => new(
        OutgoingEmailOrigin.Command,
        ValidIdentity(invocationIdentity, nameof(invocationIdentity)));

    /// <summary>Names the promotion of one draft, which is the draft rather than a key whoever asked supplied.</summary>
    /// <param name="draft">The draft being sent, which is what makes two callers promoting it one request.</param>
    /// <returns>A requester naming that draft's promotion.</returns>
    /// <remarks>
    /// A draft is promoted once, so the draft is the act and there is nothing for a caller to key it by. That is what
    /// closes the race a supplied key would leave open: two callers promoting one draft together both find it
    /// unpromoted, and it is this identity that makes their two asks one record instead of one message sent twice —
    /// the same reasoning an occasion of a recurring send is keyed by, where the duplicate would likewise be a message
    /// in somebody's mailbox rather than a row.
    /// </remarks>
    public static OutgoingEmailRequester Draft(MailDraftId draft) => new(
        OutgoingEmailOrigin.Command,
        string.Create(CultureInfo.InvariantCulture, $"draft@{draft.Value:D}"));

    /// <summary>Restores a requester from the origin and identity a record holds.</summary>
    /// <param name="origin">The kind of authored act that asked.</param>
    /// <param name="identity">The stored identity.</param>
    /// <returns>The requester those two name.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="identity" /> is blank, carries a control character, or is longer than <see cref="MaximumIdentityLength" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="origin" /> is not a declared origin.</exception>
    public static OutgoingEmailRequester Create(OutgoingEmailOrigin origin, string identity)
    {
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(
                nameof(origin),
                origin,
                "The outgoing email requester origin is not one this system declares.");
        }

        return new OutgoingEmailRequester(origin, ValidIdentity(identity, nameof(identity)));
    }

    /// <summary>Validates an identity and reports a refusal against the parameter the caller actually supplied.</summary>
    /// <remarks>
    /// The parameter name travels in rather than being taken from this method's own signature, because every factory
    /// above names its input differently and a refusal naming a parameter the caller never wrote is a refusal they
    /// cannot act on.
    /// </remarks>
    private static string ValidIdentity(string identity, string parameterName)
    {
        var trimmedIdentity = ValidIdentityPart(identity, parameterName);

        if (trimmedIdentity.Length > MaximumIdentityLength)
        {
            throw new ArgumentException(
                $"An outgoing email requester identity may be at most {MaximumIdentityLength} characters long.",
                parameterName);
        }

        return trimmedIdentity;
    }

    /// <summary>Validates what a caller supplied, whether it is the whole identity or one part of a composed one.</summary>
    /// <remarks>
    /// Length is deliberately not checked here. A part is bounded by what the composed identity may be rather than on
    /// its own, so the caller that composes decides which parameter an overflow is reported against.
    /// </remarks>
    private static string ValidIdentityPart(string identityPart, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityPart, parameterName);

        var trimmedPart = identityPart.Trim();

        // A control character would make the identity unreadable in the outbox query the record exists to serve, and it
        // is never part of a name an operator wrote or a key a caller generated.
        if (trimmedPart.Any(char.IsControl))
        {
            throw new ArgumentException(
                "An outgoing email requester identity cannot contain a control character.",
                parameterName);
        }

        return trimmedPart;
    }

    /// <summary>Validates one part of a composed identity, including that it cannot be mistaken for another split of it.</summary>
    /// <remarks>
    /// The separators are refused rather than escaped, because what a composed identity has to guarantee is that two
    /// distinct pairs cannot write the same string: a rule named <c>a</c> at revision <c>b@c</c> and a rule named
    /// <c>a@b</c> at revision <c>c</c> would otherwise compose one identity, and the unique index would read the second
    /// rule's genuine send as a retry of the first one's and never send it. Neither character belongs in a rule name or
    /// a revision an operator wrote, so refusing them costs nothing a caller can legitimately want.
    /// </remarks>
    private static string ValidComposedPart(string identityPart, string parameterName)
    {
        var trimmedPart = ValidIdentityPart(identityPart, parameterName);

        if (trimmedPart.AsSpan().ContainsAny(ComposedIdentitySeparators))
        {
            throw new ArgumentException(
                "A rule name and a revision cannot contain the characters an outgoing email's identity is composed with.",
                parameterName);
        }

        return trimmedPart;
    }

    /// <inheritdoc />
    public override string ToString() => $"{this.Origin}:{this.Identity}";
}
