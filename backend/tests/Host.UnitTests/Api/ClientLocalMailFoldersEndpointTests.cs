// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Folders.Local;
using MailFathom.Application.Jobs;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>
/// Covers what the boundary decides about local folders: which status each refusal answers with and the name it carries,
/// a request naming no account or an identity nobody issued refused before the use case is reached, the listing's order,
/// what an accepted write answers, and the strict binding of every write body.
/// </summary>
public sealed class ClientLocalMailFoldersEndpointTests
{
    [Theory]
    [InlineData(LocalMailFolderRefusal.AccountMissing, StatusCodes.Status404NotFound)]
    [InlineData(LocalMailFolderRefusal.FolderMissing, StatusCodes.Status404NotFound)]
    [InlineData(LocalMailFolderRefusal.ParentMissing, StatusCodes.Status404NotFound)]
    [InlineData(LocalMailFolderRefusal.NameInvalid, StatusCodes.Status400BadRequest)]
    [InlineData(LocalMailFolderRefusal.InboxNameAtTopLevel, StatusCodes.Status400BadRequest)]
    [InlineData(LocalMailFolderRefusal.AccountNotHeld, StatusCodes.Status409Conflict)]
    [InlineData(LocalMailFolderRefusal.ProtectedRole, StatusCodes.Status409Conflict)]
    [InlineData(LocalMailFolderRefusal.NameTaken, StatusCodes.Status409Conflict)]
    [InlineData(LocalMailFolderRefusal.NestedInItself, StatusCodes.Status409Conflict)]
    [InlineData(LocalMailFolderRefusal.TooDeep, StatusCodes.Status409Conflict)]
    [InlineData(LocalMailFolderRefusal.TooManyFolders, StatusCodes.Status409Conflict)]
    public void Refused_EachRefusal_AnswersItsStatusAndCarriesItsOwnName(LocalMailFolderRefusal refusal, int expectedStatus)
    {
        // Act
        var answer = ClientLocalMailFoldersEndpoint.Refused(refusal);

        // Assert
        Assert.Equal(expectedStatus, answer.StatusCode);
        Assert.NotNull(answer.ProblemDetails.Detail);
        Assert.Equal(refusal.ToString(), answer.ProblemDetails.Extensions["refusal"]);
    }

    /// <summary>A refusal added to the use case without an answer here would reach a client as a server failure.</summary>
    [Fact]
    public void Refused_EveryDeclaredRefusal_HasAnAnswer()
    {
        // Act
        var statuses = Enum.GetValues<LocalMailFolderRefusal>().Select(refusal => ClientLocalMailFoldersEndpoint.Refused(refusal).StatusCode);

        // Assert
        Assert.All(statuses, status => Assert.InRange(status, 400, 499));
    }

