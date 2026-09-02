// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs;

/// <summary>What one failed attempt leaves on the job's row: the verdict, and the reason it was reached.</summary>
/// <remarks>
/// <para>
/// The reason is written for an operator who is looking at a job that stopped, so it names the failure rather than
/// describing it: a first-party failure is named by its type and its stable code, and anything else by the type the
/// runtime or a library raised. <strong>An exception message never becomes a reason.</strong> Only a
/// <c>MailFathomException</c> promises its message is free of mail content, and a job's whole purpose is to work on a
/// message — so a library's message quoting a subject, an address, or a header would put mail content in a column that
/// outlives the run and reaches every log line reporting it.
/// </para>
/// <para>
/// A record replaces whatever the previous attempt left, because what an operator acts on is why the job is where it is
/// now. The attempt count beside it is what says how many times it got there.
/// </para>
/// </remarks>
public sealed record JobFailureRecord
{
    /// <summary>The greatest length a reason may have, which bounds the column it is stored in.</summary>
    public const int MaximumReasonLength = 128;

    private JobFailureRecord(JobFailureClassification classification, string reason)
    {
        this.Classification = classification;
        this.Reason = reason;
    }

    /// <summary>Gets the record left by a job claimed for a type this build has no handler for.</summary>
    /// <remarks>
    /// Permanent, because the claim is already filtered to the types this process runs: reaching this means the handler
    /// was withdrawn while the process ran, and no number of further attempts on this instance can find it.
    /// </remarks>
    public static JobFailureRecord HandlerMissing { get; } =
        new(JobFailureClassification.Permanent, "HandlerMissing");

    /// <summary>Gets the record left by an attempt that exceeded the time one job is allowed to run for.</summary>
    /// <remarks>
    /// Transient, because a timeout says the work did not finish in time rather than that it cannot: the dependency it
    /// waited on is the ordinary cause and a later attempt meets a different one. What makes repeating it safe is the
    /// promise every handler is registered on, that running it twice with one payload is the same as running it once.
    /// </remarks>
    public static JobFailureRecord ExecutionTimedOut { get; } =
        new(JobFailureClassification.Transient, "ExecutionTimeout");

    /// <summary>Gets whether repeating the work could succeed without anything else changing first.</summary>
    public JobFailureClassification Classification { get; }

    /// <summary>Gets the operator-safe name of what failed, carrying nothing out of the message the job points at.</summary>
    public string Reason { get; }

    /// <summary>States what one failed attempt records against its job.</summary>
    /// <param name="classification">Whether repeating the work could succeed.</param>
    /// <param name="reason">The operator-safe name of what failed, free of mail content.</param>
    /// <returns>The record the job's row keeps.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason" /> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="classification" /> is not a defined member.</exception>
    /// <remarks>
    /// A reason longer than the column allows is shortened rather than refused. It is composed here from a type name and
    /// a code, so the bound is unreachable in practice, and failing the one write whose purpose is to record a failure
    /// would leave the job held until its lease ran out with nothing said about why.
    /// </remarks>
    public static JobFailureRecord Create(JobFailureClassification classification, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (!Enum.IsDefined(classification))
        {
            throw new ArgumentOutOfRangeException(
                nameof(classification),
                classification,
                "A job failure is recorded as either transient or permanent.");
        }

        var trimmedReason = reason.Trim();

        return new JobFailureRecord(
            classification,
            trimmedReason.Length > MaximumReasonLength ? trimmedReason[..MaximumReasonLength] : trimmedReason);
    }
}
