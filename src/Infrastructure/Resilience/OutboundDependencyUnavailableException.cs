// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Resilience;
using MailFathom.Domain.Failures;

namespace MailFathom.Infrastructure.Resilience;

/// <summary>Indicates that a resilience pipeline declined to serve an operation any further.</summary>
/// <remarks>
/// <para>
/// An abandoned attempt, an operation that outlived its total timeout, an open circuit, and an execution shed by the
/// concurrency limiter are all the same statement to a caller: the dependency is not usable right now and the work
/// belongs to a later run. They differ only in which limit was reached, which stays readable as the
/// <see cref="Exception.InnerException" /> the pipeline produced.
/// </para>
/// <para>
/// The translation exists so the resilience library stops here. An adapter maps this one type onto the failure its own
/// application port documents, and no caller above it has to name a Polly exception to recognize a dependency that is
/// refusing work.
/// </para>
/// </remarks>
public sealed class OutboundDependencyUnavailableException : MailFathomException
{
    /// <summary>Initializes a new dependency unavailability failure naming the class whose limit was reached.</summary>
    /// <param name="dependency">The dependency class whose pipeline declined the operation.</param>
    /// <param name="rejection">The rejection the pipeline produced.</param>
    public OutboundDependencyUnavailableException(OutboundDependency dependency, Exception rejection)
        : base(
            $"Outbound dependency {dependency} declined the operation because a configured resilience limit was reached.",
            rejection) =>
        this.Dependency = dependency;

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.OutboundDependencyUnavailable;

    /// <summary>Gets the dependency class whose pipeline declined the operation.</summary>
    public OutboundDependency Dependency { get; }
}