    [Fact]
    public async Task ReadAsync_AHeldAccount_ListsItsFoldersByName()
    {
        // Arrange
        var deployment = new EndpointDeployment();
        deployment.Holding(new LocalMailFolderHolding(
            MailAccountCustodyPhase.Held,
            [
                Folder("projects"),
                Folder("Trash", MailFolderSpecialUse.Trash),
                Folder("Sent", MailFolderSpecialUse.Sent),
                Folder("Junk", MailFolderSpecialUse.Junk),
                Folder("INBOX", MailFolderSpecialUse.Inbox),
                Folder("Drafts", MailFolderSpecialUse.Drafts),
            ],
            []));

        // Act
        var result = await ClientLocalMailFoldersEndpoint.ReadAsync("primary", deployment.Editor, TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<ClientLocalMailFoldersResponse>>(result.Result).Value!;

        Assert.Equal(nameof(MailAccountCustodyPhase.Held), answered.Phase);
        Assert.Equal(["Drafts", "INBOX", "Junk", "projects", "Sent", "Trash"], answered.Folders.Select(folder => folder.Name));
        Assert.Equal(["Drafts", "Inbox", "Junk", null, "Sent", "Trash"], answered.Folders.Select(folder => folder.Role));
    }

    [Fact]
    public async Task ReadAsync_AnAccountTheCallerDoesNotHold_AnswersNotFound()
    {
        // Arrange
        var deployment = new EndpointDeployment();

        // Act
        var result = await ClientLocalMailFoldersEndpoint.ReadAsync("primary", deployment.Editor, TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status404NotFound, refusal.StatusCode);
    }

    [Fact]
    public async Task ReadAsync_NoAccountNamed_IsRefusedBeforeAnythingIsRead()
    {
        // Arrange
        var deployment = new EndpointDeployment();

        // Act
        var result = await ClientLocalMailFoldersEndpoint.ReadAsync("  ", deployment.Editor, TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        await deployment.Store.DidNotReceive().ReadAsync(Arg.Any<MailAccountIdentity>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The empty identity is one no deployment issues, so naming it is a malformed request rather than a missing folder.</summary>
    [Fact]
    public async Task DeleteAsync_TheEmptyFolderIdentity_IsRefusedAsMalformed()
    {
        // Arrange
        var deployment = new EndpointDeployment();

        // Act
        var result = await ClientLocalMailFoldersEndpoint.DeleteAsync(
            new ClientLocalMailFolderDeleteRequest("primary", Guid.Empty),
            deployment.Editor,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
    }

    /// <summary>An accepted write answers the change it made, the folder it made it to, and whether the mail erasure it queued was deferred.</summary>
    [Fact]
    public async Task DeleteAsync_AFolderInTheTrashWhileTheQueueIsFull_AnswersTheErasureAndThatItsMailWaits()
    {
        // Arrange
        var deployment = new EndpointDeployment();
        var trash = Folder("Trash", MailFolderSpecialUse.Trash);
        var old = Folder("old") with { ParentId = trash.Id };
        deployment.Holding(new LocalMailFolderHolding(
            MailAccountCustodyPhase.Held,
            [
                Folder("INBOX", MailFolderSpecialUse.Inbox),
                Folder("Drafts", MailFolderSpecialUse.Drafts),
                Folder("Sent", MailFolderSpecialUse.Sent),
                Folder("Junk", MailFolderSpecialUse.Junk),
                trash,
                old,
            ],
            []));
        deployment.Jobs.EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>())
            .Returns(JobEnqueueResult.RefusedAtCapacity());

        // Act
        var result = await ClientLocalMailFoldersEndpoint.DeleteAsync(
            new ClientLocalMailFolderDeleteRequest("primary", old.Id.Value),
            deployment.Editor,
            TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<ClientLocalMailFolderEditResponse>>(result.Result).Value!;

        Assert.Equal(nameof(LocalMailFolderChangeKind.Erased), answered.Change);
        Assert.Equal(old.Id.Value, answered.Folder.Id);
        Assert.True(answered.MailErasureDeferred);
    }

    [Theory]
    [InlineData(typeof(ClientLocalMailFolderCreateRequest), """{"account":"primary","name":"Projects","path":"Archive"}""")]
    [InlineData(typeof(ClientLocalMailFolderRenameRequest), """{"account":"primary","folderId":"0199a0c0-0000-7000-8000-000000000001","name":"Projects","path":"Archive"}""")]
    [InlineData(typeof(ClientLocalMailFolderMoveRequest), """{"account":"primary","folderId":"0199a0c0-0000-7000-8000-000000000001","path":"Archive/2026"}""")]
    [InlineData(typeof(ClientLocalMailFolderDeleteRequest), """{"account":"primary","folderId":"0199a0c0-0000-7000-8000-000000000001","path":"Archive"}""")]
    public void Deserialize_AWriteNamingAKeyNothingBinds_IsRefused(Type requestType, string body)
    {
        // Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(body, requestType, WebFormat));
    }

    [Fact]
    public void Deserialize_AMoveToTheTopOfTheHierarchy_BindsNoParent()
    {
        // Act
        var request = JsonSerializer.Deserialize<ClientLocalMailFolderMoveRequest>(
            """{"account":"primary","folderId":"0199a0c0-0000-7000-8000-000000000001","parentId":null}""",
            WebFormat);

        // Assert
        Assert.Equal("primary", request!.Account);
        Assert.Equal(Guid.Parse("0199a0c0-0000-7000-8000-000000000001"), request.FolderId);
        Assert.Null(request.ParentId);
    }

    private static JsonSerializerOptions WebFormat => new(JsonSerializerDefaults.Web);

    private static LocalMailFolder Folder(string name, MailFolderSpecialUse? role = null) =>
        new(LocalMailFolderId.Create(Guid.CreateVersion7()), ParentId: null, LocalMailFolderName.Create(name), role, SourceFolderAlias: null);

    /// <summary>The use case over a substituted store, reached by a caller who may read and edit folders.</summary>
    private sealed class EndpointDeployment
    {
        internal EndpointDeployment()
        {
            var clock = new FakeTimeProvider();
            var session = Substitute.For<IPersistenceSession>();
            session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
            var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
            sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(session);

            this.Editor = new LocalMailFolderEditor(
                this.Store,
                new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), clock),
                Substitute.For<ILocalMailFolderChangeAuditor>(),
                ClientSignalPublishers.ReachingNobody,
                this.Jobs,
                AccessAuthorizations.ForUserGranted(
                    SyntheticMailUser.Deployment,
                    [MailFathomPermission.MailRead, MailFathomPermission.MailFoldersWrite]),
                clock);
        }

        internal ILocalMailFolderStore Store { get; } = Substitute.For<ILocalMailFolderStore>();

        internal IJobStore Jobs { get; } = Substitute.For<IJobStore>();

        internal LocalMailFolderEditor Editor { get; }

        internal void Holding(LocalMailFolderHolding holding)
        {
            this.Store
                .ReadAsync(Arg.Any<MailAccountIdentity>(), Arg.Any<CancellationToken>())
                .Returns(holding);
            this.Store
                .ReadAsync(Arg.Any<IPersistenceSession>(), Arg.Any<MailAccountIdentity>(), Arg.Any<CancellationToken>())
                .Returns(holding);
        }
    }
}
