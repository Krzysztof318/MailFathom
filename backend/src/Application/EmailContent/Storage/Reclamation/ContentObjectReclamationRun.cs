// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Storage.Reclamation;

/// <summary>What one bounded run of the reclamation examined, freed, and left behind.</summary>
/// <remarks>
/// <para>
/// Counts and volumes rather than keys. A key names one message, so a result an operator, a log, or a job record could
/// meet carries how much was reclaimed and never what — the one exception being <see cref="ResumeFrom" />, which names
/// a position in a listing because a run that stops has to say where the next one begins.
/// </para>
/// <para>
/// <see cref="ResumeFrom" /> is what makes the work resumable rather than merely repeatable. A run bounded by its own
/// object ceiling, by the attempt's execution timeout, or by a shutdown stops with mail still ahead of it, and the run
/// that carries the rest starts where this one stopped instead of listing the whole bucket again.
/// </para>
/// </remarks>
public sealed record ContentObjectReclamationRun
{
    /// <summary>Gets the run of a deployment that stores content in the database, which has no bucket to sweep.</summary>
    public static ContentObjectReclamationRun None { get; } = new();

    /// <summary>Gets how many objects the run looked at, whether or not it removed them.</summary>
    public int ExaminedCount { get; init; }

    /// <summary>Gets how many objects the run removed because no row pointed at them.</summary>
    public int ReclaimedCount { get; init; }

    /// <summary>Gets how many bytes those objects held.</summary>
    public long ReclaimedBytes { get; init; }

    /// <summary>Gets how many objects the endpoint refused to remove, each left for a later run.</summary>
    public int FailedCount { get; init; }

    /// <summary>Gets how old the oldest object nothing pointed at was, or <see cref="TimeSpan.Zero" /> when the run met none.</summary>
    /// <remarks>
    /// Measured before the object was removed, because the number an operator acts on is how far behind reclamation
    /// had fallen rather than what is left after it caught up.
    /// </remarks>
    public TimeSpan OldestOrphanAge { get; init; }

    /// <summary>Gets the position the next run continues the listing from, or <see langword="null" /> when this one reached its end.</summary>
    public string? ResumeFrom { get; init; }

    /// <summary>Gets whether objects the run did not reach are still ahead of it.</summary>
    public bool ObjectsRemain => this.ResumeFrom is not null;
}
