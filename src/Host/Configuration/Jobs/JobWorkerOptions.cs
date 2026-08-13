// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace MailFathom.Host.Configuration.Jobs;

/// <summary>Configures the worker that runs durable background work.</summary>
/// <remarks>
/// <para>
/// Every setting here is a bound on one pass: how much it takes, how long it holds it, how long one job may run, and
/// how often it looks again when the queue was empty. What a job actually does is the consumer's own configuration,
/// which is why nothing here names a job type.
/// </para>
/// <para>
/// The one rule an attribute cannot express is the ordering between the timeout and the lease, and it is the rule that
/// keeps two workers from running one job: an attempt has to be cancelled before its lease can expire underneath it. A
/// deployment that inverts the two fails to start rather than running with the guarantee quietly gone.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class JobWorkerOptions : IValidatableObject
{
    /// <summary>Gets or sets whether the worker runs.</summary>
    /// <remarks>
    /// On by default, and on an instance with no registered handler it costs nothing: the worker says so once and stops,
    /// because a claim filtered to no job type would take work this build cannot run. Turning it off is for an operator
    /// who wants a replica serving MCP reads and nothing else.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the greatest number of jobs one pass claims.</summary>
    /// <remarks>
    /// The jobs of a batch run one after another, so this bounds how much one pass may hold rather than how much runs at
    /// once. Raising it makes a busy queue drain with fewer round trips; it does not make the instance work faster.
    /// </remarks>
    [Range(1, 100)]
    public int BatchSize { get; set; } = 5;

    /// <summary>Gets or sets how long a claim holds each job it takes.</summary>
    /// <remarks>
    /// It is how long work stays held after the process running it stops existing, so it is the delay before a crash is
    /// recovered from rather than a bound on anything a healthy instance does — a running attempt renews it at half this
    /// value for as long as it works.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:02", "01:00:00")]
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets or sets how long one job may run before it is cancelled.</summary>
    /// <remarks>Must be shorter than <see cref="LeaseDuration" />, which is what stops an attempt from outliving the lease it holds.</remarks>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Gets or sets how long the worker waits before looking again when a pass found nothing due.</summary>
    /// <remarks>
    /// A pass that filled its batch looks again at once, because a queue with work in it should be drained rather than
    /// polled. This is only how long an idle instance waits, so it trades how quickly enqueued work starts against how
    /// often an empty queue is queried.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (this.ExecutionTimeout >= this.LeaseDuration)
        {
            yield return new ValidationResult(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Jobs:ExecutionTimeout must be shorter than Jobs:LeaseDuration, which is {0}. A job allowed to run for as long as its lease is held can be claimed by a second worker while the first is still running it.",
                    this.LeaseDuration),
                [nameof(this.ExecutionTimeout)]);
        }
    }
}
