// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using MailFathom.Domain.Access;

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>Bounds how much local storage stored mail content may occupy, for the deployment and for each owner.</summary>
/// <remarks>
/// <para>
/// The ceilings are one answer for one content store, so this is a single process-wide instance rather than a value
/// each run reads for itself. That is what makes them ceilings at all: several folder work units run at the same moment
/// by default, and runs that each measured the same occupancy before any of them wrote would each conclude they had
/// room and overshoot the configured limit between them by however much they were allowed to fetch.
/// </para>
/// <para>
/// A claim is therefore made against this instance before a payload is fetched and kept only for what was actually
/// stored. A claim nothing wrote — an abandoned fetch, a message that had left the folder, a rolled-back commit — is
/// released with its scope, so the level tracks what storage holds rather than what runs intended to put there.
/// </para>
/// <para>
/// The level is an estimate maintained between measurements rather than a reading taken per message, because measuring
/// costs a query and the whole purpose of a ceiling is to be approached rarely. <see cref="Observe" /> replaces it with
/// a fresh measurement and carries forward whatever was claimed while that measurement was being taken, so a run
/// starting mid-write neither loses those bytes nor counts them twice.
/// </para>
/// <para>
/// The two ceilings are counted in different quantities, and that is deliberate rather than an inconsistency. The
/// deployment's is what the operator's disk fills with, which only the database can report; an owner's is the payload
/// their mail holds, which is the only figure attributable to one person at all — a catalogue answers for a table and
/// never for a share of one. So the same payload counts once against a physical figure and once against a logical one,
/// and the two are never expected to agree.
/// </para>
/// </remarks>
public sealed class StoredContentCeiling
{
    private readonly ContentLevel deployment;
    private readonly long ownerCeilingBytes;
    private readonly ConcurrentDictionary<MailOwnerId, ContentLevel> ownerLevels = new();

    /// <summary>Initializes the ceilings for the process.</summary>
    /// <param name="ceilingBytes">The configured deployment ceiling, or <see langword="null" /> when storage is bounded only by the disk.</param>
    /// <param name="ownerCeilingBytes">The configured per-owner ceiling, or <see langword="null" /> when no owner is bounded separately.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either ceiling is not positive.</exception>
    public StoredContentCeiling(long? ceilingBytes, long? ownerCeilingBytes = null)
    {
        if (ceilingBytes is { } configured)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(configured);
        }

