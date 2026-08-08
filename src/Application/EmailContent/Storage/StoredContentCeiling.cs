// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>Bounds how much local storage the stored mail content of the whole deployment may occupy.</summary>
/// <remarks>
/// <para>
/// The ceiling is one answer for one content store, so it is a single process-wide instance rather than a value each
/// run reads for itself. That is what makes it a ceiling at all: several folder work units run at the same moment by
/// default, and runs that each measured the same occupancy before any of them wrote would each conclude they had room
/// and overshoot the configured limit between them by however much they were allowed to fetch.
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
/// </remarks>
public sealed class StoredContentCeiling
{
    private readonly long ceilingBytes;
    private readonly Lock gate = new();
    private long occupiedBytes;
    private long claimedBytesEver;
    private long adoptedClaimMark;

    /// <summary>Initializes the ceiling for the process.</summary>
    /// <param name="ceilingBytes">The configured ceiling, or <see langword="null" /> when storage is bounded only by the disk.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="ceilingBytes" /> is not positive.</exception>
    public StoredContentCeiling(long? ceilingBytes)
    {
        if (ceilingBytes is { } configured)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(configured);
        }

        this.ceilingBytes = ceilingBytes ?? long.MaxValue;
        this.IsConfigured = ceilingBytes.HasValue;
    }

    /// <summary>Gets whether a ceiling is configured at all.</summary>
    public bool IsConfigured { get; }

    /// <summary>Gets how much local storage the stored content is currently believed to occupy.</summary>
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

    /// <summary>Gets the mark to capture before taking a measurement, so what is claimed during it is not lost.</summary>
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

    /// <summary>Adopts a fresh measurement of what storage holds.</summary>
    /// <param name="measuredBytes">What the content store reported occupying.</param>
    /// <param name="claimMark">The value <see cref="ClaimMark" /> held before the measurement was taken.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="measuredBytes" /> is negative.</exception>
    /// <remarks>
    /// Bytes claimed after the measurement began are added on top of it, because the measurement cannot describe writes
    /// that had not happened when it was taken. A measurement older than one already adopted is discarded rather than
    /// applied, so two runs measuring at once cannot make the newer reading lose to the slower query.
    /// </remarks>
    public void Observe(long measuredBytes, long claimMark)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(measuredBytes);

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

    /// <summary>Claims room for one payload, or reports that the ceiling has none.</summary>
    /// <param name="bytes">What the payload is expected to occupy.</param>
    /// <returns>The claim, which must be disposed; or <see langword="null" /> when the ceiling has no room for it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="bytes" /> is not positive.</exception>
    public StoredContentClaim? TryClaim(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        lock (this.gate)
        {
            if (this.occupiedBytes + bytes > this.ceilingBytes)
            {
                return null;
            }

            this.occupiedBytes += bytes;
            this.claimedBytesEver += bytes;

            return new StoredContentClaim(this, bytes);
        }
    }

    /// <summary>Gives back the part of a claim that never reached storage.</summary>
    internal void Release(long bytes)
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
