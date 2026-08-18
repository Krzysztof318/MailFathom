// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Composition;

namespace MailFathom.Application.Mail.Delivery.Addressing;

/// <summary>Carries the recipients a message is addressed to, or the reason one of them addressed nobody.</summary>
/// <remarks>
/// <para>
/// A refusal is a result rather than an exception for the reason every other refusal on this path is: the caller acts on
/// it directly, nothing has been written down, and the send simply does not exist.
/// </para>
/// <para>
/// One unresolved recipient refuses the whole message rather than dropping that person from it. A message delivered to
/// everybody but the person whose name was ambiguous is a message whose author was told it was sent, and the one reader
/// they cared about never receives it.
/// </para>
/// </remarks>
public sealed record RecipientResolution
{
    private RecipientResolution(
        IReadOnlyList<AuthoredEmailRecipient> recipients,
        RecipientResolutionRefusal? refusal)
    {
        this.Recipients = recipients;
        this.Refusal = refusal;
    }

    /// <summary>Gets the recipients the message is composed with, which is empty when the resolution was refused.</summary>
    /// <remarks>
    /// A refusal is what a caller checks rather than an empty list, because a message addressed to nobody is refused by
    /// the composition on its own terms and would otherwise arrive there as an author who named nobody.
    /// </remarks>
    public IReadOnlyList<AuthoredEmailRecipient> Recipients { get; }

    /// <summary>Gets why no recipients were resolved, or <see langword="null" /> when they were.</summary>
    public RecipientResolutionRefusal? Refusal { get; }

    /// <summary>Gets whether every recipient the author named resolved to an address.</summary>
    public bool IsResolved => this.Refusal is null;

    /// <summary>Reports every recipient the author named, in the order they named them.</summary>
    /// <param name="recipients">The resolved recipients.</param>
    /// <returns>A resolved result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="recipients" /> is <see langword="null" />.</exception>
    public static RecipientResolution Resolved(IReadOnlyList<AuthoredEmailRecipient> recipients)
    {
        ArgumentNullException.ThrowIfNull(recipients);

        return new RecipientResolution(recipients, refusal: null);
    }

    /// <summary>Reports that a recipient addressed nobody, and why.</summary>
    /// <param name="reason">What stopped it.</param>
    /// <param name="matchedContactCount">How many contacts carried the name, when the reason is an ambiguous one.</param>
    /// <returns>A refused result.</returns>
    public static RecipientResolution Refused(
        RecipientResolutionRefusalReason reason,
        int? matchedContactCount = null) =>
        new([], new RecipientResolutionRefusal(reason, matchedContactCount));
}
