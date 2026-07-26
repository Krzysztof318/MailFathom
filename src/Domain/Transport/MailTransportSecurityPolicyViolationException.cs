// Copyright © 2026 Krzysztof Kasprowicz

using System.Collections.ObjectModel;

namespace MailMcp.Domain.Transport;

/// <summary>Indicates that a mail transport security policy would weaken transport protection in a way no opt-in allows.</summary>
/// <remarks>
/// The message lists violated rule identities only. Callers must not add the account credentials, host, or secret
/// reference to it, because this exception can reach logs and operator-facing startup output.
/// </remarks>
public sealed class MailTransportSecurityPolicyViolationException : Exception
{
    /// <summary>Initializes a new transport security policy violation.</summary>
    public MailTransportSecurityPolicyViolationException()
        : this([])
    {
    }

    /// <summary>Initializes a new transport security policy violation with a safe message.</summary>
    public MailTransportSecurityPolicyViolationException(string message)
        : base(message) => this.Violations = [];

    /// <summary>Initializes a new transport security policy violation with a safe message and inner exception.</summary>
    public MailTransportSecurityPolicyViolationException(string message, Exception innerException)
        : base(message, innerException) => this.Violations = [];

    /// <summary>Initializes a new transport security policy violation for the violated rules.</summary>
    /// <param name="violations">The violated transport security rules.</param>
    public MailTransportSecurityPolicyViolationException(IReadOnlyList<MailTransportSecurityViolation> violations)
        : base($"The mail transport security policy violates {DescribeViolations(violations)}.") =>
        this.Violations = new ReadOnlyCollection<MailTransportSecurityViolation>([.. violations ?? []]);

    /// <summary>Gets the violated transport security rules.</summary>
    public IReadOnlyList<MailTransportSecurityViolation> Violations { get; }

    private static string DescribeViolations(IReadOnlyList<MailTransportSecurityViolation>? violations) =>
        violations is { Count: > 0 } ? string.Join(", ", violations) : "an unspecified transport security rule";
}
