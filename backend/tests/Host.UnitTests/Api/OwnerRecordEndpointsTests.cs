// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.OwnerSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>
/// Covers what the administrative owner routes answer. The rules underneath are proved where they live, so what these
/// hold is the boundary's own share: an identifier the type will not carry, a version no write can have been composed
/// over, a body carrying nothing to act on, and the one answer a caller must never be able to tell apart from another —
/// an owner this deployment does not hold.
/// </summary>
public sealed class OwnerRecordEndpointsTests
{
    private const string EmptyRecord = "{}";

    [Fact]
    public async Task ReadRosterAsync_ADeploymentHoldingOwners_ReportsEachOneWithTheLabelAnAdministratorSelectsBy()
    {
        // Arrange
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminRead]);
        deployment.Held(new MailOwnerRecord(SyntheticMailOwner.Deployment, "alex", DocumentWrittenAtRuntime: true));

        // Act
        var result = await OwnerRecordEndpoints.ReadRosterAsync(
            deployment.Roster,
            TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(result.Value!.Owners);

        Assert.Equal(SyntheticMailOwner.Deployment.Value, entry.Id);
        Assert.Equal("alex", entry.DisplayName);
        Assert.True(entry.RecordIsTheirOwn);
    }

    [Fact]
    public async Task ProvisionAsync_ALabelTheDeploymentAccepts_ReportsTheIdentifierItMinted()
    {
        // Arrange
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await OwnerRecordEndpoints.ProvisionAsync(
            deployment.Roster,
            new OwnerProvisioningRequest("alex"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(Guid.Empty, Assert.IsType<Ok<OwnerProvisionedResponse>>(result.Result).Value!.Id);
    }

    /// <summary>A refusal is a request the administrator corrects, so it names what to change rather than reporting that something failed.</summary>
    [Fact]
    public async Task ProvisionAsync_ARequestCarryingNoLabel_IsRefusedNamingWhatHasToChange()
    {
        // Arrange
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await OwnerRecordEndpoints.ProvisionAsync(
            deployment.Roster,
            new OwnerProvisioningRequest(DisplayName: null),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result, StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// An owner this deployment does not hold is reported as nothing erased rather than as a refusal, because the
    /// caller asked for a state and the deployment is in it — and telling the two apart would report which owner
    /// identifiers exist.
    /// </summary>
    [Fact]
    public async Task EraseAsync_AnOwnerThisDeploymentDoesNotHold_ReportsNothingErasedRatherThanRefusing()
    {
        // Arrange
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminErase]);

        // Act
        var result = await OwnerRecordEndpoints.EraseAsync(
            SyntheticMailOwner.Another.Value,
            deployment.Roster,
            TestContext.Current.CancellationToken);

        // Assert
        var erasure = Assert.IsType<Ok<OwnerErasureResponse>>(result.Result).Value!;

        Assert.False(erasure.Erased);
        Assert.False(erasure.WasServed);
    }

    /// <summary>A restart is owed where the process was serving the person it removed, and nothing else would tell an operator so.</summary>
    [Fact]
    public async Task EraseAsync_AnOwnerThisProcessIsServing_ReportsThatItWasServingThem()
    {
        // Arrange
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminErase]);
        deployment.Serving(new ServedMailOwner(SyntheticMailOwner.Deployment, "alex", MailOwnerAccountSource.OwnerDocument, []));
        deployment.Erasure.EraseAsync(SyntheticMailOwner.Deployment, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var result = await OwnerRecordEndpoints.EraseAsync(
            SyntheticMailOwner.Deployment.Value,
            deployment.Roster,
            TestContext.Current.CancellationToken);

        // Assert
        var erasure = Assert.IsType<Ok<OwnerErasureResponse>>(result.Result).Value!;

        Assert.True(erasure.Erased);
        Assert.True(erasure.WasServed);
    }

    [Fact]
    public async Task EraseAsync_ARequestNamingNoOwner_IsRefusedWithoutReachingTheErasure()
    {
        // Arrange
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminErase]);

        // Act
        var result = await OwnerRecordEndpoints.EraseAsync(
            Guid.Empty,
            deployment.Roster,
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result, StatusCodes.Status400BadRequest);
        await deployment.Erasure.DidNotReceiveWithAnyArgs()
            .EraseAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>The label the request carried is the whole of what changed, so acceptance is the whole answer.</summary>
    [Fact]
    public async Task RelabelAsync_AnOwnerThisDeploymentHolds_AnswersWithNoContent()
    {
        // Arrange
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);
        deployment.Held(new MailOwnerRecord(SyntheticMailOwner.Deployment, "alexandra", DocumentWrittenAtRuntime: true));

        // Act
        var result = await OwnerRecordEndpoints.RelabelAsync(
            SyntheticMailOwner.Deployment.Value,
            deployment.Roster,
            new OwnerRelabelRequest("alex"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NoContent>(result.Result);
        await deployment.Provisioning.Received(1)
            .RelabelAsync(SyntheticMailOwner.Deployment, "alex", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An owner this deployment does not hold is the same answer here as at every other route that names one in its
    /// own path: the record is not there. Answering a refusal instead would make this the one owner-scoped route where
    /// an absent owner reads as a request that was wrong, and would leave a caller granted the write but not the read
    /// able to tell an owner who exists from one who does not.
    /// </summary>
    [Fact]
    public async Task RelabelAsync_AnOwnerThisDeploymentDoesNotHold_AnswersThatThereIsNoSuchRecord()
    {
        // Arrange
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await OwnerRecordEndpoints.RelabelAsync(
            SyntheticMailOwner.Another.Value,
            deployment.Roster,
            new OwnerRelabelRequest("sam"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound<ProblemDetails>>(result.Result);
        await deployment.Provisioning.DidNotReceiveWithAnyArgs()
            .RelabelAsync(default, default!, CancellationToken.None);
    }

    [Fact]
    public async Task RelabelAsync_ARequestNamingNoOwner_IsRefusedWithoutReachingTheRow()
    {
        // Arrange
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await OwnerRecordEndpoints.RelabelAsync(
            Guid.Empty,
            deployment.Roster,
            new OwnerRelabelRequest("alex"),
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
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminRead]);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => OwnerRecordEndpoints.RelabelAsync(
            SyntheticMailOwner.Deployment.Value,
            deployment.Roster,
            new OwnerRelabelRequest("alex"),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadRecordAsync_AnOwnerThisDeploymentHolds_ReportsTheRecordAndTheVersionAChangeIsAcceptedAgainst()
    {
        // Arrange
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminRead]);
        deployment.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 3);

        // Act
        var result = await OwnerRecordEndpoints.ReadRecordAsync(
            SyntheticMailOwner.Deployment.Value,
            deployment.Records,
            TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.IsType<Ok<OwnerRecordResponse>>(result.Result).Value!;

        Assert.Equal(SyntheticMailOwner.Deployment.Value, record.Owner);
        Assert.Equal(3, record.Version);
        Assert.False(record.ReadFromConfiguration);
    }

    /// <summary>The same answer an owner this deployment genuinely does not hold receives, which is what keeps a caller from learning which identifiers exist by asking about them.</summary>
    [Fact]
    public async Task ReadRecordAsync_AnOwnerThisDeploymentDoesNotHold_AnswersWithoutSayingAnythingAboutTheIdentifier()
    {
        // Arrange
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminRead]);

        // Act
        var result = await OwnerRecordEndpoints.ReadRecordAsync(
            SyntheticMailOwner.Another.Value,
            deployment.Records,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<NotFound<ProblemDetails>>(result.Result).Value!;

        Assert.DoesNotContain(
            SyntheticMailOwner.Another.Value.ToString("D"),
            refusal.Detail!,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The parser's own message names the offending token, the JSON path it stopped at, and a byte position — and that
    /// path is composed from the row's own key names, which for an owner's record are their mailboxes.
    /// </summary>
    [Fact]
    public async Task ReadRecordAsync_ARowThatIsNotADocumentOfSettings_IsRefusedWithoutRepeatingWhatTheParserSaw()
    {
        // Arrange
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminRead]);
        deployment.Holding(SyntheticMailOwner.Deployment, """{"MailAccounts":[{"AccountId":"alex-private"}""", version: 1);

        // Act
        var result = await OwnerRecordEndpoints.ReadRecordAsync(
            SyntheticMailOwner.Deployment.Value,
            deployment.Records,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = AssertRefusal(result.Result, StatusCodes.Status400BadRequest);

        Assert.DoesNotContain("alex-private", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadRecordAsync_ARequestNamingNoOwner_IsRefusedWithoutReachingTheStore()
    {
        // Arrange
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminRead]);

        // Act
        var result = await OwnerRecordEndpoints.ReadRecordAsync(
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
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await OwnerRecordEndpoints.SaveRecordAsync(
            SyntheticMailOwner.Deployment.Value,
            deployment.Records,
            new OwnerRecordSaveRequest(-1, EmptyRecord),
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
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await OwnerRecordEndpoints.SaveRecordAsync(
            SyntheticMailOwner.Deployment.Value,
            deployment.Records,
            new OwnerRecordSaveRequest(1, document),
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
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await OwnerRecordEndpoints.AddMailAccountAsync(
            SyntheticMailOwner.Deployment.Value,
            deployment.Records,
            new OwnerMailAccountRequest(1, account),
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
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await OwnerRecordEndpoints.RemoveMailAccountAsync(
            SyntheticMailOwner.Deployment.Value,
            deployment.Records,
            new OwnerMailAccountRemovalRequest(1, accountId),
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
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);
        deployment.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 6);

        // Act
        var result = await OwnerRecordEndpoints.AddMailAccountAsync(
            SyntheticMailOwner.Deployment.Value,
            deployment.Records,
            new OwnerMailAccountRequest(2, """{"AccountId":"archive"}"""),
            TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.IsType<Ok<OwnerRecordWriteResponse>>(result.Result).Value!;

        Assert.False(written.Committed);
        Assert.Equal(6, written.Version);
        Assert.Equal(MailFathomErrorCode.ConfigurationVersionSuperseded.Value, written.Code);
    }

    [Fact]
    public async Task AddMailAccountAsync_AnOwnerThisDeploymentDoesNotHold_AnswersThatThereIsNoSuchOwner()
    {
        // Arrange
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await OwnerRecordEndpoints.AddMailAccountAsync(
            SyntheticMailOwner.Another.Value,
            deployment.Records,
            new OwnerMailAccountRequest(1, """{"AccountId":"archive"}"""),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound<ProblemDetails>>(result.Result);
    }

    [Fact]
    public async Task StoreSecretAsync_AnExistingOwner_ReturnsOnlyTheDatabaseReference()
    {
        // Arrange
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);
        deployment.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 1);

        // Act
        var result = await OwnerRecordEndpoints.StoreSecretAsync(
            SyntheticMailOwner.Deployment.Value,
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
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await OwnerRecordEndpoints.StoreSecretAsync(
            SyntheticMailOwner.Deployment.Value,
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

    /// <summary>The preview names what stops deciding this owner's mailboxes once the adoption commits, which is the part an operator weighs.</summary>
    [Fact]
    public async Task ReadAdoptableAsync_AnOwnerThisDeploymentHolds_ReportsWhetherThereIsAnAdoptionToPerform()
    {
        // Arrange
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminRead]);
        deployment.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 1);
        deployment.Serving(new ServedMailOwner(SyntheticMailOwner.Deployment, "alex", MailOwnerAccountSource.OwnerDocument, []));

        // Act
        var result = await OwnerRecordEndpoints.ReadAdoptableAsync(
            SyntheticMailOwner.Deployment.Value,
            deployment.Records,
            TestContext.Current.CancellationToken);

        // Assert
        var preview = Assert.IsType<Ok<OwnerAdoptionPreviewResponse>>(result.Result).Value!;

        Assert.False(preview.ReadFromConfiguration);
        Assert.Null(preview.ConfigurationPath);
    }

    [Fact]
    public async Task AdoptAsync_ARequestStatingANegativeVersion_IsRefusedWithoutReachingTheStore()
    {
        // Arrange
        var deployment = new OwnerRecordDeployment([MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var result = await OwnerRecordEndpoints.AdoptAsync(
            SyntheticMailOwner.Deployment.Value,
            deployment.Records,
            new OwnerAdoptionRequest(-1),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result, StatusCodes.Status400BadRequest);
        await deployment.Documents.DidNotReceiveWithAnyArgs().ReadAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>Every route asks the use case for its permission with the transport absent, so an entrypoint added later cannot widen the surface by forgetting a route filter.</summary>
    [Fact]
    public async Task ReadRosterAsync_ACallerHoldingNothing_IsRefusedByTheUseCaseRatherThanByTheRoute()
    {
        // Arrange
        var deployment = new OwnerRecordDeployment([]);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => OwnerRecordEndpoints.ReadRosterAsync(deployment.Roster, TestContext.Current.CancellationToken));
    }

    private static string AssertRefusal(IResult result, int expectedStatus)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);

        Assert.Equal(expectedStatus, problem.StatusCode);

        return problem.ProblemDetails.Detail!;
    }
}
