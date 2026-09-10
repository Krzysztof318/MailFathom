// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
/// Covers the surface a user maintains their own record over. What separates it from the administrative one is that
/// no request names a user: the caller's own identity is the whole of who the change is for, so what these hold is
/// that the record acted on is the signed-in caller's, that the answer carries nothing about anybody else, and that a
/// caller acting for nobody's mail is refused rather than resolved to whoever the deployment happens to hold.
/// </summary>
public sealed class ClientUserRecordEndpointTests
{
    private const string EmptyRecord = "{}";

    [Fact]
    public async Task ReadAsync_AUserSignedIn_HandsThemTheirOwnRecordAndTheVersionAChangeIsAcceptedAgainst()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailRead);
        deployment.Holding(SyntheticMailUser.Deployment, EmptyRecord, version: 2);

        // Act
        var result = await ClientUserRecordEndpoint.ReadAsync(
            deployment.Records,
            TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.IsType<Ok<UserRecordResponse>>(result.Result).Value!;

        Assert.Equal(SyntheticMailUser.Deployment.Value, record.User);
        Assert.Equal(2, record.Version);
    }

    /// <summary>
    /// The record read is resolved from whoever was admitted, so a deployment holding somebody else's record answers
    /// this caller about their own and never about that one.
    /// </summary>
    [Fact]
    public async Task ReadAsync_ADeploymentAlsoHoldingAnotherUsersRecord_ReadsOnlyTheSignedInUsers()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailRead);
        deployment.Holding(SyntheticMailUser.Deployment, EmptyRecord, version: 1);
        deployment.Holding(SyntheticMailUser.Another, """{"MailAccounts":[{"AccountId":"not-theirs"}]}""", version: 9);

        // Act
        var result = await ClientUserRecordEndpoint.ReadAsync(
            deployment.Records,
            TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.IsType<Ok<UserRecordResponse>>(result.Result).Value!;

        Assert.Equal(SyntheticMailUser.Deployment.Value, record.User);
        Assert.DoesNotContain("not-theirs", record.Document, StringComparison.Ordinal);
    }

    /// <summary>Reached where the row behind an authenticated caller has gone, which is a user erased under a credential that has not yet been withdrawn.</summary>
    [Fact]
    public async Task ReadAsync_ACallerWhoseRowHasGone_AnswersThatThereIsNoRecord()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailRead);

        // Act
        var result = await ClientUserRecordEndpoint.ReadAsync(
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
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailRead);
        deployment.Holding(SyntheticMailUser.Deployment, """{"MailAccounts":[{"AccountId":"alex-private"}""", version: 1);

        // Act
        var result = await ClientUserRecordEndpoint.ReadAsync(
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
        var deployment = new UserRecordDeployment([MailFathomPermission.MailRead]);
        deployment.Holding(SyntheticMailUser.Deployment, EmptyRecord, version: 1);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => ClientUserRecordEndpoint.ReadAsync(deployment.Records, TestContext.Current.CancellationToken));
    }

    /// <summary>Maintaining a record is a grant of its own, so a caller holding only the read cannot write with it.</summary>
    [Fact]
    public async Task AddMailAccountAsync_ACallerHoldingOnlyTheMailRead_IsRefused()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailRead);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => ClientUserRecordEndpoint.AddMailAccountAsync(
                deployment.Records,
                new UserMailAccountRequest(1, """{"AccountId":"archive"}"""),
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A user declares one more mailbox of their own, and the commit is composed over the version they read. The
    /// credential it names is material provisioned for this user — which is what a user-written record may name,
    /// and what the operator declares by naming the material after the person it belongs to.
    /// </summary>
    [Fact]
    public async Task AddMailAccountAsync_ADeclarationTheRecordAccepts_CommitsItToTheSignedInUsersRecord()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailAccountsWrite);
        deployment.Holding(SyntheticMailUser.Deployment, EmptyRecord, version: 4);

        // Act
        var result = await ClientUserRecordEndpoint.AddMailAccountAsync(
            deployment.Records,
            new UserMailAccountRequest(4, AccountProvisionedFor(SyntheticMailUser.Deployment, "archive")),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Assert.IsType<Ok<UserRecordWriteResponse>>(result.Result).Value!.Committed);
        await deployment.Store.Received(1).CommitAsync(
            SyntheticMailUser.Deployment,
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
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailAccountsWrite);

        // Act
        var result = await ClientUserRecordEndpoint.SaveAsync(
            deployment.Records,
            new UserRecordSaveRequest(1, document),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result);
    }

    [Fact]
    public async Task SaveAsync_ARequestStatingANegativeVersion_IsRefusedWithoutReachingTheStore()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailAccountsWrite);

        // Act
        var result = await ClientUserRecordEndpoint.SaveAsync(
            deployment.Records,
            new UserRecordSaveRequest(-1, EmptyRecord),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result);
        await deployment.Documents.DidNotReceiveWithAnyArgs().ReadAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The whole-record save is the widest write this surface publishes, so it is the one that has to prove the bound
    /// on what a user may name. A record naming material provisioned for them commits as an ordinary write.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ARecordNamingMaterialProvisionedForThem_CommitsToTheSignedInUsersRecord()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailAccountsWrite);
        deployment.Holding(SyntheticMailUser.Deployment, EmptyRecord, version: 6);

        // Act
        var result = await ClientUserRecordEndpoint.SaveAsync(
            deployment.Records,
            new UserRecordSaveRequest(6, RecordDeclaring(AccountProvisionedFor(SyntheticMailUser.Deployment, "archive"))),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Assert.IsType<Ok<UserRecordWriteResponse>>(result.Result).Value!.Committed);
        await deployment.Store.Received(1).CommitAsync(
            SyntheticMailUser.Deployment,
            Arg.Any<string>(),
            6,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The same route with a reference outside this user's own material, which is the write that would hand one person
    /// another's credential to present to a mail server. It is refused before the commit, so a save that pasted the
    /// whole record past the narrower routes reaches the same bound they do.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ARecordNamingAnotherUsersCredential_IsRefusedBeforeItIsCommitted()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailAccountsWrite);
        deployment.Holding(SyntheticMailUser.Deployment, EmptyRecord, version: 6);

        // Act
        var result = await ClientUserRecordEndpoint.SaveAsync(
            deployment.Records,
            new UserRecordSaveRequest(6, RecordDeclaring(AccountProvisionedFor(SyntheticMailUser.Another, "archive"))),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(Assert.IsType<Ok<UserRecordWriteResponse>>(result.Result).Value!.Committed);
        await deployment.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveMailAccountAsync_ARequestNamingNoAccount_IsRefused(string? accountId)
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailAccountsWrite);

        // Act
        var result = await ClientUserRecordEndpoint.RemoveMailAccountAsync(
            deployment.Records,
            new UserMailAccountRemovalRequest(1, accountId),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result);
    }

    /// <summary>The mail already stored for that account stays: what this does is stop the deployment reading the mailbox, and erasing it is a separate act.</summary>
    [Fact]
    public async Task RemoveMailAccountAsync_AnIdentifierTheirRecordDeclares_CommitsTheRecordWithoutIt()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailAccountsWrite);
        deployment.Holding(
            SyntheticMailUser.Deployment,
            $$"""{ "MailAccounts": [ {{Account("primary")}}, {{Account("archive")}} ] }""",
            version: 1);

        // Act
        var result = await ClientUserRecordEndpoint.RemoveMailAccountAsync(
            deployment.Records,
            new UserMailAccountRemovalRequest(1, "archive"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Assert.IsType<Ok<UserRecordWriteResponse>>(result.Result).Value!.Committed);
        await deployment.Store.Received(1).CommitAsync(
            SyntheticMailUser.Deployment,
            Arg.Is<string>(candidate => !candidate!.Contains("archive", StringComparison.Ordinal)),
            1,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A refusal about the record itself arrives as an outcome with a success status, because it is something the user
    /// acts on and continues from — and it carries the version they compose the next attempt over. The declaration is
    /// one this user may name, so the version is the only rule left that can refuse it: a reference the secret bound
    /// rejects would answer the same two values from a rule this test does not name.
    /// </summary>
    [Fact]
    public async Task AddMailAccountAsync_AVersionSomebodyElseHasMovedPast_AnswersWithTheOutcomeRatherThanAnError()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailAccountsWrite);
        deployment.Holding(SyntheticMailUser.Deployment, EmptyRecord, version: 8);

        // Act
        var result = await ClientUserRecordEndpoint.AddMailAccountAsync(
            deployment.Records,
            new UserMailAccountRequest(3, AccountProvisionedFor(SyntheticMailUser.Deployment, "archive")),
            TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.IsType<Ok<UserRecordWriteResponse>>(result.Result).Value!;

        Assert.False(written.Committed);
        Assert.Equal(8, written.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddFolderAsync_ARequestNamingNoAccount_IsRefused(string? accountId)
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailAccountsWrite);

        // Act
        var result = await ClientUserRecordEndpoint.AddFolderAsync(
            deployment.Records,
            new UserFolderRequest(1, accountId, """{"Alias":"INBOX/PROJECTS"}"""),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result);
    }

    [Fact]
    public async Task AddFolderAsync_AFolderTheirRecordAccepts_CommitsItIntoThatAccount()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailAccountsWrite);
        deployment.Holding(SyntheticMailUser.Deployment, RecordDeclaring(Account("primary")), version: 1);

        // Act
        var result = await ClientUserRecordEndpoint.AddFolderAsync(
            deployment.Records,
            new UserFolderRequest(1, "primary", """{"Alias":"INBOX/PROJECTS","RemotePath":"INBOX/Projects","CreateIfMissing":true}"""),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Assert.IsType<Ok<UserRecordWriteResponse>>(result.Result).Value!.Committed);
        await deployment.Store.Received(1).CommitAsync(
            SyntheticMailUser.Deployment,
            Arg.Is<string>(candidate => candidate!.Contains("INBOX/PROJECTS", StringComparison.Ordinal)),
            1,
            Arg.Any<CancellationToken>());
    }

    /// <summary>The three-level ceiling reaches the caller as a sentence they can act on rather than as a fault the process reports.</summary>
    [Fact]
    public async Task AddFolderAsync_AnAliasNestedPastThreeLevels_AnswersARefusalNamingItAndCommitsNothing()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailAccountsWrite);
        deployment.Holding(SyntheticMailUser.Deployment, RecordDeclaring(Account("primary")), version: 1);

        // Act
        var result = await ClientUserRecordEndpoint.AddFolderAsync(
            deployment.Records,
            new UserFolderRequest(1, "primary", """{"Alias":"INBOX/PROJECTS/2027/Q1","RemotePath":"INBOX/Projects/2027/Q1"}"""),
            TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<UserRecordWriteResponse>>(result.Result).Value!;
        Assert.False(answered.Committed);
        Assert.Contains(answered.Messages, said => said.Contains("INBOX/PROJECTS/2027/Q1", StringComparison.Ordinal));
        await deployment.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>A grant that reads mail has not thereby been granted the ability to change what this deployment reads.</summary>
    [Fact]
    public async Task AddFolderAsync_ACallerHoldingOnlyTheMailRead_IsRefused()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailRead);

        // Act and assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => ClientUserRecordEndpoint.AddFolderAsync(
                deployment.Records,
                new UserFolderRequest(1, "primary", """{"Alias":"INBOX"}"""),
                TestContext.Current.CancellationToken));
    }

    /// <summary>Renaming is where the alias travelling beside the declaration earns itself: the two deliberately differ.</summary>
    [Fact]
    public async Task ReplaceFolderAsync_AFolderRenamed_CommitsTheRecordCarryingTheNewAliasAndNotTheOld()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailAccountsWrite);
        deployment.Holding(
            SyntheticMailUser.Deployment,
            $$"""{ "MailAccounts": [ {{AccountDeclaringFolder("primary", "INBOX/OLD")}} ] }""",
            version: 1);

        // Act
        var result = await ClientUserRecordEndpoint.ReplaceFolderAsync(
            deployment.Records,
            new UserFolderReplacementRequest(1, "primary", "INBOX/OLD", """{"Alias":"INBOX/NEW","RemotePath":"INBOX/New"}"""),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Assert.IsType<Ok<UserRecordWriteResponse>>(result.Result).Value!.Committed);
        await deployment.Store.Received(1).CommitAsync(
            SyntheticMailUser.Deployment,
            Arg.Is<string>(candidate =>
                candidate!.Contains("INBOX/NEW", StringComparison.Ordinal)
                && !candidate.Contains("INBOX/OLD", StringComparison.Ordinal)),
            1,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReplaceFolderAsync_ARequestNamingNoFolder_IsRefused(string? alias)
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailAccountsWrite);

        // Act
        var result = await ClientUserRecordEndpoint.ReplaceFolderAsync(
            deployment.Records,
            new UserFolderReplacementRequest(1, "primary", alias, """{"Alias":"INBOX"}"""),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReplaceFolderAsync_ARequestNamingNoAccount_IsRefused(string? accountId)
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailAccountsWrite);

        // Act
        var result = await ClientUserRecordEndpoint.ReplaceFolderAsync(
            deployment.Records,
            new UserFolderReplacementRequest(1, accountId, "INBOX/OLD", """{"Alias":"INBOX/NEW"}"""),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveFolderAsync_ARequestNamingNoAccount_IsRefused(string? accountId)
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailAccountsWrite);

        // Act
        var result = await ClientUserRecordEndpoint.RemoveFolderAsync(
            deployment.Records,
            new UserFolderRemovalRequest(1, accountId, "INBOX/PROJECTS"),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveFolderAsync_ARequestNamingNoFolder_IsRefused(string? alias)
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailAccountsWrite);

        // Act
        var result = await ClientUserRecordEndpoint.RemoveFolderAsync(
            deployment.Records,
            new UserFolderRemovalRequest(1, "primary", alias),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result);
    }

    /// <summary>An alias the account does not declare is a name the caller got wrong, and it is reported as one rather than committed as nothing.</summary>
    [Fact]
    public async Task RemoveFolderAsync_AnAliasTheAccountDoesNotDeclare_CommitsNothing()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticMailUser.Deployment, MailFathomPermission.MailAccountsWrite);
        deployment.Holding(
            SyntheticMailUser.Deployment,
            $$"""{ "MailAccounts": [ {{AccountDeclaringFolder("primary", "INBOX")}} ] }""",
            version: 1);

        // Act
        var result = await ClientUserRecordEndpoint.RemoveFolderAsync(
            deployment.Records,
            new UserFolderRemovalRequest(1, "primary", "INBOX/PROJECTS"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(Assert.IsType<Ok<UserRecordWriteResponse>>(result.Result).Value!.Committed);
        await deployment.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, TestContext.Current.CancellationToken);
    }

    private static UserRecordDeployment SignedInAs(MailUserId user, MailFathomPermission granted) =>
        new([granted], user);

    /// <summary>A mailbox already declaring one folder, which the two acts that find one by alias are held against.</summary>
    private static string AccountDeclaringFolder(string accountId, string alias) =>
        $$"""
          {
            "AccountId": "{{accountId}}",
            "DisplayName": "{{accountId}}",
            "Host": "imap.example.test",
            "UserName": "mailfathom@example.test",
            "Secrets": { "Password": { "Name": "{{accountId}}-password", "SecretReference": "file:/run/secrets/{{accountId}}-password" } },
            "Folders": [ { "Alias": "{{alias}}", "RemotePath": "{{alias}}" } ]
          }
          """;

    /// <summary>A mailbox whose credential this deployment provisioned for one user, which its name is what says.</summary>
    private static string AccountProvisionedFor(MailUserId user, string accountId) =>
        $$"""
          {
            "AccountId": "{{accountId}}",
            "DisplayName": "{{accountId}}",
            "Host": "imap.example.test",
            "UserName": "mailfathom@example.test",
            "Secrets": { "Password": { "Name": "{{accountId}}-password", "SecretReference": "file:/run/secrets/user-{{user.Value:D}}-{{accountId}}" } }
          }
          """;

    /// <summary>A whole record declaring one mailbox, which is what the save route is handed.</summary>
    private static string RecordDeclaring(string account) => $$"""{ "MailAccounts": [ {{account}} ] }""";

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
