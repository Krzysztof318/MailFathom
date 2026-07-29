// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Failures;
using MailMcp.Domain.Transport;
using MailMcp.TestSupport;
using Xunit;

namespace MailMcp.Domain.UnitTests;

/// <summary>Covers the contract every MailMcp failure takes part in.</summary>
public sealed class MailMcpExceptionTests
{
    /// <summary>A failure outside the hierarchy carries no code a boundary can report and obeys no stated message contract.</summary>
    [Fact]
    public void DomainAssembly_EveryDeclaredException_DerivesFromMailMcpException() =>
        ExceptionHierarchyAssertion.AssertEveryDeclaredExceptionDerivesFrom(
            typeof(MailMcpException).Assembly,
            typeof(MailMcpException));

    [Fact]
    public void ErrorCode_TransportSecurityPolicyViolation_IsTheCodeForThatFailure()
    {
        // Arrange
        var violations = new[] { MailTransportSecurityViolation.UnencryptedConnectionRequiresExplicitOptIn };

        // Act
        var failure = new MailTransportSecurityPolicyViolationException(violations);

        // Assert
        Assert.Equal(MailMcpErrorCode.MailTransportSecurityPolicyViolated, failure.ErrorCode);
    }

    /// <summary>A violation naming no rule would report that something is unsafe while withholding what, so it is rejected at construction.</summary>
    [Fact]
    public void MailTransportSecurityPolicyViolationException_NoViolations_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new MailTransportSecurityPolicyViolationException([]));
        Assert.Throws<ArgumentNullException>(() => new MailTransportSecurityPolicyViolationException(null!));
    }

    [Fact]
    public void MailTransportSecurityPolicyViolationException_Violations_AreNamedInTheMessageAndKept()
    {
        // Arrange
        var violations = new[]
        {
            MailTransportSecurityViolation.UnencryptedConnectionRequiresExplicitOptIn,
            MailTransportSecurityViolation.ClearTextAuthenticationRequiresEncryptedConnection,
        };

        // Act
        var failure = new MailTransportSecurityPolicyViolationException(violations);

        // Assert
        Assert.Equal(violations, failure.Violations);
        Assert.Contains(nameof(MailTransportSecurityViolation.UnencryptedConnectionRequiresExplicitOptIn), failure.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(MailTransportSecurityViolation.ClearTextAuthenticationRequiresEncryptedConnection), failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The payload is copied at construction so a caller's later mutation cannot rewrite what the failure reports.</summary>
    [Fact]
    public void MailTransportSecurityPolicyViolationException_CallerMutatesTheSuppliedList_DoesNotChangeTheFailure()
    {
        // Arrange
        var violations = new List<MailTransportSecurityViolation> { MailTransportSecurityViolation.UnencryptedConnectionRequiresExplicitOptIn };
        var failure = new MailTransportSecurityPolicyViolationException(violations);

        // Act
        violations.Add(MailTransportSecurityViolation.ClearTextAuthenticationRequiresEncryptedConnection);

        // Assert
        Assert.Single(failure.Violations);
    }
}
