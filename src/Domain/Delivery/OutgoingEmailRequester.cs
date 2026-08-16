// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Delivery;

/// <summary>Names the authored act that asked for a message to be sent, in a form two requests can be compared by.</summary>
/// <remarks>
/// <para>
/// This is the half of an outgoing message's idempotency identity that says who asked; the sending account is the other.
/// It has to answer one question: would asking again be the same request or a new one. A rule answers it with its own
/// name and the revision it was evaluated at, so re-evaluating an unchanged rule sends nothing a second time and
/// changing the rule asks afresh. Somebody present answers it with a key of their own, so a retried command is the same
/// request and a second command is a second one.
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
    /// <exception cref="ArgumentException">Thrown when <paramref name="ruleName" /> or <paramref name="revision" /> is blank, carries a control character, or is long enough that the composed identity exceeds <see cref="MaximumIdentityLength" />.</exception>
    /// <remarks>
    /// The email is part of the identity here and is not part of a mutation's, because the two records are keyed
    /// differently: a mutation is recorded against the occurrence it changes, so its requester never has to name one,
    /// while an outgoing message is recorded against an account and would otherwise let one rule send once for a whole
    /// mailbox. The value is MailFathom's own local identifier rather than anything the message said.
    /// </remarks>
    public static OutgoingEmailRequester Rule(string ruleName, string revision, StoredEmailId actedOn)
    {
        var trimmedRuleName = ValidIdentityPart(ruleName, nameof(ruleName));
        var trimmedRevision = ValidIdentityPart(revision, nameof(revision));

        var identity = string.Create(
            CultureInfo.InvariantCulture,
            $"{trimmedRuleName}@{trimmedRevision}#{actedOn}");

        // Both parts are the caller's and either can be the one that overflowed, so the refusal names whichever no
        // longer fits beside the other rather than always the first. The email is MailFathom's own identifier and is
        // fixed in length, so it is never the part somebody can shorten.
        if (identity.Length > MaximumIdentityLength)
        {
            throw new ArgumentException(
                $"An outgoing message requester identity may be at most {MaximumIdentityLength} characters long.",
                trimmedRuleName.Length >= trimmedRevision.Length ? nameof(ruleName) : nameof(revision));
        }

        return new OutgoingEmailRequester(OutgoingEmailOrigin.Rule, identity);
    }

    /// <summary>Names one act somebody asked for, by the key they supplied for it.</summary>
    /// <param name="invocationIdentity">The key that decides whether asking again is the same request: the same for a retry of one act and different for a second act.</param>
    /// <returns>A requester naming that act.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="invocationIdentity" /> is blank, carries a control character, or is longer than <see cref="MaximumIdentityLength" />.</exception>
    public static OutgoingEmailRequester Command(string invocationIdentity) => new(
        OutgoingEmailOrigin.Command,
        ValidIdentity(invocationIdentity, nameof(invocationIdentity)));

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
                "The outgoing message requester origin is not one this system declares.");
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
                $"An outgoing message requester identity may be at most {MaximumIdentityLength} characters long.",
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
                "An outgoing message requester identity cannot contain a control character.",
                parameterName);
        }

        return trimmedPart;
    }

    /// <inheritdoc />
    public override string ToString() => $"{this.Origin}:{this.Identity}";
}
