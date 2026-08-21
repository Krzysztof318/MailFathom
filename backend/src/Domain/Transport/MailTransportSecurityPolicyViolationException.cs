// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.ObjectModel;
using MailFathom.Domain.Failures;

namespace MailFathom.Domain.Transport;

/// <summary>Indicates that a mail transport security policy would weaken transport protection in a way no opt-in allows.</summary>
/// <remarks>The message lists violated rule identities only; <see cref="MailFathomException" /> states what a message may carry.</remarks>
public sealed class MailTransportSecurityPolicyViolationException : MailFathomException
{
    /// <summary>Initializes a new transport security policy violation for the violated rules.</summary>
    /// <param name="violations">The violated transport security rules, of which there is at least one.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="violations" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="violations" /> is empty, which would report a violation naming no rule.</exception>
    public MailTransportSecurityPolicyViolationException(IReadOnlyList<MailTransportSecurityViolation> violations)
        : base($"The mail transport security policy violates {DescribeViolations(violations)}.")
    {
        this.Violations = new ReadOnlyCollection<MailTransportSecurityViolation>([.. violations]);
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.MailTransportSecurityPolicyViolated;

    /// <summary>Gets the violated transport security rules, of which there is at least one.</summary>
    public IReadOnlyList<MailTransportSecurityViolation> Violations { get; }

    private static string DescribeViolations(IReadOnlyList<MailTransportSecurityViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(violations);

        if (violations.Count is 0)
        {
            throw new ArgumentException("A transport security policy violation names at least one violated rule.", nameof(violations));
        }

        return string.Join(", ", violations);
    }
}
