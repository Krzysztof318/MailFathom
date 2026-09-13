// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>
/// Covers what the administrative mail-account routes answer. The rules underneath are proved where they live, so what
/// these hold is the boundary's own share: a body carrying nothing to act on, a version no write can have been composed
/// over, a user named by no identifier, and the answer for an account or a user this deployment does not hold.
/// </summary>
public sealed class MailAccountEndpointsTests
{
    private const string Declaration = """
                                       {
                                         "EmailAddress": "archive@example.test",
                                         "DisplayName": "archive",
                                         "Host": "imap.example.test",
                                         "UserName": "archive@example.test",
                                         "Secrets": { "Password": { "Name": "archive-password", "SecretReference": "file:/run/secrets/archive-password" } }
                                       }
                                       """;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task CreateAsync_ARequestCarryingNoDeclaration_IsRefused(string? account)
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await MailAccountEndpoints.CreateAsync(
            deployment.MailAccounts,
            new MailAccountCreationRequest(SyntheticMailUser.Deployment.Value, account),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result);
    }

    [Fact]
    public async Task CreateAsync_ARequestNamingNobody_IsRefused()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await MailAccountEndpoints.CreateAsync(
            deployment.MailAccounts,
            new MailAccountCreationRequest(Guid.Empty, Declaration),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result);
    }

    [Fact]
    public async Task CreateAsync_AUserThisDeploymentDoesNotHold_AnswersThatThereIsNoSuchUser()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await MailAccountEndpoints.CreateAsync(
            deployment.MailAccounts,
            new MailAccountCreationRequest(SyntheticMailUser.Another.Value, Declaration),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound<ProblemDetails>>(result.Result);
    }

    /// <summary>The identifier is generated here, so the answer to a creation is the only place the caller learns it.</summary>
    [Fact]
    public async Task CreateAsync_ADeclarationTheDeploymentAccepts_AnswersTheIdentifierItGenerated()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);
        deployment.Holding(SyntheticMailUser.Deployment, """{"Language":"English"}""", version: 1);

        // Act
        var result = await MailAccountEndpoints.CreateAsync(
            deployment.MailAccounts,
            new MailAccountCreationRequest(SyntheticMailUser.Deployment.Value, Declaration),
            TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.IsType<Ok<MailAccountWriteResponse>>(result.Result).Value!;
        Assert.True(written.Committed);
        Assert.Equal(Assert.Single(deployment.MailAccountRecords.Accounts).Id, written.AccountId);
    }

    [Fact]
    public async Task ReadAsync_AnAccountThisDeploymentDoesNotHold_AnswersThatThereIsNoSuchAccount()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminRead]);

        // Act
        var result = await MailAccountEndpoints.ReadAsync(
            Guid.NewGuid(),
            deployment.MailAccounts,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound<ProblemDetails>>(result.Result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task SaveAsync_ARequestCarryingNoDeclaration_IsRefused(string? account)
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await MailAccountEndpoints.SaveAsync(
            Guid.NewGuid(),
            deployment.MailAccounts,
            new MailAccountSaveRequest(1, account),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result);
    }

    [Fact]
    public async Task SaveAsync_ARequestStatingANegativeVersion_IsRefused()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await MailAccountEndpoints.SaveAsync(
            Guid.NewGuid(),
            deployment.MailAccounts,
            new MailAccountSaveRequest(-1, Declaration),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result);
    }

    [Fact]
    public async Task SaveAsync_AnAccountThisDeploymentDoesNotHold_AnswersThatThereIsNoSuchAccount()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await MailAccountEndpoints.SaveAsync(
            Guid.NewGuid(),
            deployment.MailAccounts,
            new MailAccountSaveRequest(1, Declaration),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound<ProblemDetails>>(result.Result);
    }

    [Fact]
    public async Task AssignAsync_ARequestNamingNobody_IsRefused()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await MailAccountEndpoints.AssignAsync(
            Guid.NewGuid(),
            deployment.MailAccounts,
            new MailAccountAssignmentRequest(Guid.Empty),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result);
    }

    [Fact]
    public async Task AssignAsync_AnAccountThisDeploymentDoesNotHold_AnswersThatThereIsNoSuchAccount()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);
        deployment.Holding(SyntheticMailUser.Deployment, """{"Language":"English"}""", version: 1);

        // Act
        var result = await MailAccountEndpoints.AssignAsync(
            Guid.NewGuid(),
            deployment.MailAccounts,
            new MailAccountAssignmentRequest(SyntheticMailUser.Deployment.Value),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound<ProblemDetails>>(result.Result);
    }

    [Fact]
    public async Task UnassignAsync_ARequestNamingNobody_IsRefused()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminErase]);

        // Act
        var result = await MailAccountEndpoints.UnassignAsync(
            Guid.NewGuid(),
            deployment.MailAccounts,
            new MailAccountAssignmentRequest(Guid.Empty),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result);
    }

    /// <summary>An account already gone is the state the caller asked for, so it is reported as nothing erased rather than refused.</summary>
    [Fact]
    public async Task EraseAsync_AnAccountThisDeploymentDoesNotHold_AnswersThatNothingWasErased()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminErase]);

        // Act
        var result = await MailAccountEndpoints.EraseAsync(
            Guid.NewGuid(),
            deployment.MailAccounts,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Value!.Erased);
    }

    private static void AssertRefusal(IResult result)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);

        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }
}