        if (ownerCeilingBytes is { } configuredForOwner)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(configuredForOwner);
        }

        this.deployment = new ContentLevel(ceilingBytes ?? long.MaxValue);
        this.ownerCeilingBytes = ownerCeilingBytes ?? long.MaxValue;
        this.IsConfigured = ceilingBytes.HasValue;
        this.IsConfiguredPerOwner = ownerCeilingBytes.HasValue;
    }

    /// <summary>Gets whether a deployment-wide ceiling is configured at all.</summary>
    public bool IsConfigured { get; }

    /// <summary>Gets whether a per-owner ceiling is configured at all.</summary>
    public bool IsConfiguredPerOwner { get; }

    /// <summary>Gets how much local storage the stored content is currently believed to occupy across the deployment.</summary>
    public long OccupiedBytes => this.deployment.OccupiedBytes;

    /// <summary>Gets the mark to capture before taking a measurement, so what is claimed during it is not lost.</summary>
    /// <param name="owner">The owner whose measurement is about to be taken beside the deployment's.</param>
    /// <returns>The marks both levels held before the measurement.</returns>
    public StoredContentMeasurementMark MarkBefore(MailOwnerId owner) =>
        new(this.deployment.ClaimMark, this.LevelOf(owner).ClaimMark);

    /// <summary>Gets how much of local storage one owner's stored content is currently believed to occupy.</summary>
    /// <param name="owner">The owner asked about.</param>
    /// <returns>The bytes that owner's payloads are believed to hold.</returns>
    public long OccupiedBytesFor(MailOwnerId owner) => this.LevelOf(owner).OccupiedBytes;

    /// <summary>Adopts a fresh measurement of what storage holds, for the deployment and for one owner.</summary>
    /// <param name="owner">The owner the second figure was measured for.</param>
    /// <param name="measuredBytes">What the content store reported occupying in total.</param>
    /// <param name="measuredOwnerBytes">What that owner's payloads were reported to hold.</param>
    /// <param name="mark">The value <see cref="MarkBefore" /> returned before the measurements were taken.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either measurement is negative.</exception>
    /// <remarks>
    /// Bytes claimed after the measurement began are added on top of it, because the measurement cannot describe writes
    /// that had not happened when it was taken. A measurement older than one already adopted is discarded rather than
    /// applied, so two runs measuring at once cannot make the newer reading lose to the slower query. Each level is
    /// judged against its own mark, so a stale owner reading cannot discard a fresh deployment one.
    /// </remarks>
    public void Observe(
        MailOwnerId owner,
        long measuredBytes,
        long measuredOwnerBytes,
        StoredContentMeasurementMark mark)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(measuredBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(measuredOwnerBytes);

        this.deployment.Observe(measuredBytes, mark.DeploymentClaimMark);
        this.LevelOf(owner).Observe(measuredOwnerBytes, mark.OwnerClaimMark);
    }

    /// <summary>Claims room for one payload of one owner, or reports which ceiling has none.</summary>
    /// <param name="owner">The owner whose mail the payload is.</param>
    /// <param name="bytes">What the payload is expected to occupy.</param>
    /// <returns>The claim, or the bound that refused it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="bytes" /> is not positive.</exception>
    /// <remarks>
    /// Both levels have to admit the payload, and the deployment's is taken first so that a refusal by it never leaves
    /// an owner charged for a payload nothing will fetch. The owner's refusal gives the deployment's claim straight
    /// back, which is what keeps one owner meeting their share from consuming the instance's.
    /// </remarks>
    public StoredContentClaimAttempt TryClaim(MailOwnerId owner, long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        if (!this.deployment.TryTake(bytes))
        {
            return StoredContentClaimAttempt.Refused(StoredContentBound.Deployment);
        }

        var ownerLevel = this.LevelOf(owner);
        if (!ownerLevel.TryTake(bytes))
        {
            this.deployment.Release(bytes);

            return StoredContentClaimAttempt.Refused(StoredContentBound.Owner);
        }

        return StoredContentClaimAttempt.Granted(new StoredContentClaim(this.deployment, ownerLevel, bytes));
    }

    private ContentLevel LevelOf(MailOwnerId owner) =>
        this.ownerLevels.GetOrAdd(owner, _ => new ContentLevel(this.ownerCeilingBytes));

    /// <summary>One population's believed occupancy, and the room it still has.</summary>
    /// <remarks>
    /// The deployment and each owner are the same arithmetic over different measurements, so they are one type used
    /// twice rather than two sets of fields that would drift. Each instance guards itself, because the two are taken in
    /// order and a lock spanning both would be held across the dictionary lookup between them.
    /// </remarks>
    internal sealed class ContentLevel(long ceilingBytes)
    {
        private readonly Lock gate = new();
        private long occupiedBytes;
        private long claimedBytesEver;
        private long adoptedClaimMark;

        public long OccupiedBytes
        {
            get
            {
                lock (this.gate)
                {
                    return this.occupiedBytes;
                }
            }
        }

        public long ClaimMark
        {
            get
            {
                lock (this.gate)
                {
                    return this.claimedBytesEver;
                }
            }
        }

        public void Observe(long measuredBytes, long claimMark)
        {
            lock (this.gate)
            {
                if (claimMark < this.adoptedClaimMark)
                {
                    return;
                }

                this.occupiedBytes = measuredBytes + (this.claimedBytesEver - claimMark);
                this.adoptedClaimMark = claimMark;
            }
        }

        public bool TryTake(long bytes)
        {
            lock (this.gate)
            {
                if (this.occupiedBytes + bytes > ceilingBytes)
                {
                    return false;
                }

                this.occupiedBytes += bytes;
                this.claimedBytesEver += bytes;

                return true;
            }
        }

        public void Release(long bytes)
        {
            if (bytes == 0)
            {
                return;
            }

            lock (this.gate)
            {
                this.occupiedBytes -= bytes;
                this.claimedBytesEver -= bytes;
            }
        }
    }
}
