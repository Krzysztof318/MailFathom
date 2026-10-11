// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>
/// Covers the surface a user maintains their own record and their own mail accounts over. What separates it from the
/// administrative one is that no request names a user: the caller's own identity is the whole of who the change is
/// for, so what these hold is that the record acted on is the signed-in caller's, that the answer carries nothing about
/// anybody else, and that a caller acting for nobody's mail is refused rather than resolved to whoever the deployment
/// happens to hold.
/// </summary>
public sealed class ClientUserRecordEndpointTests
{
    /// <summary>The record a provisioning leaves behind, which declares nothing until its user asks for something.</summary>
    private const string EmptyRecord = "{}";

    [Fact]
    public async Task ReadAsync_AUserSignedIn_HandsThemTheirOwnRecordAndTheVersionAChangeIsAcceptedAgainst()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticUser.Deployment, MailFathomPermission.MailRead);
        deployment.Holding(SyntheticUser.Deployment, EmptyRecord, version: 2);

        // Act
        var result = await ClientUserRecordEndpoint.ReadAsync(
            deployment.Records,
            TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.IsType<Ok<UserRecordResponse>>(result.Result).Value!;

        Assert.Equal(SyntheticUser.Deployment.Value, record.User);
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
        var deployment = SignedInAs(SyntheticUser.Deployment, MailFathomPermission.MailRead);
        deployment.Holding(SyntheticUser.Deployment, EmptyRecord, version: 1);
        deployment.Holding(
            SyntheticUser.Another,
            EmptyRecord,
            version: 9,
            Mailbox(SyntheticUser.Another, "not-theirs"));

        // Act
        var result = await ClientUserRecordEndpoint.ReadAsync(
            deployment.Records,
            TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.IsType<Ok<UserRecordResponse>>(result.Result).Value!;

        Assert.Equal(SyntheticUser.Deployment.Value, record.User);
        Assert.DoesNotContain("not-theirs", record.Document, StringComparison.Ordinal);
    }

    /// <summary>
    /// "Changeable" is the person's own answer on this surface, so a client draws a held setting as text: a setting
    /// a policy holds is not theirs, and neither is one only an administrator writes.
    /// </summary>
    [Fact]
    public async Task ReadAsync_ARecordUnderAPolicy_ReportsWhichPropertiesAreThePersonsToChange()
    {
        // Arrange
        var deployment = new UserRecordDeployment(
            [MailFathomPermission.MailRead, MailFathomPermission.MailAccountsWrite],
            SyntheticUser.Deployment);
        deployment.Holding(SyntheticUser.Deployment, EmptyRecord, version: 2);
        deployment.GovernedBy(SyntheticUser.Deployment, StatedPolicies.Governing(deployment: """{"Users":{"Forced":{"TimeZone":"Europe/Warsaw"}}}"""));

        // Act
        var result = await ClientUserRecordEndpoint.ReadAsync(
            deployment.Records,
            TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.IsType<Ok<UserRecordResponse>>(result.Result).Value!;

        Assert.Equal(
            [("EndpointAccess:McpEndpoint", false), ("Language", true), ("TimeZone", false)],
            record.Properties
                .Where(property => property.Path is "TimeZone" or "Language" or "EndpointAccess:McpEndpoint")
                .Select(property => (property.Path, property.Changeable))
                .Order());
        Assert.Equal("Deployment", record.Properties.Single(property => property.Path == "TimeZone").HeldBy);
    }

    /// <summary>
    /// The read needs less than the record's own write does, so a caller granted the read alone is told a property is
    /// theirs to change only where a route of their own writes it: the zone and the portrait, and nothing the save
    /// they would be refused carries.
    /// </summary>
    [Fact]
    public async Task ReadAsync_ACallerWhoseGrantOmitsTheRecordWrite_ReportsOnlyWhatARouteOfTheirOwnWritesAsChangeable()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticUser.Deployment, MailFathomPermission.MailRead);
        deployment.Holding(SyntheticUser.Deployment, EmptyRecord, version: 2);

        // Act
        var result = await ClientUserRecordEndpoint.ReadAsync(
            deployment.Records,
            TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.IsType<Ok<UserRecordResponse>>(result.Result).Value!;

        Assert.Equal(
            ["Portrait", "TimeZone"],
            record.Properties.Where(property => property.Changeable).Select(property => property.Path).Order());
    }

    /// <summary>
    /// A person reads their accounts as the policies governing them resolve each: what an account is served, where
    /// it came from, who holds it, and whether it is theirs to change, which is the list of folders at most. No
    /// secret-bearing value travels with it.
    /// </summary>
    [Fact]
    public async Task ReadMailAccountsAsync_AnAccountUnderAPolicy_ReportsEachPropertyAsItIsServedAndWhatIsThePersonsToChange()
    {
        // Arrange
        var work = Mailbox(SyntheticUser.Deployment, "work");
        var deployment = new UserRecordDeployment(
            [MailFathomPermission.MailRead, MailFathomPermission.MailAccountsWrite],
            SyntheticUser.Deployment);
        deployment.Holding(SyntheticUser.Deployment, EmptyRecord, version: 2, work);
        deployment.GovernedBy(
            SyntheticUser.Deployment,
            StatedPolicies.Governing(deployment: """{"MailAccounts":{"Forced":{"Host":"imap.corp.example.test"}}}"""));

        // Act
        var result = await ClientUserRecordEndpoint.ReadMailAccountsAsync(
            deployment.MailAccounts,
            TestContext.Current.CancellationToken);

        // Assert
        var accounts = Assert.IsType<Ok<UserMailAccountsResponse>>(result.Result).Value!;
        var held = Assert.Single(accounts.Accounts);
        var host = held.Properties.Single(property => property.Path == "Host");

        Assert.Equal(work.Id.ToString("D"), held.Id);
        Assert.Null(held.Unresolved);
        Assert.Equal("imap.corp.example.test", host.Value!.GetValue<string>());
        Assert.Equal(("DeploymentForced", "Deployment", false), (host.Source, host.HeldBy, host.Changeable));
        Assert.Equal(["Folders"], held.Properties.Where(property => property.Changeable).Select(property => property.Path));
        Assert.Null(held.Properties.Single(property => property.Path == "Secrets:Password").Value);
    }

    [Fact]
    public async Task ReadMailAccountsAsync_ACallerWhoseRowHasGone_AnswersThatThereIsNoRecord()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticUser.Deployment, MailFathomPermission.MailRead);

        // Act
        var result = await ClientUserRecordEndpoint.ReadMailAccountsAsync(
            deployment.MailAccounts,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound<ProblemDetails>>(result.Result);
    }

    /// <summary>Reached where the row behind an authenticated caller has gone, which is a user erased under a credential that has not yet been withdrawn.</summary>
    [Fact]
    public async Task ReadAsync_ACallerWhoseRowHasGone_AnswersThatThereIsNoRecord()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticUser.Deployment, MailFathomPermission.MailRead);

        // Act
        var result = await ClientUserRecordEndpoint.ReadAsync(
            deployment.Records,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound<ProblemDetails>>(result.Result);
    }

    /// <summary>The parser's message names the JSON path it stopped at, and that path is composed from this person's own settings.</summary>
    [Fact]
    public async Task ReadAsync_ARowThatIsNotADocumentOfSettings_IsRefusedWithoutRepeatingWhatTheParserSaw()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticUser.Deployment, MailFathomPermission.MailRead);
        deployment.Holding(SyntheticUser.Deployment, """{"Portraits":[{"Caption":"alex-private"}""", version: 1);

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
        deployment.Holding(SyntheticUser.Deployment, EmptyRecord, version: 1);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => ClientUserRecordEndpoint.ReadAsync(deployment.Records, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task SaveAsync_ARequestCarryingNoRecord_IsRefused(string? document)
    {
        // Arrange
        var deployment = SignedInAs(SyntheticUser.Deployment, MailFathomPermission.MailAccountsWrite);

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
        var deployment = SignedInAs(SyntheticUser.Deployment, MailFathomPermission.MailAccountsWrite);

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
    /// The whole-record save is the widest write this surface publishes, and a mailbox is not part of it: one pasted
    /// into the record is refused before the commit, so the save cannot declare what the administrative endpoint alone
    /// declares.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ARecordNamingMailAccounts_IsRefusedBeforeItIsCommitted()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticUser.Deployment, MailFathomPermission.MailAccountsWrite);
        deployment.Holding(SyntheticUser.Deployment, EmptyRecord, version: 6);

        // Act
        var result = await ClientUserRecordEndpoint.SaveAsync(
            deployment.Records,
            new UserRecordSaveRequest(
                6,
                """{ "MailAccounts": [ { "DisplayName": "archive", "Language": "English", "Host": "imap.example.test" } ] }"""),
            TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.IsType<Ok<UserRecordWriteResponse>>(result.Result).Value!;

        Assert.False(written.Committed);
        Assert.Contains(written.Messages, said => said.Contains("mfctl account edit", StringComparison.Ordinal));
        await deployment.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, default, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddFolderAsync_ARequestNamingNoAccount_IsRefused(string? accountId)
    {
        // Arrange
        var deployment = SignedInAs(SyntheticUser.Deployment, MailFathomPermission.MailAccountsWrite);

        // Act
        var result = await ClientUserRecordEndpoint.AddFolderAsync(
            deployment.MailAccounts,
            new UserFolderRequest(1, accountId, """{"Alias":"INBOX/PROJECTS"}"""),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result);
    }

    [Fact]
    public async Task AddFolderAsync_AFolderTheirAccountAccepts_SavesItIntoThatAccount()
    {
        // Arrange
        var primary = Mailbox(SyntheticUser.Deployment, "primary");
        var deployment = SignedInAs(SyntheticUser.Deployment, MailFathomPermission.MailAccountsWrite);
        deployment.Holding(SyntheticUser.Deployment, EmptyRecord, version: 1, primary);

        // Act
        var result = await ClientUserRecordEndpoint.AddFolderAsync(
            deployment.MailAccounts,
            new UserFolderRequest(
                1,
                primary.Id.ToString("D"),
                """{"Alias":"INBOX/PROJECTS","RemotePath":"INBOX/Projects","CreateIfMissing":true}"""),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Assert.IsType<Ok<UserRecordWriteResponse>>(result.Result).Value!.Committed);
        Assert.Contains(
            "INBOX/PROJECTS",
            Assert.Single(deployment.MailAccountRecords.Accounts).Document,
            StringComparison.Ordinal);
    }

    /// <summary>The three-level ceiling reaches the caller as a sentence they can act on rather than as a fault the process reports.</summary>
    [Fact]
    public async Task AddFolderAsync_AnAliasNestedPastThreeLevels_AnswersARefusalNamingItAndSavesNothing()
    {
        // Arrange
        var primary = Mailbox(SyntheticUser.Deployment, "primary");
        var deployment = SignedInAs(SyntheticUser.Deployment, MailFathomPermission.MailAccountsWrite);
        deployment.Holding(SyntheticUser.Deployment, EmptyRecord, version: 1, primary);

        // Act
        var result = await ClientUserRecordEndpoint.AddFolderAsync(
            deployment.MailAccounts,
            new UserFolderRequest(
                1,
                primary.Id.ToString("D"),
                """{"Alias":"INBOX/PROJECTS/2027/Q1","RemotePath":"INBOX/Projects/2027/Q1"}"""),
            TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<UserRecordWriteResponse>>(result.Result).Value!;
        Assert.False(answered.Committed);
        Assert.Contains(answered.Messages, said => said.Contains("INBOX/PROJECTS/2027/Q1", StringComparison.Ordinal));
        Assert.Equal(primary, Assert.Single(deployment.MailAccountRecords.Accounts));
    }

    /// <summary>A grant that reads mail has not thereby been granted the ability to change what this deployment reads.</summary>
    [Fact]
    public async Task AddFolderAsync_ACallerHoldingOnlyTheMailRead_IsRefused()
    {
        // Arrange
        var deployment = SignedInAs(SyntheticUser.Deployment, MailFathomPermission.MailRead);

        // Act and assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => ClientUserRecordEndpoint.AddFolderAsync(
                deployment.MailAccounts,
                new UserFolderRequest(1, Guid.NewGuid().ToString("D"), """{"Alias":"INBOX"}"""),
                TestContext.Current.CancellationToken));
    }

    /// <summary>Renaming is where the alias travelling beside the declaration earns itself: the two deliberately differ.</summary>
    [Fact]
    public async Task ReplaceFolderAsync_AFolderRenamed_SavesTheAccountCarryingTheNewAliasAndNotTheOld()
    {
        // Arrange
        var primary = Mailbox(SyntheticUser.Deployment, "primary", folderAlias: "INBOX/OLD");
        var deployment = SignedInAs(SyntheticUser.Deployment, MailFathomPermission.MailAccountsWrite);
        deployment.Holding(SyntheticUser.Deployment, EmptyRecord, version: 1, primary);

        // Act
        var result = await ClientUserRecordEndpoint.ReplaceFolderAsync(
            deployment.MailAccounts,
            new UserFolderReplacementRequest(
                1,
                primary.Id.ToString("D"),
                "INBOX/OLD",
                """{"Alias":"INBOX/NEW","RemotePath":"INBOX/New"}"""),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Assert.IsType<Ok<UserRecordWriteResponse>>(result.Result).Value!.Committed);
        var saved = Assert.Single(deployment.MailAccountRecords.Accounts).Document;
        Assert.Contains("INBOX/NEW", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("INBOX/OLD", saved, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReplaceFolderAsync_ARequestNamingNoFolder_IsRefused(string? alias)
    {
        // Arrange
        var deployment = SignedInAs(SyntheticUser.Deployment, MailFathomPermission.MailAccountsWrite);

        // Act
        var result = await ClientUserRecordEndpoint.ReplaceFolderAsync(
            deployment.MailAccounts,
            new UserFolderReplacementRequest(1, Guid.NewGuid().ToString("D"), alias, """{"Alias":"INBOX"}"""),
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
        var deployment = SignedInAs(SyntheticUser.Deployment, MailFathomPermission.MailAccountsWrite);

        // Act
        var result = await ClientUserRecordEndpoint.ReplaceFolderAsync(
            deployment.MailAccounts,
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
        var deployment = SignedInAs(SyntheticUser.Deployment, MailFathomPermission.MailAccountsWrite);

        // Act
        var result = await ClientUserRecordEndpoint.RemoveFolderAsync(
            deployment.MailAccounts,
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
        var deployment = SignedInAs(SyntheticUser.Deployment, MailFathomPermission.MailAccountsWrite);

        // Act
        var result = await ClientUserRecordEndpoint.RemoveFolderAsync(
            deployment.MailAccounts,
            new UserFolderRemovalRequest(1, Guid.NewGuid().ToString("D"), alias),
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefusal(result.Result);
    }

    /// <summary>An alias the account does not declare is a name the caller got wrong, and it is reported as one rather than saved as nothing.</summary>
    [Fact]
    public async Task RemoveFolderAsync_AnAliasTheAccountDoesNotDeclare_SavesNothing()
    {
        // Arrange
        var primary = Mailbox(SyntheticUser.Deployment, "primary", folderAlias: "INBOX");
        var deployment = SignedInAs(SyntheticUser.Deployment, MailFathomPermission.MailAccountsWrite);
        deployment.Holding(SyntheticUser.Deployment, EmptyRecord, version: 1, primary);

        // Act
        var result = await ClientUserRecordEndpoint.RemoveFolderAsync(
            deployment.MailAccounts,
            new UserFolderRemovalRequest(1, primary.Id.ToString("D"), "INBOX/PROJECTS"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(Assert.IsType<Ok<UserRecordWriteResponse>>(result.Result).Value!.Committed);
        Assert.Equal(primary, Assert.Single(deployment.MailAccountRecords.Accounts));
    }

    private static UserRecordDeployment SignedInAs(UserId user, MailFathomPermission granted) =>
        new([granted], user);

    /// <summary>A mailbox already held and assigned, whose credential was provisioned for the user it belongs to.</summary>
    private static MailAccountRecord Mailbox(UserId user, string name, string? folderAlias = null)
    {
        var folders = folderAlias is null
            ? string.Empty
            : $$"""
                ,
                "Folders": [ { "Alias": "{{folderAlias}}", "RemotePath": "{{folderAlias}}" } ]
                """;

        return new MailAccountRecord(
            Guid.NewGuid(),
            $"{name}@example.test",
            name,
            $$"""
              {
                "Language": "English",
                "Host": "imap.example.test",
                "UserName": "{{name}}@example.test",
                "Secrets": { "Password": { "Name": "{{name}}-password", "SecretReference": "file:/run/secrets/user-{{user.Value:D}}-{{name}}" } }{{folders}}
              }
              """,
            Version: 1);
    }

    private static string AssertRefusal(IResult result)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);

        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);

        return problem.ProblemDetails.Detail!;
    }
}
