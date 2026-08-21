// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Paging;

namespace MailFathom.Application.Jobs.DeadLetters;

/// <summary>Marks where one page of dead letters ended, so the next page continues from it.</summary>
/// <remarks>
/// <para>
/// The reading is ordered newest first by the instant the job stopped, with the job's own identifier breaking a tie,
/// and this pairs those two values with a fingerprint of the filters the page was read under. Keyset rather than offset
/// because the set moves while it is being read: a worker dead-lettering another job, or an operator retrying one from
/// a second terminal, would otherwise shift a window and cause a job to be skipped or listed twice — which on this
/// surface means an operator acting on a list that no longer describes the queue.
/// </para>
/// <para>
/// It carries no secret and needs no signature: every value in it is one the caller already supplied or already
/// received. Encoding is about opacity rather than protection — a client that cannot read a cursor does not build one.
/// The encoded form itself is <see cref="KeysetCursorPayload" />'s, which every keyset cursor here shares.
/// </para>
/// </remarks>
public readonly record struct DeadLetteredJobCursor
{
    private DeadLetteredJobCursor(DateTimeOffset deadLetteredAt, JobId jobId, string filterFingerprint)
    {
        this.DeadLetteredAt = deadLetteredAt;
        this.JobId = jobId;
        this.FilterFingerprint = filterFingerprint;
    }

    /// <summary>Gets the instant the last job the page returned reached its state.</summary>
    public DateTimeOffset DeadLetteredAt { get; }

    /// <summary>Gets that job, which breaks a tie between two that stopped in one instant.</summary>
    public JobId JobId { get; }

    /// <summary>Gets the fingerprint of the filters this cursor was issued for.</summary>
    public string FilterFingerprint { get; }

    /// <summary>Creates the cursor that continues a walk after one position in the reading.</summary>
    /// <param name="deadLetteredAt">The instant the page ended on.</param>
    /// <param name="jobId">The job that stopped at that instant.</param>
    /// <param name="filterFingerprint">The fingerprint of the filters the page was read under.</param>
    /// <returns>The cursor.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filterFingerprint" /> is blank.</exception>
    public static DeadLetteredJobCursor After(
        DateTimeOffset deadLetteredAt,
        JobId jobId,
        string filterFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filterFingerprint);

        return new DeadLetteredJobCursor(deadLetteredAt, jobId, filterFingerprint);
    }

    /// <summary>Reads a cursor a caller presented.</summary>
    /// <param name="text">The encoded cursor, as a previous page returned it.</param>
    /// <param name="cursor">The decoded cursor when the text is one this version issued; otherwise <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the text decoded into a usable cursor; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// Every job this reading returns stopped at a known instant, so a payload carrying none names no boundary here and
    /// is refused. Whether a decoded cursor belongs to the current request is a separate question its
    /// <see cref="FilterFingerprint" /> answers, and one this method deliberately does not ask.
    /// </remarks>
    public static bool TryDecode(string? text, out DeadLetteredJobCursor? cursor)
    {
        cursor = null;

        if (!KeysetCursorPayload.TryDecode(text, out var payload) || payload.Position is not { } deadLetteredAt)
        {
            return false;
        }

        cursor = new DeadLetteredJobCursor(
            deadLetteredAt,
            JobId.Create(payload.Identity),
            payload.FilterFingerprint);

        return true;
    }

    /// <summary>Writes the cursor as the opaque string a caller presents to continue the walk.</summary>
    /// <returns>The encoded cursor.</returns>
    public string Encode() =>
        KeysetCursorPayload.At(this.DeadLetteredAt, this.JobId.Value, this.FilterFingerprint).Encode();
}
