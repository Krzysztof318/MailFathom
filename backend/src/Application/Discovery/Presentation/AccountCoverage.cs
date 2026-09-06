// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Discovery.Presentation;

/// <summary>What one run read of one account: which account it was, how far back and forward the mail it found reached, and how current the local copy of that account was.</summary>
/// <remarks>
/// <para>
/// The reason a plan carries this at all is that a client reads a synchronized copy. An account nobody has reconciled
/// since yesterday means the answer may be missing what arrived since, and that is not a footnote — it may be the whole
/// reason the answer is wrong. Saying it as a value per account is what lets a client draw it beside the answer rather
/// than leaving a reader to remember which mailbox was behind.
/// </para>
/// <para>
/// The dates are the run's own reach rather than the mailbox's. They bound the mail this run actually drew on, so a
/// question about last month that found nothing older says so, and a reader can see that an answer about a decade of
/// correspondence rested on three weeks of it. They are absent together where the run found nothing in the account,
/// which is itself worth drawing: an account that was read and yielded nothing is not an account that was skipped.
/// </para>
/// <para>
/// The account is named by MailFathom's own configured identifier, which is what every other surface names an account
/// by and what a client already holds a display name for. No host, no user name, and no address is here: how the
/// deployment reaches a mailbox is the operator's business rather than a property of an answer.
/// </para>
/// </remarks>
public sealed record AccountCoverage
{
    /// <summary>Initializes what one run read of one account.</summary>
    /// <param name="account">MailFathom's own configured identifier for the account.</param>
    /// <param name="freshness">How current the local copy of the account was when the run read it.</param>
    /// <param name="earliestReceivedAt">When the oldest mail the run drew on from this account arrived, or <see langword="null" /> where it drew on none.</param>
    /// <param name="latestReceivedAt">When the newest mail the run drew on from this account arrived, or <see langword="null" /> where it drew on none.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="freshness" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="account" /> is the unspecified default, when only one of the two dates is present, or when the earliest is after the latest.</exception>
    public AccountCoverage(
        PresentationText account,
        PresentationFreshness freshness,
        DateTimeOffset? earliestReceivedAt,
        DateTimeOffset? latestReceivedAt)
    {
        ArgumentNullException.ThrowIfNull(freshness);
        PresentationRequirement.Specified(account, nameof(account));

        if (earliestReceivedAt is null != (latestReceivedAt is null))
        {
            throw new ArgumentException(
                "A run that drew on mail from an account reports both ends of what it drew on.",
                nameof(earliestReceivedAt));
        }

        if (earliestReceivedAt is { } earliest && latestReceivedAt is { } latest && earliest > latest)
        {
            throw new ArgumentException(
                "The oldest mail a run drew on cannot have arrived after the newest.",
                nameof(earliestReceivedAt));
        }

        this.Account = account;
        this.Freshness = freshness;
        this.EarliestReceivedAt = earliestReceivedAt;
        this.LatestReceivedAt = latestReceivedAt;
    }

    /// <summary>Gets MailFathom's own configured identifier for the account.</summary>
    public PresentationText Account { get; }

    /// <summary>Gets how current the local copy of the account was when the run read it.</summary>
    public PresentationFreshness Freshness { get; }

    /// <summary>Gets when the oldest mail the run drew on from this account arrived, or <see langword="null" /> where it drew on none.</summary>
    public DateTimeOffset? EarliestReceivedAt { get; }

    /// <summary>Gets when the newest mail the run drew on from this account arrived, or <see langword="null" /> where it drew on none.</summary>
    public DateTimeOffset? LatestReceivedAt { get; }
}
