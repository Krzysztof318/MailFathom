// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.Execution;

/// <summary>Decides what a job's row records about the failure one attempt raised.</summary>
/// <remarks>
/// <para>
/// A port rather than a method on the executor, because the answer is read out of failure types the adapters own: a
/// dependency that declined the work, a provider that refused it, a socket that closed. Naming those inside
/// <c>Application</c> would put the outbound stack on the wrong side of the boundary, and asking the handler would let
/// every consumer invent a verdict of its own for the same failure.
/// </para>
/// <para>
/// The classification and the reason are one answer rather than two calls, because they are read from the same walk
/// over the same exception chain, and a reason that disagreed with the verdict beside it would be worse than either.
/// </para>
/// </remarks>
public interface IJobFailureClassifier
{
    /// <summary>Classifies what a handler raised, and names it in terms safe to store and to log.</summary>
    /// <param name="failure">The failure the attempt produced.</param>
    /// <returns>The verdict and the operator-safe reason the job's row keeps.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="failure" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The answer is derived from failure types and from what a first-party failure declares about itself, never from an
    /// exception message: a library's message may quote the message the job points at.
    /// </remarks>
    JobFailureRecord Classify(Exception failure);
}
