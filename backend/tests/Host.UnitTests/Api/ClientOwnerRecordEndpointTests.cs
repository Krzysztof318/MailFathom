// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>
/// Covers the surface an owner maintains their own record over. What separates it from the administrative one is that
/// no request names an owner: the caller's own identity is the whole of who the change is for, so what these hold is
/// that the record acted on is the signed-in caller's, that the answer carries nothing about anybody else, and that a
/// caller acting for nobody's mail is refused rather than resolved to whoever the deployment happens to hold.
/// </summary>
public sealed class ClientOwnerRecordEndpointTests
{
    private const string EmptyRecord = "{}";

    [Fact]
    public async Task ReadAsync_AnOwnerSignedIn_HandsThemTheirOwnRecordAndTheVersionAChangeIsAcceptedAgainst()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailOwner.Deployment, MailFathomPermission.MailRead);
        deployment.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 2);

        // Act
        var result = await ClientOwnerRecordEndpoint.ReadAsync(
            deployment.Records,
            TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.IsType<Ok<OwnerRecordResponse>>(result.Result).Value!;

        Assert.Equal(SyntheticMailOwner.Deployment.Value, record.Owner);
        Assert.Equal(2, record.Version);
    }

    /// <summary>
    /// The record read is resolved from whoever was admitted, so a deployment holding somebody else's record answers
    /// this caller about their own and never about that one.
    /// </summary>
    [Fact]
    public async Task ReadAsync_ADeploymentAlsoHoldingAnotherOwnersRecord_ReadsOnlyTheSignedInOwners()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailOwner.Deployment, MailFathomPermission.MailRead);
        deployment.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 1);
        deployment.Holding(SyntheticMailOwner.Another, """{"MailAccounts":[{"AccountId":"not-theirs"}]}""", version: 9);

        // Act
        var result = await ClientOwnerRecordEndpoint.ReadAsync(
            deployment.Records,
            TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.IsType<Ok<OwnerRecordResponse>>(result.Result).Value!;

        Assert.Equal(SyntheticMailOwner.Deployment.Value, record.Owner);
        Assert.DoesNotContain("not-theirs", record.Document, StringComparison.Ordinal);
    }

    /// <summary>Reached where the row behind an authenticated caller has gone, which is an owner erased under a credential that has not yet been withdrawn.</summary>
    [Fact]
    public async Task ReadAsync_ACallerWhoseRowHasGone_AnswersThatThereIsNoRecord()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailOwner.Deployment, MailFathomPermission.MailRead);

        // Act
        var result = await ClientOwnerRecordEndpoint.ReadAsync(
            deployment.Records,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound<ProblemDetails>>(result.Result);
    }

    /// <summary>The parser's message names the JSON path it stopped at, and that path is composed from this person's own mailboxes.</summary>
    [Fact]
    public async Task ReadAsync_ARowThatIsNotADocumentOfSettings_IsRefusedWithoutRepeatingWhatTheParserSaw()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailOwner.Deployment, MailFathomPermission.MailRead);
        deployment.Holding(SyntheticMailOwner.Deployment, """{"MailAccounts":[{"AccountId":"alex-private"}""", version: 1);

        // Act
        var result = await ClientOwnerRecordEndpoint.ReadAsync(
            deployment.Records,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = AssertRefusal(result.Result);

        Assert.DoesNotContain("alex-private", refusal, StringComparison.Ordinal);
    }

    /// <summary>A caller admitted for nobody's mail is an entrypoint that never said whose record it wanted, which is refused rather than resolved.</summary>
    [Fact]
    public async Task ReadAsync_ACallerActingForNobody_IsRefusedRatherThanResolvedToWhoeverTheDeploymentHolds()
    {
        // Arrange
        var deployment = new OwnerRecordDeployment([MailFathomPermission.MailRead]);
        deployment.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 1);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => ClientOwnerRecordEndpoint.ReadAsync(deployment.Records, TestContext.Current.CancellationToken));
    }

    /// <summary>Maintaining a record is a grant of its own, so a caller holding only the read cannot write with it.</summary>
    [Fact]
    public async Task AddMailAccountAsync_ACallerHoldingOnlyTheMailRead_IsRefused()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailOwner.Deployment, MailFathomPermission.MailRead);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => ClientOwnerRecordEndpoint.AddMailAccountAsync(
                deployment.Records,
                new OwnerMailAccountRequest(1, """{"AccountId":"archive"}"""),
                TestContext.Current.CancellationToken));
    }

    /// <summary>An owner declares one more mailbox of their own, and the commit is composed over the version they read.</summary>
    [Fact]
    public async Task AddMailAccountAsync_ADeclarationTheRecordAccepts_CommitsItToTheSignedInOwnersRecord()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailOwner.Deployment, MailFathomPermission.MailAccountsWrite);
        deployment.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 4);

        // Act
        var result = await ClientOwnerRecordEndpoint.AddMailAccountAsync(
            deployment.Records,
            new OwnerMailAccountRequest(4, Account("archive")),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Assert.IsType<Ok<OwnerRecordWriteResponse>>(result.Result).Value!.Committed);
        await deployment.Store.Received(1).CommitAsync(
            SyntheticMailOwner.Deployment,
            Arg.Any<string>(),
            4,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task SaveAsync_ARequestCarryingNoRecord_IsRefused(string? document)
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailOwner.Deployment, MailFathomPermission.MailAccountsWrite);

        // Act
        var result = await ClientOwnerRecordEndpoint.SaveAsync(
            deployment.Records,
            new OwnerRecordSaveRequest(1, document),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result);
    }

    [Fact]
    public async Task SaveAsync_ARequestStatingANegativeVersion_IsRefusedWithoutReachingTheStore()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailOwner.Deployment, MailFathomPermission.MailAccountsWrite);

        // Act
        var result = await ClientOwnerRecordEndpoint.SaveAsync(
            deployment.Records,
            new OwnerRecordSaveRequest(-1, EmptyRecord),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result);
        await deployment.Documents.DidNotReceiveWithAnyArgs().ReadAsync(default, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveMailAccountAsync_ARequestNamingNoAccount_IsRefused(string? accountId)
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailOwner.Deployment, MailFathomPermission.MailAccountsWrite);

        // Act
        var result = await ClientOwnerRecordEndpoint.RemoveMailAccountAsync(
            deployment.Records,
            new OwnerMailAccountRemovalRequest(1, accountId),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result);
    }

    /// <summary>The mail already stored for that account stays: what this does is stop the deployment reading the mailbox, and erasing it is a separate act.</summary>
    [Fact]
    public async Task RemoveMailAccountAsync_AnIdentifierTheirRecordDeclares_CommitsTheRecordWithoutIt()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailOwner.Deployment, MailFathomPermission.MailAccountsWrite);
        deployment.Holding(
            SyntheticMailOwner.Deployment,
            $$"""{ "MailAccounts": [ {{Account("primary")}}, {{Account("archive")}} ] }""",
            version: 1);

        // Act
        var result = await ClientOwnerRecordEndpoint.RemoveMailAccountAsync(
            deployment.Records,
            new OwnerMailAccountRemovalRequest(1, "archive"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Assert.IsType<Ok<OwnerRecordWriteResponse>>(result.Result).Value!.Committed);
        await deployment.Store.Received(1).CommitAsync(
            SyntheticMailOwner.Deployment,
            Arg.Is<string>(candidate => !candidate!.Contains("archive", StringComparison.Ordinal)),
            1,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A refusal about the record itself arrives as an outcome with a success status, because it is something the owner
    /// acts on and continues from — and it carries the version they compose the next attempt over.
    /// </summary>
    [Fact]
    public async Task AddMailAccountAsync_AVersionSomebodyElseHasMovedPast_AnswersWithTheOutcomeRatherThanAnError()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailOwner.Deployment, MailFathomPermission.MailAccountsWrite);
        deployment.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 8);

        // Act
        var result = await ClientOwnerRecordEndpoint.AddMailAccountAsync(
            deployment.Records,
            new OwnerMailAccountRequest(3, Account("archive")),
            TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.IsType<Ok<OwnerRecordWriteResponse>>(result.Result).Value!;

        Assert.False(written.Committed);
        Assert.Equal(8, written.Version);
    }

    private static OwnerRecordDeployment SignedInAs(MailOwnerId owner, MailFathomPermission granted) =>
        new([granted], owner);

    private static string Account(string accountId) =>
        $$"""
          {
            "AccountId": "{{accountId}}",
            "DisplayName": "{{accountId}}",
            "Host": "imap.example.test",
            "UserName": "mailfathom@example.test",
            "Secrets": { "Password": { "Name": "{{accountId}}-password", "SecretReference": "file:/run/secrets/{{accountId}}-password" } }
          }
          """;

    private static string AssertRefusal(IResult result)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);

        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);

        return problem.ProblemDetails.Detail!;
    }
}
