// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Sockets;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Domain.Failures;

namespace MailFathom.Infrastructure.Resilience;

/// <summary>Classifies what a job handler raised, so only work that could still succeed is attempted again.</summary>
/// <remarks>
/// <para>
/// It sits beside <see cref="TransientFailureClassifier" /> and answers a different question at a different level. That
/// one decides whether one call to a dependency is worth repeating inside a single attempt, from the protocol family
/// the caller named. This one decides whether a whole job is worth attempting again minutes later, in a process that
/// need not be the one that failed and with no idea which of a handler's dependencies produced the failure — so it
/// reads what a failure declares about itself rather than re-deriving a family verdict the caller is not there to name.
/// </para>
/// <para>
/// A pipeline that declined the work is the clearest such declaration: an open circuit, a shed execution, an exhausted
/// attempt budget, and an expired total timeout all say the dependency is unusable right now and the work belongs to a
/// later run. That is also where the two levels meet — by the time one of those reaches a handler, the operation's own
/// retry budget is already spent, and the job's attempt is the next layer out rather than a second retry at the same one.
/// </para>
/// <para>
/// Everything unrecognized is permanent, which is the same refusal the classifier beside it makes for the same reason:
/// a failure whose meaning is unknown is not repeated. What it costs here is bounded — a job that could have succeeded
/// stops on its first attempt and is visible as a dead letter — while the opposite mistake is a job repeating a failure
/// nothing can fix until it has spent its whole budget.
/// </para>
/// <para>
/// The reason is composed from type names and a stable error code, never from an exception message. A handler works on
/// mail, so a library's message may quote a subject, an address, or a header, and this text is stored on the row and
/// read back into every report of it.
/// </para>
/// </remarks>
internal sealed class JobFailureClassifier : IJobFailureClassifier
{
    /// <inheritdoc />
    public JobFailureRecord Classify(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return JobFailureRecord.Create(ClassifyCause(failure), DescribeReason(failure));
    }

    /// <summary>Reads the verdict off the failure, following the chain because an adapter wraps what it caught.</summary>
    private static JobFailureClassification ClassifyCause(Exception failure) => failure switch
    {
        OutboundDependencyUnavailableException => JobFailureClassification.Transient,

        // The adapter has already classified its provider's answer, so this defers to it rather than producing a
        // second opinion for the same failure.
        EmbeddingGenerationFailedException generationFailure => generationFailure.IsWorthRepeating
            ? JobFailureClassification.Transient
            : JobFailureClassification.Permanent,

        // A connection that dropped or a stream that ended is the one failure family that means the same thing to every
        // dependency, and it is what a handler reaching outside the resilience pipelines produces.
        SocketException or IOException or TimeoutException => JobFailureClassification.Transient,

        { InnerException: { } cause } => ClassifyCause(cause),

        _ => JobFailureClassification.Permanent,
    };

    /// <summary>Names the failure by its type, and by the stable code where MailFathom raised it itself.</summary>
    /// <remarks>
    /// The first first-party failure in the chain is preferred over the outermost exception, because a wrapper the
    /// runtime produced names nothing an operator can look up while the code beneath it names exactly one failure.
    /// </remarks>
    private static string DescribeReason(Exception failure) =>
        FindFirstPartyFailure(failure) is { } firstPartyFailure
            ? $"{firstPartyFailure.GetType().Name} ({firstPartyFailure.ErrorCode})"
            : failure.GetType().Name;

    private static MailFathomException? FindFirstPartyFailure(Exception failure) => failure switch
    {
        MailFathomException firstPartyFailure => firstPartyFailure,
        { InnerException: { } cause } => FindFirstPartyFailure(cause),
        _ => null,
    };
}
