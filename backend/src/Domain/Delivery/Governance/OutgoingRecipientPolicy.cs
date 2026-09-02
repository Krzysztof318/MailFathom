// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Delivery.Governance;

/// <summary>Who this deployment may write to, as the operator's answer rather than any author's.</summary>
/// <remarks>
/// <para>
/// It is one policy for the whole installation rather than a setting per account, per rule, or per tool, because the
/// question it answers is about the instance: an operator who has said their instance only ever writes to their own
/// team has said it about everything the instance can do, and a bound written per caller would have to be written again
/// for the caller added next.
/// </para>
/// <para>
/// A deployment that names nobody restricts nobody, which is the default posture and the one an operator gets by
/// writing nothing. That is coherent rather than lax: sending is already off until an account is turned on, so an
/// instance nobody enabled writes to nobody whatever this policy says.
/// </para>
/// <para>
/// The two lists are read denied-first, so an address on both is refused. An operator who wrote it twice described the
/// narrower intent twice, and the stricter reading is the one that cannot cost them a message they did not mean to
/// send.
/// </para>
/// </remarks>
public sealed class OutgoingRecipientPolicy
{
    private readonly IReadOnlyList<OutgoingRecipientRule> allowed;
    private readonly IReadOnlyList<OutgoingRecipientRule> denied;

    private OutgoingRecipientPolicy(
        IReadOnlyList<OutgoingRecipientRule> allowed,
        IReadOnlyList<OutgoingRecipientRule> denied)
    {
        this.allowed = allowed;
        this.denied = denied;
    }

    /// <summary>Gets the policy of a deployment that named nobody, which admits every recipient.</summary>
    public static OutgoingRecipientPolicy Unrestricted { get; } = new([], []);

    /// <summary>Gets whether this policy could refuse anything at all.</summary>
    /// <remarks>It is what lets a caller say, at startup and in a report, that this instance may write to anybody.</remarks>
    public bool RestrictsRecipients => this.allowed.Count > 0 || this.denied.Count > 0;

    /// <summary>Builds a policy from the entries an operator wrote.</summary>
    /// <param name="allowed">The mailboxes and organizations this deployment may write to, or nothing to admit every recipient no denied entry names.</param>
    /// <param name="denied">The mailboxes and organizations this deployment may never write to.</param>
    /// <returns>The policy.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either list is <see langword="null" />.</exception>
    public static OutgoingRecipientPolicy Create(
        IReadOnlyList<OutgoingRecipientRule> allowed,
        IReadOnlyList<OutgoingRecipientRule> denied)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        ArgumentNullException.ThrowIfNull(denied);

        return allowed.Count == 0 && denied.Count == 0
            ? Unrestricted
            : new OutgoingRecipientPolicy([.. allowed], [.. denied]);
    }

    /// <summary>Judges one recipient of one message.</summary>
    /// <param name="recipient">The address the message would be offered to.</param>
    /// <returns>The reason this recipient is refused, or <see langword="null" /> when the policy admits them.</returns>
    /// <remarks>
    /// Every recipient of a message is judged, and a message naming one the policy refuses is refused whole rather than
    /// delivered to the rest: a message written to four people and sent to three is a message its author never wrote,
    /// and nothing downstream could tell the difference afterwards.
    /// </remarks>
    public OutgoingRecipientRefusalReason? Judge(EmailAddress recipient)
    {
        if (this.denied.Any(rule => rule.Matches(recipient)))
        {
            return OutgoingRecipientRefusalReason.DeniedByPolicy;
        }

        return this.allowed.Count > 0 && !this.allowed.Any(rule => rule.Matches(recipient))
            ? OutgoingRecipientRefusalReason.OutsideAllowedRecipients
            : null;
    }

    /// <summary>Judges everybody one message is addressed to, and reports the first refusal.</summary>
    /// <param name="recipients">The addresses the message would be offered to.</param>
    /// <returns>The reason the first refused recipient is refused, or <see langword="null" /> when the policy admits them all.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="recipients" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// It stops at the first refusal because the message is refused whole either way, which is the reason
    /// <see cref="Judge" /> states. Reading on would only name a second address the author learns about on the next
    /// attempt, and the reason reported carries no address at all.
    /// </remarks>
    public OutgoingRecipientRefusalReason? FindFirstRefusal(IReadOnlyList<OutgoingRecipient> recipients)
    {
        ArgumentNullException.ThrowIfNull(recipients);

        foreach (var recipient in recipients)
        {
            if (this.Judge(recipient.Address) is { } refusal)
            {
                return refusal;
            }
        }

        return null;
    }
}
