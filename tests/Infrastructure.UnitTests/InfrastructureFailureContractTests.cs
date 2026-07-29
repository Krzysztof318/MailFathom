// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Resilience;
using MailMcp.Domain.Failures;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Resilience;
using MailMcp.TestSupport;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

/// <summary>Covers the failure contract the infrastructure boundary raises.</summary>
public sealed class InfrastructureFailureContractTests
{
    /// <summary>A failure outside the hierarchy carries no code a boundary can report and obeys no stated message contract.</summary>
    [Fact]
    public void InfrastructureAssembly_EveryDeclaredException_DerivesFromMailMcpException() =>
        ExceptionHierarchyAssertion.AssertEveryDeclaredExceptionDerivesFrom(
            typeof(OutboundDependencyUnavailableException).Assembly,
            typeof(MailMcpException));

    [Fact]
    public void ErrorCode_OutboundDependencyUnavailable_IsTheCodeAndKeepsTheRejection()
    {
        // Arrange
        var rejection = new InvalidOperationException("pipeline rejection");

        // Act
        var failure = new OutboundDependencyUnavailableException(OutboundDependency.MailboxDataRetrieval, rejection);

        // Assert
        Assert.Equal(MailMcpErrorCode.OutboundDependencyUnavailable, failure.ErrorCode);
        Assert.Equal(OutboundDependency.MailboxDataRetrieval, failure.Dependency);
        Assert.Same(rejection, failure.InnerException);
    }

    [Fact]
    public void ErrorCode_MailAuthenticationMechanismUnavailable_IsTheCodeForThatFailure()
    {
        // Act
        var failure = new MailAuthenticationMechanismUnavailableException("primary", ["SCRAM-SHA-256"]);

        // Assert
        Assert.Equal(MailMcpErrorCode.MailAuthenticationMechanismUnavailable, failure.ErrorCode);
        Assert.Equal("primary", failure.AccountId);
        Assert.Equal(["SCRAM-SHA-256"], failure.PermittedMechanismNames);
    }

    /// <summary>Recording what the server offered would document a downgrade path in logs, so the failure never learns it.</summary>
    [Fact]
    public void MailAuthenticationMechanismUnavailableException_Message_NamesOnlyThePermittedMechanisms()
    {
        // Act
        var failure = new MailAuthenticationMechanismUnavailableException("primary", ["SCRAM-SHA-256"]);

        // Assert
        Assert.Contains("SCRAM-SHA-256", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("LOGIN", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("PLAIN", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MailAuthenticationMechanismUnavailableException_MissingArguments_AreRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new MailAuthenticationMechanismUnavailableException(null!, ["SCRAM-SHA-256"]));
        Assert.Throws<ArgumentNullException>(() => new MailAuthenticationMechanismUnavailableException("primary", null!));
    }

    /// <summary>The payload is copied at construction so a caller's later mutation cannot rewrite what the failure reports.</summary>
    [Fact]
    public void MailAuthenticationMechanismUnavailableException_CallerMutatesTheSuppliedList_DoesNotChangeTheFailure()
    {
        // Arrange
        var permitted = new List<string> { "SCRAM-SHA-256" };
        var failure = new MailAuthenticationMechanismUnavailableException("primary", permitted);

        // Act
        permitted.Add("PLAIN");

        // Assert
        Assert.Equal(["SCRAM-SHA-256"], failure.PermittedMechanismNames);
    }
}
