// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Resilience;
using MailFathom.Domain.Failures;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Resilience;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests;

/// <summary>Covers the failure contract the infrastructure boundary raises.</summary>
public sealed class InfrastructureFailureContractTests
{
    /// <summary>A failure outside the hierarchy carries no code a boundary can report and obeys no stated message contract.</summary>
    [Fact]
    public void InfrastructureAssembly_EveryDeclaredException_DerivesFromMailFathomException()
    {
        // Arrange
        var infrastructureAssembly = typeof(OutboundDependencyUnavailableException).Assembly;

        // Act, Assert
        ExceptionHierarchyAssertion.AssertEveryDeclaredExceptionDerivesFrom(infrastructureAssembly, typeof(MailFathomException));
    }

    [Fact]
    public void ErrorCode_OutboundDependencyUnavailable_IsTheCodeAndKeepsTheRejection()
    {
        // Arrange
        var rejection = new InvalidOperationException("pipeline rejection");

        // Act
        var failure = new OutboundDependencyUnavailableException(OutboundDependency.MailboxDataRetrieval, rejection);

        // Assert
        Assert.Equal(MailFathomErrorCode.OutboundDependencyUnavailable, failure.ErrorCode);
        Assert.Equal(OutboundDependency.MailboxDataRetrieval, failure.Dependency);
        Assert.Same(rejection, failure.InnerException);
    }

    /// <summary>An inner exception is diagnostic detail for a log; copying its text would put a provider payload into an operator-facing message.</summary>
    [Fact]
    public void OutboundDependencyUnavailableException_Message_NamesOnlyTheDependencyClass()
    {
        // Arrange
        var rejection = new InvalidOperationException("host mail.example.test refused the connection");

        // Act
        var failure = new OutboundDependencyUnavailableException(OutboundDependency.MailboxDataRetrieval, rejection);

        // Assert
        Assert.Contains(nameof(OutboundDependency.MailboxDataRetrieval), failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(rejection.Message, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("mail.example.test", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorCode_MailAuthenticationMechanismUnavailable_IsTheCodeForThatFailure()
    {
        // Act
        var failure = new MailAuthenticationMechanismUnavailableException("primary", ["SCRAM-SHA-256"]);

        // Assert
        Assert.Equal(MailFathomErrorCode.MailAuthenticationMechanismUnavailable, failure.ErrorCode);
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

    /// <summary>What an operator does about a refused credential and about an unreachable endpoint are different acts, so an alert has to tell them apart.</summary>
    [Theory]
    [MemberData(nameof(EveryRaisableObjectStorageClassification))]
    public void ObjectStorageUnavailableException_ErrorCode_IsTheClassificationsRatherThanTheTypes(
        string classificationName)
    {
        // Arrange
        Assert.True(ObjectStorageFailure.TryParse(classificationName, out var classification));

        var answer = new InvalidOperationException("the endpoint answered");

        // Act
        var failure = ObjectStorageUnavailableException.From(classification, answer);

        // Assert
        Assert.Equal(classification.ErrorCode, failure.ErrorCode);
        Assert.Equal(classification, failure.Failure);
        Assert.Same(answer, failure.InnerException);
    }

    /// <summary>
    /// The message names the configuration key an operator edits and nothing else. The endpoint's own answer stays on
    /// the inner exception, which is diagnostic detail for a log rather than something a boundary republishes.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryRaisableObjectStorageClassification))]
    public void ObjectStorageUnavailableException_Message_CarriesNeitherTheAddressNorTheCredential(
        string classificationName)
    {
        // Arrange
        Assert.True(ObjectStorageFailure.TryParse(classificationName, out var classification));

        var answer = new InvalidOperationException(
            "objects.example.test refused AKIAEXAMPLEIDENTIFIER for bucket payloads");

        // Act
        var failure = ObjectStorageUnavailableException.From(classification, answer);

        // Assert
        Assert.DoesNotContain(answer.Message, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("objects.example.test", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("AKIAEXAMPLEIDENTIFIER", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The classification is what supplies the code, so a failure raised without one would carry none.</summary>
    [Fact]
    public void ObjectStorageUnavailableException_AnUnspecifiedClassification_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => ObjectStorageUnavailableException.From(default, new InvalidOperationException("nothing")));
        Assert.Throws<ArgumentNullException>(
            () => ObjectStorageUnavailableException.From(ObjectStorageFailure.TimedOut, cause: null!));
    }

    /// <summary>
    /// Every classification but the caller's own, which is rethrown unchanged so a caller that went away and an endpoint
    /// that refused work never arrive as one failure.
    /// </summary>
    public static TheoryData<string> EveryRaisableObjectStorageClassification =>
    [
        .. ObjectStorageFailure.All
            .Where(classification => classification != ObjectStorageFailure.CallerCancelled)
            .Select(classification => classification.Name),
    ];
}
