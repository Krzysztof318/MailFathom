// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Access;

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>Bounds how much local storage stored mail content may occupy, for the deployment and for each user.</summary>
/// <remarks>
/// <para>
/// The ceilings are one answer for one content store, and one deployment may run several replicas writing into it, so
/// the reservation a claim holds lives in that store rather than in this process. That is what makes them ceilings at
/// all: several folder runs write at the same moment — in one process and, above one replica, in several — and runs
/// that each measured the same occupancy before any of them wrote would each conclude they had room and overshoot the
/// configured limit between them by however much they were allowed to fetch.
/// </para>
/// <para>
/// A claim is therefore made against <see cref="IStoredContentClaimStore" /> before a payload is fetched and given back
/// once the payload has either reached storage or been abandoned. A claim nothing wrote — an abandoned fetch, a message
/// that had left the folder, a rolled-back commit — is released with its scope, and one whose holder stopped answering
/// expires, so what binds tracks what storage holds or is about to rather than what runs intended to put there.
/// </para>
/// <para>
/// Nothing is remembered between claims, which is what makes this scoped rather than process-wide: the occupancy and
/// the outstanding claims are read inside the statement that takes the claim, so there is no level to carry forward and
/// no measurement to grow stale. A deployment that declared neither ceiling takes no claim and reaches no database at
/// all, which is what keeps the cost of a bound on the deployments that asked for one.
/// </para>
/// <para>
/// The two ceilings are counted in different quantities, and that is deliberate rather than an inconsistency. The
/// deployment's is what the operator's disk fills with, which only the database can report; a user's is the payload
/// their mail holds, which is the only figure attributable to one person at all — a catalogue answers for a table and
/// never for a share of one. So the same payload counts once against a physical figure and once against a logical one,
/// and the two are never expected to agree.
/// </para>
/// </remarks>
public sealed class StoredContentCeiling
{
    /// <summary>How long a claim binds before it expires unreleased.</summary>
    /// <remarks>
    /// A claim covers one message's fetch and its store, so this is what one message may take end to end and not what
    /// a run may. It is a constant rather than a setting because what it bounds is a failure — a replica that stopped
    /// answering while holding room — and the only judgement in it is that a message which has taken five minutes is a
    /// message whose holder is gone. Too short releases room a live holder is still about to use, which the ceiling
    /// then admits somebody else into; too long leaves a dead replica's bytes reserved for that much longer. Neither
    /// loses mail, which is why it is not worth an operator's attention.
    /// </remarks>
    public static readonly TimeSpan ClaimLifetime = TimeSpan.FromMinutes(5);

    private readonly IStoredContentClaimStore claimStore;
    private readonly StoredContentCeilings ceilings;

    /// <summary>Initializes the ceilings this deployment declared.</summary>
    /// <param name="claimStore">Holds what every replica has claimed and decides whether one more fits.</param>
    /// <param name="ceilingBytes">The configured deployment ceiling, or <see langword="null" /> when storage is bounded only by the disk.</param>
    /// <param name="userCeilingBytes">The configured per-user ceiling, or <see langword="null" /> when no user is bounded separately.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="claimStore" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either ceiling is not positive.</exception>
    public StoredContentCeiling(IStoredContentClaimStore claimStore, long? ceilingBytes, long? userCeilingBytes = null)
    {
        ArgumentNullException.ThrowIfNull(claimStore);

        if (ceilingBytes is { } configured)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(configured);
        }

        if (userCeilingBytes is { } configuredForUser)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(configuredForUser);
        }

        this.claimStore = claimStore;
        this.ceilings = new StoredContentCeilings(ceilingBytes, userCeilingBytes);
    }

    /// <summary>Gets whether a deployment-wide ceiling is configured at all.</summary>
    public bool IsConfigured => this.ceilings.DeploymentBytes.HasValue;

    /// <summary>Gets whether a per-user ceiling is configured at all.</summary>
    public bool IsConfiguredPerUser => this.ceilings.UserBytes.HasValue;

    /// <summary>Claims room for one payload of one user, or reports which ceiling has none.</summary>
    /// <param name="user">The user whose mail the payload is.</param>
    /// <param name="bytes">What the payload is expected to occupy.</param>
    /// <param name="cancellationToken">Cancels the claim.</param>
    /// <returns>The claim, or the bound that refused it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="bytes" /> is not positive.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <remarks>
    /// Both ceilings have to admit the payload, and the store admits them together rather than one after the other:
    /// charging one and then discovering the other refuses would leave a population reserved for a payload nothing will
    /// fetch, which is precisely what an expiry is a last resort against rather than a routine outcome.
    /// </remarks>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the claim passes to the caller through the granted attempt, which disposes it once the payload has reached storage or been abandoned.")]
    public async Task<StoredContentClaimAttempt> TryClaimAsync(
        MailUserId user,
        long bytes,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        RequireNamedUser(user);

        // A deployment that bounds neither population has nothing for a claim to be visible to, so it reaches no
        // database: the grant is real and its release is a no-op, which is what keeps the whole mechanism off the
        // ingest path of a deployment that declared no ceiling.
        if (!this.ceilings.BoundsAnything)
        {
            return StoredContentClaimAttempt.Granted(
                new StoredContentClaim(this.claimStore, StoredContentClaimRecord.Unbounded.ClaimId, bytes));
        }

        var record = await this.claimStore.ClaimAsync(
            user,
            bytes,
            this.ceilings,
            ClaimLifetime,
            cancellationToken);

        return record.IsGranted
            ? StoredContentClaimAttempt.Granted(new StoredContentClaim(this.claimStore, record.ClaimId, bytes))
            : StoredContentClaimAttempt.Refused(record.ReachedBound);
    }

    /// <summary>Refuses a user naming nobody, which is the one argument no ceiling can be measured or claimed for.</summary>
    /// <remarks>
    /// A user naming nobody would otherwise be claimed against as a population of its own: bytes would be reserved and
    /// admitted against a ceiling for "nobody", which reads as a working bound right up until somebody asks whose it
    /// was. The spend gate refuses the same argument for the same reason, and this is the storage half of that rule.
    /// </remarks>
    private static void RequireNamedUser(MailUserId user)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException(
                "A stored-content ceiling is measured and claimed for a named user, so a user naming nobody cannot be bounded.",
                nameof(user));
        }
    }
}
