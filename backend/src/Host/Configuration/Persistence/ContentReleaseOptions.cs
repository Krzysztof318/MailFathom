// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Application.EmailContent.Release;

namespace MailFathom.Host.Configuration.Persistence;

/// <summary>Bounds the one irreversible step of the move: freeing the database copies the copy left behind.</summary>
/// <remarks>
/// <para>
/// Nothing here releases anything, and there is no setting that does. A release is an operator's request each time,
/// through the administrative endpoint, because it removes the last copy of a message this deployment holds outside the
/// bucket — and no elapsing interval, finished move, or restarted host may take that decision for them.
/// </para>
/// <para>
/// What these two settle is how much one request frees, and how recently the copy it frees may have been verified. The
/// second is the answer to an operator who discovers a problem a week after switching: a deployment that states a week
/// here cannot free anything it has not been reading from the bucket for a week, however emphatically somebody asks.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class ContentReleaseOptions
{
    /// <summary>The configuration path this block is bound from, used to name a faulty setting.</summary>
    internal const string SectionPath = $"{ContentStorageOptions.SectionName}:{nameof(ContentStorageOptions.Release)}";

    /// <summary>The greatest number of retained copies one request may free.</summary>
    /// <remarks>
    /// A bound on the request rather than on the operation, because the command repeats it. Past a couple of thousand
    /// rows a single statement stops being the bounded, interruptible thing the release is meant to be — and the reason
    /// to raise it at all is a release of a very large mailbox, which is exactly the case where an operator most wants
    /// each step to be small enough to stop.
    /// </remarks>
    internal const int MaximumPayloadsPerBatch = 2000;

    /// <summary>The longest hold a deployment may state between an object being verified and a release freeing its copy.</summary>
    /// <remarks>
    /// A year, because the hold answers an operator who discovers a problem some time after switching and nobody
    /// discovers one a year later from bytes they have not read since. What the ceiling is really for is the value
    /// nobody meant: an interval wide enough to put the cutoff before <see cref="DateTimeOffset.MinValue" /> throws when
    /// a release computes it rather than when the host reads it, so a mistyped duration would be found by an operator
    /// asking to free copies rather than by the deployment refusing to start. Holding a copy indefinitely needs no
    /// setting at all — nothing frees one until somebody asks.
    /// </remarks>
    internal static readonly TimeSpan MaximumSafetyInterval = TimeSpan.FromDays(365);

    /// <summary>Gets or sets how long a retained copy is held after its object was verified, before any release frees it.</summary>
    /// <remarks>
    /// Zero by default, which is not the same as freeing anything on its own: the default hold is the operator's own
    /// decision, and nothing is freed until they ask for it. What a positive value adds is a floor beneath that
    /// decision.
    /// </remarks>
    public TimeSpan SafetyInterval { get; set; } = TimeSpan.Zero;

    /// <summary>Gets or sets how many retained copies one request frees before it answers.</summary>
    public int PayloadsPerBatch { get; set; } = 200;

    /// <summary>Reports every reason these bounds could not be used, by reading the declaration alone.</summary>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the declaration is usable.</returns>
    public IEnumerable<string> FindConfigurationErrors()
    {
        if (this.SafetyInterval < TimeSpan.Zero || this.SafetyInterval > MaximumSafetyInterval)
        {
            yield return Error(
                nameof(this.SafetyInterval),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "is '{0}', which is outside the permitted range of nothing at all to '{1}'. State how long a copy is held after its object was verified, or nothing at all to hold it until an operator releases it.",
                    this.SafetyInterval,
                    MaximumSafetyInterval));
        }

        if (this.PayloadsPerBatch <= 0 || this.PayloadsPerBatch > MaximumPayloadsPerBatch)
        {
            yield return Error(
                nameof(this.PayloadsPerBatch),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "is '{0}', which is outside the permitted range of 1 to {1}.",
                    this.PayloadsPerBatch,
                    MaximumPayloadsPerBatch));
        }
    }

    /// <summary>Reads the two bounds one release is performed under.</summary>
    /// <returns>The bounds the release works within.</returns>
    internal RetainedContentReleaseOptions ToReleaseOptions() => new()
    {
        SafetyInterval = this.SafetyInterval,
        PayloadsPerBatch = this.PayloadsPerBatch,
    };

    private static string Error(string propertyName, string detail) =>
        string.Format(CultureInfo.InvariantCulture, "{0}:{1} {2}", SectionPath, propertyName, detail);
}
