// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

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
/// A claim names the account, and the per-user figure is the largest sum over the users assigned that account, each
/// user's sum being over every account they are assigned. That is what ADR 0014 asks of a shared mailbox: it counts in
/// full against everybody assigned it, so the user closest to their ceiling decides, and a mailbox assigned to nobody
/// is bounded by the deployment alone.
/// </para>
/// <para>
/// The claims are one dictionary rather than one per account, which is what lets a test hold two mailboxes' claims
/// against each other exactly as one deployment's replicas hold theirs. Nothing here expires on a clock: a test that
/// needs an abandoned claim states it with <see cref="ExpireEveryClaim" />, so the suite stays free of a wall clock.
/// </para>
/// </remarks>
internal sealed class InMemoryStoredContentClaimStore : IStoredContentClaimStore
{
    private readonly Dictionary<Guid, ReservedRoom> claims = [];
    private readonly Dictionary<MailAccountId, long> occupiedBytesByAccount = [];
    private readonly StubMailAccountAssignments assignments = new();

    /// <summary>Gets or sets what local content storage is reported to occupy across the deployment.</summary>
    public long OccupiedBytes { get; set; }

    /// <summary>Gets how many claims are still binding, which is what says a released claim was given back.</summary>
    /// <remarks>
    /// An expired claim is not one of them, for the same reason it reserves nothing: the persisted store sweeps
    /// expired rows inside its claim statement, so a deployment holds no such row and neither does this.
    /// </remarks>
    public int OutstandingClaimCount => this.claims.Values.Count(room => !room.HasExpired);

    /// <summary>Gets how much every unexpired claim reserves between them.</summary>
    public long ReservedBytes => this.claims.Values.Where(room => !room.HasExpired).Sum(room => room.Bytes);

    /// <summary>Gets how many times a release was asked for, whether or not it met a claim.</summary>
    /// <remarks>
    /// Counted rather than inferred from what is left, because removing a claim is idempotent: a claim released twice
    /// and a claim released once leave the same store, so nothing about the outstanding count can tell a caller that
    /// releases once from one that does not.
    /// </remarks>
    public int ReleaseCount { get; private set; }

    /// <summary>States what one mailbox's stored content occupies before the test begins.</summary>
    /// <param name="account">The mailbox.</param>
    /// <param name="occupiedBytes">What its payloads hold.</param>
    /// <returns>This store, so arrangements read as one expression.</returns>
    public InMemoryStoredContentClaimStore Holding(MailAccountId account, long occupiedBytes)
    {
        this.occupiedBytesByAccount[account] = occupiedBytes;

        return this;
    }

    /// <summary>States which mailboxes one user is assigned, which is what their own ceiling is summed over.</summary>
    /// <param name="user">The user.</param>
    /// <param name="accounts">The mailboxes assigned to them.</param>
    /// <returns>This store, so arrangements read as one expression.</returns>
    /// <remarks>
    /// A test that says nothing about assignments is bounded by the deployment's ceiling alone, which is the same
    /// answer a deployment gives for a mailbox nobody has been assigned.
    /// </remarks>
    public InMemoryStoredContentClaimStore Assigning(MailUserId user, params MailAccountId[] accounts)
    {
        this.assignments.Assigning(user, accounts);

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
        MailAccountId account,
        long bytes,
        StoredContentCeilings ceilings,
        TimeSpan claimLifetime,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(claimLifetime, TimeSpan.Zero);
        RequireNamedAccount(account);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ceilings.BoundsAnything)
        {
            return Task.FromResult(StoredContentClaimRecord.Unbounded);
        }

        var binding = this.claims.Values.Where(room => !room.HasExpired).ToArray();
        var deploymentHeld = this.OccupiedBytes + binding.Sum(room => room.Bytes);
        var userHeld = this.assignments.UsersAssignedTo(account)
            .Select(reader => this.assignments.AccountsAssignedTo(reader).Sum(assigned =>
                this.occupiedBytesByAccount.GetValueOrDefault(assigned)
                + binding.Where(room => room.Account == assigned).Sum(room => room.Bytes)))
            .DefaultIfEmpty(0L)
            .Max();

        if (deploymentHeld > (ceilings.DeploymentBytes ?? long.MaxValue) - bytes)
        {
            return Task.FromResult(new StoredContentClaimRecord(ClaimId: null, StoredContentBound.Deployment));
        }

        if (userHeld > (ceilings.UserBytes ?? long.MaxValue) - bytes)
        {
            return Task.FromResult(new StoredContentClaimRecord(ClaimId: null, StoredContentBound.User));
        }

        var claimId = Guid.CreateVersion7();
        this.claims[claimId] = new ReservedRoom(account, bytes, HasExpired: false);

        return Task.FromResult(new StoredContentClaimRecord(claimId, StoredContentBound.None));
    }

    /// <inheritdoc />
    public Task ReleaseAsync(Guid claimId, CancellationToken cancellationToken)
    {
        this.ReleaseCount++;
        this.claims.Remove(claimId);

        return Task.CompletedTask;
    }

    private static void RequireNamedAccount(MailAccountId account)
    {
        if (string.IsNullOrWhiteSpace(account.Value))
        {
            throw new ArgumentException(
                "A stored-content claim is reserved against a named account, so an account naming nothing cannot claim.",
                nameof(account));
        }
    }

    private sealed record ReservedRoom(MailAccountId Account, long Bytes, bool HasExpired);
}
