// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>What one sweep of the bucket may reach, and what it must leave alone.</summary>
/// <remarks>
/// <para>
/// The age floor is the whole difference between a reclamation and a race. An object is written before the unit of work
/// that points at it commits, so between the two there is an object no row names and that nothing is wrong with;
/// removing one would destroy mail the write was in the middle of storing. Nothing below the floor is touched whatever
/// the reference check says, which makes the floor a correctness bound rather than a tuning knob — and a
/// privacy-relevant setting besides, because it is part of how long mail whose record is gone can still exist as bytes.
/// </para>
/// <para>
/// The object ceiling bounds one attempt rather than the sweep. What it does not reach is handed to the segment after
/// it, so raising it makes each attempt longer and lowering it makes the chain longer; neither changes what is
/// eventually reclaimed.
/// </para>
/// </remarks>
public sealed record ContentObjectReclamationBounds
{
    /// <summary>How many keys one listing request asks for.</summary>
    /// <remarks>
    /// The endpoint's own maximum, which is what makes the number of round trips the smallest it can be for a given
    /// bucket. It is not configurable: a page is held in memory for as long as its objects are being decided about, and
    /// a thousand keys is both what S3 answers with by default and small enough that the page is never the largest
    /// thing this process holds.
    /// </remarks>
    public const int ListingPageSize = 1000;

    private ContentObjectReclamationBounds(TimeSpan minimumObjectAge, int maximumObjectsPerRun)
    {
        this.MinimumObjectAge = minimumObjectAge;
        this.MaximumObjectsPerRun = maximumObjectsPerRun;
    }

    /// <summary>Gets the age below which an object is left alone however few rows point at it.</summary>
    public TimeSpan MinimumObjectAge { get; }

    /// <summary>Gets how many objects one run may examine before handing the rest to the run after it.</summary>
    public int MaximumObjectsPerRun { get; }

    /// <summary>Composes the bounds a deployment's configuration describes.</summary>
    /// <param name="minimumObjectAge">The age below which an object is never reclaimed.</param>
    /// <param name="maximumObjectsPerRun">How many objects one run may examine.</param>
    /// <returns>The composed bounds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the age is not positive or the ceiling is below one page.</exception>
    /// <remarks>Called only after configuration validation has passed, so what is left here is the floor beneath which the type itself would be unusable.</remarks>
    public static ContentObjectReclamationBounds Create(TimeSpan minimumObjectAge, int maximumObjectsPerRun)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(minimumObjectAge, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumObjectsPerRun, ListingPageSize);

        return new ContentObjectReclamationBounds(minimumObjectAge, maximumObjectsPerRun);
    }
}
