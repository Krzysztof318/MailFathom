// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Storage.Reclamation;

/// <summary>Removes the stored mail nothing points at any more, one bounded run at a time.</summary>
/// <remarks>
/// <para>
/// The second of the two mechanisms that keep a bucket honest, and it answers the failure the first one cannot. A
/// deliberate erasure removes the object after the transaction that removed its row has committed; this removes what
/// that never reached — an object written by a unit of work that never committed, a draft revision a later one
/// superseded, and the object of any erasure whose removal the endpoint refused.
/// </para>
/// <para>
/// <b>An object nothing points at is mail nobody agreed to keep.</b> That is what makes this a privacy obligation
/// rather than a way of saving storage, and it is why the interval it runs on and the age below which it leaves an
/// object alone are settings the retention documentation states rather than housekeeping.
/// </para>
/// <para>
/// The implementation is bounded in three ways at once and none of them is optional: it pages the listing rather than
/// reading one, it stops after a configured number of objects so an attempt cannot hold a worker for a whole bucket,
/// and it observes cancellation between pages so a shutdown ends it where it stands. What it does not reach is
/// <see cref="ContentObjectReclamationRun.ResumeFrom" />, and the run after it starts there.
/// </para>
/// <para>
/// Running it twice over one object is the same as running it once, and two runs that overlap are safe: removing a key
/// nothing holds succeeds, so the worst an overlap costs is a second request. What neither can do is remove an object a
/// row points at, because the reference check is read after the listing and an object younger than the age floor is
/// left alone whatever the check says.
/// </para>
/// </remarks>
public interface IContentObjectReclamation
{
    /// <summary>Reclaims what one bounded run reaches, beginning where the previous one stopped.</summary>
    /// <param name="resumeFrom">The position a previous run answered with, or <see langword="null" /> to begin the listing.</param>
    /// <param name="cancellationToken">Ends the run between pages, leaving the rest to the run after it.</param>
    /// <returns>What the run examined, freed, and left behind.</returns>
    /// <remarks>
    /// Cancellation ends the run rather than raising, because being stopped is how an ordinary run ends: the executor
    /// cancels an attempt at its execution timeout and at shutdown, and neither says the work failed. What the run owes
    /// afterwards is the position the next one resumes from, which it cannot state by throwing.
    /// </remarks>
    Task<ContentObjectReclamationRun> ReclaimAsync(string? resumeFrom, CancellationToken cancellationToken);
}
