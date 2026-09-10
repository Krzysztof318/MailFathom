// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Security.Sessions;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Secrets.Database;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>
/// Covers what the administrative user routes answer. The rules underneath are proved where they live, so what these
/// hold is the boundary's own share: an identifier the type will not carry, a version no write can have been composed
/// over, a body carrying nothing to act on, and the one answer a caller must never be able to tell apart from another —
/// a user this deployment does not hold.
/// </summary>
public sealed class UserRecordEndpointsTests
{
    private const string EmptyRecord = "{}";

    private static readonly DateTimeOffset Instant = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A session held for the user these tests erase, minted by a credential the erasure removes by cascade and never names.</summary>
    private static AdmittedUserCredential SessionHeldForTheUser() => new(
        new Guid("8b2e91c4-0a77-4f35-9d18-6e4c2a70b5f9"),
        SyntheticMailUser.Deployment,
        [MailFathomPermission.MailRead]);

    [Fact]
    public async Task ReadRosterAsync_ADeploymentHoldingUsers_ReportsEachOneWithTheLabelAnAdministratorSelectsBy()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminRead]);
        deployment.Held(new MailUserRecord(SyntheticMailUser.Deployment, "alex", DocumentWrittenAtRuntime: true));

        // Act
        var result = await UserRecordEndpoints.ReadRosterAsync(
            deployment.Roster,
            TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(result.Value!.Users);

        Assert.Equal(SyntheticMailUser.Deployment.Value, entry.Id);
        Assert.Equal("alex", entry.DisplayName);
        Assert.True(entry.RecordIsTheirOwn);
    }

    [Fact]
    public async Task ProvisionAsync_ALabelTheDeploymentAccepts_ReportsTheIdentifierItMinted()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await UserRecordEndpoints.ProvisionAsync(
            deployment.Roster,
            new UserProvisioningRequest("alex"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(Guid.Empty, Assert.IsType<Ok<UserProvisionedResponse>>(result.Result).Value!.Id);
    }

    /// <summary>A refusal is a request the administrator corrects, so it names what to change rather than reporting that something failed.</summary>
    [Fact]
    public async Task ProvisionAsync_ARequestCarryingNoLabel_IsRefusedNamingWhatHasToChange()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await UserRecordEndpoints.ProvisionAsync(
            deployment.Roster,
            new UserProvisioningRequest(DisplayName: null),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result, StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// A user this deployment does not hold is reported as nothing erased rather than as a refusal, because the
    /// caller asked for a state and the deployment is in it — and telling the two apart would report which user
    /// identifiers exist.
    /// </summary>
    [Fact]
    public async Task EraseAsync_AUserThisDeploymentDoesNotHold_ReportsNothingErasedRatherThanRefusing()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminErase]);

        // Act
        var result = await UserRecordEndpoints.EraseAsync(
            SyntheticMailUser.Another.Value,
            deployment.Roster,
            sessions: null,
            TestContext.Current.CancellationToken);

        // Assert
        var erasure = Assert.IsType<Ok<UserErasureResponse>>(result.Result).Value!;

        Assert.False(erasure.Erased);
        Assert.False(erasure.WasServed);
    }

    /// <summary>The response says whether the erasure also removed the person from the running process.</summary>
    [Fact]
    public async Task EraseAsync_AUserThisProcessIsServing_ReportsThatItWasServingThem()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminErase]);
        deployment.Serving(new ServedMailUser(SyntheticMailUser.Deployment, "alex", []));
        deployment.Erasure.EraseAsync(SyntheticMailUser.Deployment, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var result = await UserRecordEndpoints.EraseAsync(
            SyntheticMailUser.Deployment.Value,
            deployment.Roster,
            sessions: null,
            TestContext.Current.CancellationToken);

        // Assert
        var erasure = Assert.IsType<Ok<UserErasureResponse>>(result.Result).Value!;

        Assert.True(erasure.Erased);
        Assert.True(erasure.WasServed);
    }

    /// <summary>Erasing a person ends the client sessions their credentials minted, which the cascade that removes those rows never reaches.</summary>
    /// <remarks>
    /// A session is verified against the process's own store rather than against the row it came from, and a renewal
    /// re-mints from what that store holds — so an erased person's client would go on authenticating, and renewing,
    /// for as long as the process ran.
    /// </remarks>
    [Fact]
    public async Task EraseAsync_AUserThisDeploymentHolds_EndsTheSessionsTheirCredentialsMinted()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminErase]);
        var sessions = new ClientSessionTokens(new FakeTimeProvider(Instant));
        var held = sessions.Mint(SessionHeldForTheUser())!;

        deployment.Erasure.EraseAsync(SyntheticMailUser.Deployment, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        await UserRecordEndpoints.EraseAsync(
            SyntheticMailUser.Deployment.Value,
            deployment.Roster,
            sessions,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(sessions.Verify(held.Value));
    }

    [Fact]
    public async Task EraseAsync_ARequestNamingNoUser_IsRefusedWithoutReachingTheErasure()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminErase]);

        // Act
        var result = await UserRecordEndpoints.EraseAsync(
            Guid.Empty,
            deployment.Roster,
            sessions: null,
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result, StatusCodes.Status400BadRequest);
        await deployment.Erasure.DidNotReceiveWithAnyArgs()
            .EraseAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>The label the request carried is the whole of what changed, so acceptance is the whole answer.</summary>
    [Fact]
    public async Task RelabelAsync_AUserThisDeploymentHolds_AnswersWithNoContent()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);
        deployment.Held(new MailUserRecord(SyntheticMailUser.Deployment, "alexandra", DocumentWrittenAtRuntime: true));

        // Act
        var result = await UserRecordEndpoints.RelabelAsync(
            SyntheticMailUser.Deployment.Value,
            deployment.Roster,
            new UserRelabelRequest("alex"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NoContent>(result.Result);
        await deployment.Provisioning.Received(1)
            .RelabelAsync(SyntheticMailUser.Deployment, "alex", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A user this deployment does not hold is the same answer here as at every other route that names one in its
    /// own path: the record is not there. Answering a refusal instead would make this the one user-scoped route where
    /// an absent user reads as a request that was wrong, and would leave a caller granted the write but not the read
    /// able to tell a user who exists from one who does not.
    /// </summary>
    [Fact]
    public async Task RelabelAsync_AUserThisDeploymentDoesNotHold_AnswersThatThereIsNoSuchRecord()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await UserRecordEndpoints.RelabelAsync(
            SyntheticMailUser.Another.Value,
            deployment.Roster,
            new UserRelabelRequest("sam"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound<ProblemDetails>>(result.Result);
        await deployment.Provisioning.DidNotReceiveWithAnyArgs()
            .RelabelAsync(default, default!, CancellationToken.None);
    }

    [Fact]
    public async Task RelabelAsync_ARequestNamingNoUser_IsRefusedWithoutReachingTheRow()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await UserRecordEndpoints.RelabelAsync(
            Guid.Empty,
            deployment.Roster,
            new UserRelabelRequest("alex"),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result, StatusCodes.Status400BadRequest);
        await deployment.Provisioning.DidNotReceiveWithAnyArgs()
            .RelabelAsync(default, default!, CancellationToken.None);
    }

    /// <summary>Reading the roster is not deciding what it says, so the read grant reaches no rename.</summary>
    [Fact]
    public async Task RelabelAsync_ACallerHoldingOnlyTheAdministrativeRead_IsRefused()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminRead]);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => UserRecordEndpoints.RelabelAsync(
            SyntheticMailUser.Deployment.Value,
            deployment.Roster,
            new UserRelabelRequest("alex"),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadRecordAsync_AUserThisDeploymentHolds_ReportsTheRecordAndTheVersionAChangeIsAcceptedAgainst()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminRead]);
        deployment.Holding(SyntheticMailUser.Deployment, EmptyRecord, version: 3);

        // Act
        var result = await UserRecordEndpoints.ReadRecordAsync(
            SyntheticMailUser.Deployment.Value,
            deployment.Records,
            TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.IsType<Ok<UserRecordResponse>>(result.Result).Value!;

        Assert.Equal(SyntheticMailUser.Deployment.Value, record.User);
        Assert.Equal(3, record.Version);
        Assert.False(record.ReadFromConfiguration);
    }

    /// <summary>The same answer a user this deployment genuinely does not hold receives, which is what keeps a caller from learning which identifiers exist by asking about them.</summary>
    [Fact]
    public async Task ReadRecordAsync_AUserThisDeploymentDoesNotHold_AnswersWithoutSayingAnythingAboutTheIdentifier()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminRead]);

        // Act
        var result = await UserRecordEndpoints.ReadRecordAsync(
            SyntheticMailUser.Another.Value,
            deployment.Records,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<NotFound<ProblemDetails>>(result.Result).Value!;

        Assert.DoesNotContain(
            SyntheticMailUser.Another.Value.ToString("D"),
            refusal.Detail!,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The parser's own message names the offending token, the JSON path it stopped at, and a byte position — and that
    /// path is composed from the row's own key names, which for a user's record are their mailboxes.
    /// </summary>
    [Fact]
    public async Task ReadRecordAsync_ARowThatIsNotADocumentOfSettings_IsRefusedWithoutRepeatingWhatTheParserSaw()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminRead]);
        deployment.Holding(SyntheticMailUser.Deployment, """{"MailAccounts":[{"AccountId":"alex-private"}""", version: 1);

        // Act
        var result = await UserRecordEndpoints.ReadRecordAsync(
            SyntheticMailUser.Deployment.Value,
            deployment.Records,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = AssertRefusal(result.Result, StatusCodes.Status400BadRequest);

        Assert.DoesNotContain("alex-private", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadRecordAsync_ARequestNamingNoUser_IsRefusedWithoutReachingTheStore()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminRead]);

        // Act
        var result = await UserRecordEndpoints.ReadRecordAsync(
            Guid.Empty,
            deployment.Records,
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result, StatusCodes.Status400BadRequest);
        await deployment.Documents.DidNotReceiveWithAnyArgs().ReadAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>A version is what a record is accepted against, so one no write can have been composed over is a request to correct.</summary>
    [Fact]
    public async Task SaveRecordAsync_ARequestStatingANegativeVersion_IsRefusedWithoutReachingTheStore()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await UserRecordEndpoints.SaveRecordAsync(
            SyntheticMailUser.Deployment.Value,
            deployment.Records,
            new UserRecordSaveRequest(-1, EmptyRecord),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result, StatusCodes.Status400BadRequest);
        await deployment.Documents.DidNotReceiveWithAnyArgs().ReadAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>An editing session that means to change nothing sends nothing at all, so an empty body is a request to correct rather than a record to apply.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task SaveRecordAsync_ARequestCarryingNoRecord_IsRefused(string? document)
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await UserRecordEndpoints.SaveRecordAsync(
            SyntheticMailUser.Deployment.Value,
            deployment.Records,
            new UserRecordSaveRequest(1, document),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result, StatusCodes.Status400BadRequest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AddMailAccountAsync_ARequestCarryingNoDeclaration_IsRefused(string? account)
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await UserRecordEndpoints.AddMailAccountAsync(
            SyntheticMailUser.Deployment.Value,
            deployment.Records,
            new UserMailAccountRequest(1, account),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result, StatusCodes.Status400BadRequest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveMailAccountAsync_ARequestNamingNoAccount_IsRefused(string? accountId)
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await UserRecordEndpoints.RemoveMailAccountAsync(
            SyntheticMailUser.Deployment.Value,
            deployment.Records,
            new UserMailAccountRemovalRequest(1, accountId),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result, StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Every refusal about the record itself is a success status carrying the outcome, because each is something the
    /// administrator acts on and continues from — and each carries the version they compose the next attempt over.
    /// </summary>
    [Fact]
    public async Task AddMailAccountAsync_AWriteTheRecordRefuses_AnswersWithTheOutcomeRatherThanAnError()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);
        deployment.Holding(SyntheticMailUser.Deployment, EmptyRecord, version: 6);

        // Act
        var result = await UserRecordEndpoints.AddMailAccountAsync(
            SyntheticMailUser.Deployment.Value,
            deployment.Records,
            new UserMailAccountRequest(2, """{"AccountId":"archive"}"""),
            TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.IsType<Ok<UserRecordWriteResponse>>(result.Result).Value!;

        Assert.False(written.Committed);
        Assert.Equal(6, written.Version);
        Assert.Equal(MailFathomErrorCode.ConfigurationVersionSuperseded.Value, written.Code);
    }

    [Fact]
    public async Task AddMailAccountAsync_AUserThisDeploymentDoesNotHold_AnswersThatThereIsNoSuchUser()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await UserRecordEndpoints.AddMailAccountAsync(
            SyntheticMailUser.Another.Value,
            deployment.Records,
            new UserMailAccountRequest(1, """{"AccountId":"archive"}"""),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound<ProblemDetails>>(result.Result);
    }

    [Fact]
    public async Task StoreSecretAsync_AnExistingUser_ReturnsOnlyTheDatabaseReference()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);
        deployment.Holding(SyntheticMailUser.Deployment, EmptyRecord, version: 1);

        // Act
        var result = await UserRecordEndpoints.StoreSecretAsync(
            SyntheticMailUser.Deployment.Value,
            new StoredSecretWriteRequest("primary-password", "not-a-real-mailbox-password"),
            deployment.Secrets,
            TestContext.Current.CancellationToken);

        // Assert
        var provisioned = Assert.IsType<Ok<StoredSecretProvisionedResponse>>(result.Result).Value!;
        Assert.StartsWith("database:", provisioned.SecretReference, StringComparison.Ordinal);
        Assert.DoesNotContain("mailbox-password", provisioned.SecretReference, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoreSecretAsync_ARequestCarryingEmptyMaterial_RefusesWithoutReachingTheStore()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await UserRecordEndpoints.StoreSecretAsync(
            SyntheticMailUser.Deployment.Value,
            new StoredSecretWriteRequest("primary-password", string.Empty),
            deployment.Secrets,
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result, StatusCodes.Status400BadRequest);
        await deployment.StoredSecrets.DidNotReceiveWithAnyArgs().StoreAsync(
            default!,
            default,
            default,
            default,
            default!,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StoreSecretAsync_AUserThisDeploymentDoesNotHold_AnswersThatThereIsNoSuchUser()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await UserRecordEndpoints.StoreSecretAsync(
            SyntheticMailUser.Another.Value,
            new StoredSecretWriteRequest("primary-password", "not-a-real-mailbox-password"),
            deployment.Secrets,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound<ProblemDetails>>(result.Result);
    }

    [Fact]
    public async Task StoreSecretAsync_MaterialPastTheBound_RefusesWithoutReachingTheStore()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);
        var material = new string('x', IStoredSecretStore.MaximumMaterialByteCount + 1);

        // Act
        var result = await UserRecordEndpoints.StoreSecretAsync(
            SyntheticMailUser.Deployment.Value,
            new StoredSecretWriteRequest("primary-password", material),
            deployment.Secrets,
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result, StatusCodes.Status400BadRequest);
        await deployment.StoredSecrets.DidNotReceiveWithAnyArgs().StoreAsync(
            default!,
            default,
            default,
            default,
            default!,
            TestContext.Current.CancellationToken);
    }

    /// <summary>Every route asks the use case for its permission with the transport absent, so an entrypoint added later cannot widen the surface by forgetting a route filter.</summary>
    [Fact]
    public async Task ReadRosterAsync_ACallerHoldingNothing_IsRefusedByTheUseCaseRatherThanByTheRoute()
    {
        // Arrange
        var deployment = new UserRecordDeployment([]);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => UserRecordEndpoints.ReadRosterAsync(deployment.Roster, TestContext.Current.CancellationToken));
    }

    private static string AssertRefusal(IResult result, int expectedStatus)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);

        Assert.Equal(expectedStatus, problem.StatusCode);

        return problem.ProblemDetails.Detail!;
    }
}
