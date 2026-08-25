// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Infrastructure.ObjectStorage;

namespace MailFathom.Host.Configuration.Persistence;

/// <summary>Declares how often the object endpoint is swept for mail nothing points at, and what a sweep leaves alone.</summary>
/// <remarks>
/// <para>
/// <b>Both settings here are privacy-relevant rather than housekeeping.</b> A deliberate erasure removes the object
/// immediately after the transaction that removed its row commits, so in the ordinary case neither of these decides
/// anything. What they decide is the other case: the bound on how long mail whose record is already gone can still
/// exist as bytes, when a write never committed, when a draft revision was superseded, or when the endpoint refused
/// the removal. The retention documentation states that bound to an operator, and these are the two numbers it is
/// composed of.
/// </para>
/// <para>
/// Read once while the host composes, like the endpoint above it. A sweep interval that changed under a running
/// process would move the occasions of a schedule whose durable state records the last one it dispatched.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class ContentObjectReclamationOptions
{
    /// <summary>The configuration path this block is bound from, used to name a faulty setting.</summary>
    internal const string SectionPath = $"{ObjectStorageOptions.SectionPath}:{nameof(ObjectStorageOptions.Reclamation)}";

    /// <summary>The shortest age floor a deployment may configure.</summary>
    /// <remarks>
    /// An hour, and it is a correctness bound rather than a preference. An object is written before the unit of work
    /// that points at it commits, so a floor short enough to reach a write in flight would let a sweep remove mail that
    /// was in the middle of being stored — and the endpoint's clock is its own, so the floor absorbs the skew between
    /// the two as well. An hour is orders of magnitude above both and still far below any interval worth running.
    /// </remarks>
    internal static readonly TimeSpan MinimumObjectAgeFloor = TimeSpan.FromHours(1);

    /// <summary>The longest age floor a deployment may configure.</summary>
    /// <remarks>Thirty days, because the floor is also the promise made to a data subject about how long bytes can outlive their record, and a longer one would be a retention decision written in the wrong place.</remarks>
    internal static readonly TimeSpan MaximumObjectAgeFloor = TimeSpan.FromDays(30);

    /// <summary>Gets or sets the occasions a sweep is dispatched on, in the schedule syntax the job queue reads.</summary>
    /// <remarks>Every six hours by default, which keeps the bound on an orphan's life short without listing a whole bucket more often than a mailbox changes.</remarks>
    public string Schedule { get; set; } = "Every 06:00:00";

    /// <summary>Gets or sets the age below which an object is never reclaimed.</summary>
    public TimeSpan MinimumObjectAge { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Gets or sets how many objects one run may examine before handing the rest to the run after it.</summary>
    /// <remarks>A hundred thousand is well above the mailboxes this is deployed against, so an ordinary sweep is one run and the ceiling is what stops an unexpectedly large bucket from holding a worker.</remarks>
    public int MaximumObjectsPerRun { get; set; } = 100_000;

    /// <summary>Reports every reason the sweep could not be run as declared, by reading the declaration alone.</summary>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the declaration is usable.</returns>
    public IEnumerable<string> FindConfigurationErrors()
    {
        if (!JobRecurrence.TryParse(this.Schedule, out _, out var scheduleError))
        {
            yield return Error(nameof(this.Schedule), scheduleError!);
        }

        if (this.MinimumObjectAge < MinimumObjectAgeFloor || this.MinimumObjectAge > MaximumObjectAgeFloor)
        {
            yield return Error(
                nameof(this.MinimumObjectAge),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "is '{0}', which is outside the permitted range of {1} to {2}. Below the floor a sweep could remove an object whose unit of work has not committed yet, which is mail being lost rather than reclaimed.",
                    this.MinimumObjectAge,
                    MinimumObjectAgeFloor,
                    MaximumObjectAgeFloor));
        }

        if (this.MaximumObjectsPerRun < ContentObjectReclamationBounds.ListingPageSize)
        {
            yield return Error(
                nameof(this.MaximumObjectsPerRun),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "is {0}, which is below the {1} keys one listing request answers with. A run that may examine less than one page would hand on after every page and never reclaim anything.",
                    this.MaximumObjectsPerRun,
                    ContentObjectReclamationBounds.ListingPageSize));
        }
    }

    /// <summary>Builds the bounds one run of the sweep is held to.</summary>
    /// <returns>The bounds.</returns>
    /// <remarks>Called only after validation has passed, so what is left here is mapping rather than checking.</remarks>
    public ContentObjectReclamationBounds ToBounds() =>
        ContentObjectReclamationBounds.Create(this.MinimumObjectAge, this.MaximumObjectsPerRun);

    /// <summary>Builds the recurrence the sweep is dispatched on.</summary>
    /// <returns>The recurrence.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the declaration was not validated first.</exception>
    public JobRecurrence ToRecurrence() => JobRecurrence.TryParse(this.Schedule, out var recurrence, out _)
        ? recurrence!
        : throw new InvalidOperationException(
            "The reclamation schedule names no recurrence this system runs, which configuration validation reports before composition reaches this.");

    private static string Error(string propertyName, string detail) =>
        string.Format(CultureInfo.InvariantCulture, "{0}:{1} {2}", SectionPath, propertyName, detail);
}
