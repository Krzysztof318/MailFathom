// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Discovery.Presentation;

/// <summary>How current the data behind something in a plan was, and when that was established.</summary>
/// <remarks>
/// <para>
/// The timestamp and the verdict travel together because either alone misleads. A timestamp with no verdict leaves the
/// reader to decide whether four hours is stale, which depends on the mailbox rather than on the clock; a verdict with
/// no timestamp cannot be re-judged later by a screen that has been open since.
/// </para>
/// <para>
/// The constructor is what holds the two members to the combinations that mean something, and it is also the one
/// deserialization uses — so a plan arriving from a producer this deployment does not control is held to the same rule
/// as one composed in process. The named members below are how a producer states a freshness readably.
/// </para>
/// </remarks>
public sealed record PresentationFreshness
{
    /// <summary>Initializes the freshness of data behind a block.</summary>
    /// <param name="staleness">How far behind the mail server the data may be.</param>
    /// <param name="observedAt">When the local copy was last established against the mail server.</param>
    /// <exception cref="ArgumentException">Thrown when an established verdict carries no timestamp, or an unknown one carries a timestamp.</exception>
    public PresentationFreshness(PresentationStaleness staleness, DateTimeOffset? observedAt)
    {
        if (staleness is PresentationStaleness.Unknown && observedAt is not null)
        {
            throw new ArgumentException(
                "Freshness nothing established carries no observation time.",
                nameof(observedAt));
        }

        if (staleness is not PresentationStaleness.Unknown && observedAt is null)
        {
            throw new ArgumentException(
                "A stated freshness carries the time the local copy was established.",
                nameof(observedAt));
        }

        this.Staleness = staleness;
        this.ObservedAt = observedAt;
    }

    /// <summary>Gets how far behind the mail server the data may be.</summary>
    public PresentationStaleness Staleness { get; }

    /// <summary>Gets when the local copy was last established against the mail server, or <see langword="null" /> where nothing established it.</summary>
    public DateTimeOffset? ObservedAt { get; }

    /// <summary>Gets the freshness of data nothing established, which claims neither currency nor staleness.</summary>
    public static PresentationFreshness Unknown { get; } = new(PresentationStaleness.Unknown, observedAt: null);

    /// <summary>States that the local copy was current when it was read.</summary>
    /// <param name="observedAt">When the local copy was last established against the mail server.</param>
    /// <returns>The freshness of data known to be current.</returns>
    public static PresentationFreshness CurrentAt(DateTimeOffset observedAt) =>
        new(PresentationStaleness.Current, observedAt);

    /// <summary>States that the local copy is known to be behind the mail server.</summary>
    /// <param name="observedAt">When the local copy was last established against the mail server.</param>
    /// <returns>The freshness of data known to be behind.</returns>
    public static PresentationFreshness StaleSince(DateTimeOffset observedAt) =>
        new(PresentationStaleness.Stale, observedAt);
}
