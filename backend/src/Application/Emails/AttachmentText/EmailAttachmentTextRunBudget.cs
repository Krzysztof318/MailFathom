// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.AttachmentText;

/// <summary>How many octets one account run has left to read out of attachments.</summary>
/// <remarks>
/// <para>
/// One of these lives for one pass and is spent down as the pass walks. It is mutable because it is a running total
/// rather than a setting — the ceiling it starts at is
/// <see cref="EmailAttachmentTextBounds.MaxInputOctetsPerAccountRun" />, which is configuration and immutable like every
/// other bound.
/// </para>
/// <para>
/// It is deliberately not shared between accounts. The synchronization run already gives each account its own isolation
/// and its own slot, so a mailbox full of large attachments delays nobody else's run, and a budget shared across
/// accounts would let whichever account ran first spend everybody's.
/// </para>
/// </remarks>
public sealed class EmailAttachmentTextRunBudget
{

    /// <summary>Opens a budget for one pass.</summary>
    /// <param name="maxOctets">The octets this pass may read across every message it reaches.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxOctets" /> is negative.</exception>
    public EmailAttachmentTextRunBudget(long maxOctets)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxOctets);

        this.RemainingOctets = maxOctets;
    }

    /// <summary>Gets whether the pass has nothing left to spend.</summary>
    public bool IsExhausted => this.RemainingOctets <= 0;

    /// <summary>Gets how many octets are still available to read.</summary>
    public long RemainingOctets { get; private set; }

    /// <summary>Takes the octets one attachment would cost, when the pass can still afford them.</summary>
    /// <param name="octets">What reading the attachment would read.</param>
    /// <returns><see langword="true" /> when the budget was charged; <see langword="false" /> when it could not be.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="octets" /> is negative.</exception>
    /// <remarks>
    /// Reserved before the read rather than counted after it, so an attachment is never opened on a budget that could
    /// not have paid for it. An attachment larger than the whole remaining budget is refused rather than admitted for
    /// being the first one asked, which is what keeps the ceiling a ceiling.
    /// </remarks>
    public bool TryReserve(long octets)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(octets);

        if (octets > this.RemainingOctets)
        {
            this.RemainingOctets = 0;

            return false;
        }

        this.RemainingOctets -= octets;

        return true;
    }
}
