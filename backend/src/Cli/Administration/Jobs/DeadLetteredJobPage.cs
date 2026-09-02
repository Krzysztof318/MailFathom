// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Jobs;

/// <summary>One page of the background work a deployment will not attempt again.</summary>
/// <param name="Jobs">The jobs, ordered by when each one stopped, newest first.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end.</param>
internal sealed record DeadLetteredJobPage(
    [property: JsonPropertyName("jobs")] IReadOnlyList<DeadLetteredJobReading> Jobs,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);

/// <summary>One job a deployment will not attempt again.</summary>
/// <param name="Job">The identifier a retry or a drop names it by.</param>
/// <param name="Type">The kind of work.</param>
/// <param name="Key">The identity the enqueuer composed, which a retry runs under unchanged.</param>
/// <param name="Account">The account the work belongs to, absent when it belongs to none.</param>
/// <param name="AttemptCount">How many attempts were handed out before the job stopped.</param>
/// <param name="FailureClassification">What the failure that ended it was classified as, absent where the deployment records none.</param>
/// <param name="FailureReason">The deployment's own name for that failure, absent where it records none.</param>
/// <param name="EnqueuedAt">When the work was first enqueued.</param>
/// <param name="DeadLetteredAt">When the job stopped.</param>
internal sealed record DeadLetteredJobReading(
    [property: JsonPropertyName("job")] Guid Job,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("key")] string? Key,
    [property: JsonPropertyName("account")] string? Account,
    [property: JsonPropertyName("attemptCount")] int AttemptCount,
    [property: JsonPropertyName("failureClassification")] string? FailureClassification,
    [property: JsonPropertyName("failureReason")] string? FailureReason,
    [property: JsonPropertyName("enqueuedAt")] DateTimeOffset EnqueuedAt,
    [property: JsonPropertyName("deadLetteredAt")] DateTimeOffset DeadLetteredAt)
{
    /// <summary>Describes what ended the job, and how many attempts it took to get there.</summary>
    /// <returns>The classification, the reason, and the attempt count.</returns>
    /// <remarks>
    /// The classification leads because it is what decides the operator's next move: a permanent failure names
    /// something to fix before a retry could do anything, and a transient one that ran out of attempts names a
    /// dependency that stayed broken for longer than the queue was willing to wait.
    /// </remarks>
    internal string DescribeFailure() => string.Create(
        CultureInfo.InvariantCulture,
        $"{this.FailureClassification ?? "unrecorded"} {this.FailureReason ?? "failure"} after {this.AttemptCount} attempt(s)");
}
