// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Resilience;

/// <summary>Decides whether a failed outbound operation is worth repeating.</summary>
/// <remarks>
/// <para>
/// The port exists so a use case can ask the question the retry pipeline asks itself, without depending on the
/// resilience library that owns the pipeline. A supervisor deciding whether to keep an account in its rotation and a
/// pipeline deciding whether to make another attempt must agree, and they only agree if one implementation answers
/// both.
/// </para>
/// <para>
/// Authentication, permission, and malformed-request failures are terminal for every dependency class. Repeating them
/// cannot succeed, and against a mail server it can lock the account.
/// </para>
/// </remarks>
public interface ITransientFailureClassifier
{
    /// <summary>Reports whether a failure is expected to clear on its own, so repeating the operation can succeed.</summary>
    /// <param name="dependency">The dependency class the operation belonged to.</param>
    /// <param name="failure">The failure the operation produced.</param>
    /// <returns><see langword="true" /> when the operation is worth repeating; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="failure" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="dependency" /> is not a defined member.</exception>
    /// <remarks>
    /// A caller's own cancellation is never transient. Implementations answer from the failure's type and its
    /// protocol-level status alone, and never from personal data, credentials, or provider payloads.
    /// </remarks>
    bool IsTransientFailure(OutboundDependency dependency, Exception failure);
}
