// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;

namespace MailFathom.AI.Discovery;

/// <summary>Answers how current the data behind a set of sources was, from what the run read of the accounts they came from.</summary>
/// <remarks>
/// <para>
/// Freshness is a property of an account and a block rests on messages, so something has to reduce the accounts a
/// block's own sources were read from to one reading. It reduces to the worst rather than to the newest, because a
/// block resting on one current message and one that is behind is a block a reader should treat as behind — the whole
/// point of the value is that it says whether the answer may be missing what arrived since.
/// </para>
/// <para>
/// A block resting on nothing takes the run's own reading, over every account its scope reached. That is the honest
/// answer for an unsupported result: what a reader wants to know there is how current the mail that was searched was,
/// since a mailbox a day behind is one of the reasons a question goes unanswered.
/// </para>
/// </remarks>
internal sealed class DiscoveryFreshnessIndex
{
    private readonly Dictionary<string, PresentationFreshness> byAccount;
    private readonly PresentationFreshness acrossTheRun;

    /// <summary>Indexes what the run read of its accounts against the sources it declared.</summary>
    /// <param name="sources">The sources the run declared, each naming the account it was read from.</param>
    /// <param name="coverage">What the run read, one entry per account its scope reached.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    internal DiscoveryFreshnessIndex(
        IReadOnlyList<DiscoveryComposedSource> sources,
        IReadOnlyList<AccountCoverage> coverage)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(coverage);

        this.byAccount = coverage.ToDictionary(
            account => account.Account.Value,
            account => account.Freshness,
            StringComparer.Ordinal);

        this.acrossTheRun = Worst([.. coverage.Select(account => account.Freshness)]);
    }

    /// <summary>Says how current the data behind a set of sources was.</summary>
    /// <param name="sources">The sources the block rests on, which may be none.</param>
    /// <returns>The worst reading of the accounts those sources were read from, or the run's own where there are none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources" /> is <see langword="null" />.</exception>
    internal PresentationFreshness Of(IReadOnlyList<DiscoveryComposedSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (sources.Count is 0)
        {
            return this.acrossTheRun;
        }

        return Worst(
        [
            .. sources
                .Select(source => source.AccountId.Value)
                .Distinct(StringComparer.Ordinal)
                .Select(account => this.byAccount.GetValueOrDefault(account, PresentationFreshness.Unknown)),
        ]);
    }

    /// <summary>Reduces several readings to the one a reader should act on.</summary>
    /// <remarks>
    /// Known to be behind outranks everything, because it is the only one of the three that says the answer may be
    /// missing something. Nothing established outranks current, because a copy nobody has measured is not a copy known
    /// to be up to date. The instant carried is the oldest of the readings being reduced, so a block resting on two
    /// accounts reports the staler of the two observations rather than the more flattering one.
    /// </remarks>
    private static PresentationFreshness Worst(IReadOnlyList<PresentationFreshness> readings)
    {
        var behind = readings
            .Where(reading => reading.Staleness is PresentationStaleness.Stale)
            .ToArray();

        if (behind.Length != 0)
        {
            return PresentationFreshness.StaleSince(behind.Min(reading => reading.ObservedAt!.Value));
        }

        var current = readings
            .Where(reading => reading.Staleness is PresentationStaleness.Current)
            .ToArray();

        return current.Length == readings.Count && current.Length != 0
            ? PresentationFreshness.CurrentAt(current.Min(reading => reading.ObservedAt!.Value))
            : PresentationFreshness.Unknown;
    }
}
