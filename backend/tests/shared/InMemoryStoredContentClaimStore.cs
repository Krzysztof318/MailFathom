// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Domain.Access;

namespace MailFathom.TestSupport;

/// <summary>Holds what storage occupies and what every claim reserves in memory, so a ceiling has both figures to read.</summary>
/// <remarks>
/// <para>
/// Hand-written rather than substituted, because a ceiling test arranges an occupancy and then asserts against what the
/// ceiling did with it: a substitute would answer from a script and the assertion would be about the script. It does
/// the same arithmetic the persisted store's statement does — an occupancy plus every unexpired claim, judged against
/// each ceiling, with the deployment's refusal reported in preference to the user's — so a claim that would be refused
/// in a deployment is refused here.
/// </para>
/// <para>
/// The claims are one dictionary rather than one per user, which is what lets a test hold two users' claims against
/// each other exactly as one deployment's replicas hold theirs. Nothing here expires on a clock: a test that needs an
/// abandoned claim states it with <see cref="ExpireEveryClaim" />, so the suite stays free of a wall clock.
/// </para>
/// </remarks>
internal sealed class InMemoryStoredContentClaimStore : IStoredContentClaimStore
{
    private readonly Dictionary<Guid, ReservedRoom> claims = [];
    private readonly Dictionary<MailUserId, long> occupiedBytesByUser = [];

    /// <summary>Gets or sets what local content storage is reported to occupy across the deployment.</summary>
    public long OccupiedBytes { get; set; }

    /// <summary>Gets how many claims are still binding, which is what says a released claim was given back.</summary>
    public int OutstandingClaimCount => this.claims.Count;

    /// <summary>Gets how much every unexpired claim reserves between them.</summary>
    public long ReservedBytes => this.claims.Values.Where(room => !room.HasExpired).Sum(room => room.Bytes);

    /// <summary>Gets how many times a release was asked for, whether or not it met a claim.</summary>
    /// <remarks>
    /// Counted rather than inferred from what is left, because removing a claim is idempotent: a claim released twice
    /// and a claim released once leave the same store, so nothing about the outstanding count can tell a caller that
    /// releases once from one that does not.
    /// </remarks>
    public int ReleaseCount { get; private set; }

    /// <summary>States what one user's stored content occupies before the test begins.</summary>
    /// <param name="user">The user.</param>
    /// <param name="occupiedBytes">What their payloads hold.</param>
    /// <returns>This store, so arrangements read as one expression.</returns>
    public InMemoryStoredContentClaimStore Holding(MailUserId user, long occupiedBytes)
    {
        this.occupiedBytesByUser[user] = occupiedBytes;

        return this;
    }

    /// <summary>States what local content storage occupies before the test begins.</summary>
    /// <param name="occupiedBytes">What the deployment's stored content holds.</param>
    /// <returns>This store, so arrangements read as one expression.</returns>
    public InMemoryStoredContentClaimStore HoldingInTotal(long occupiedBytes)
    {
        this.OccupiedBytes = occupiedBytes;

        return this;
    }

    /// <summary>Stops every claim binding without releasing one, which is what a replica that died leaves behind.</summary>
    public void ExpireEveryClaim()
    {
        foreach (var claimId in this.claims.Keys.ToArray())
        {
            this.claims[claimId] = this.claims[claimId] with { HasExpired = true };
        }
    }

    /// <inheritdoc />
    public Task<StoredContentClaimRecord> ClaimAsync(
        MailUserId user,
        long bytes,
        StoredContentCeilings ceilings,
        TimeSpan claimLifetime,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(claimLifetime, TimeSpan.Zero);
        RequireNamedUser(user);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ceilings.BoundsAnything)
        {
            return Task.FromResult(StoredContentClaimRecord.Unbounded);
        }

        var binding = this.claims.Values.Where(room => !room.HasExpired).ToArray();
        var deploymentHeld = this.OccupiedBytes + binding.Sum(room => room.Bytes);
        var userHeld = this.occupiedBytesByUser.GetValueOrDefault(user)
            + binding.Where(room => room.User == user).Sum(room => room.Bytes);

        if (deploymentHeld > (ceilings.DeploymentBytes ?? long.MaxValue) - bytes)
        {
            return Task.FromResult(new StoredContentClaimRecord(ClaimId: null, StoredContentBound.Deployment));
        }

        if (userHeld > (ceilings.UserBytes ?? long.MaxValue) - bytes)
        {
            return Task.FromResult(new StoredContentClaimRecord(ClaimId: null, StoredContentBound.User));
        }

        var claimId = Guid.CreateVersion7();
        this.claims[claimId] = new ReservedRoom(user, bytes, HasExpired: false);

        return Task.FromResult(new StoredContentClaimRecord(claimId, StoredContentBound.None));
    }

    /// <inheritdoc />
    public Task ReleaseAsync(Guid claimId, CancellationToken cancellationToken)
    {
        this.ReleaseCount++;
        this.claims.Remove(claimId);

        return Task.CompletedTask;
    }

    private static void RequireNamedUser(MailUserId user)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException(
                "A stored-content claim is reserved against a named user's ceiling, so a user naming nobody cannot claim.",
                nameof(user));
        }
    }

    private sealed record ReservedRoom(MailUserId User, long Bytes, bool HasExpired);
}
