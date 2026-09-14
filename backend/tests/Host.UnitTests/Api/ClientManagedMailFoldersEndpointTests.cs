// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Folders;
using MailFathom.Application.Folders.Local;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail;
using MailFathom.Application.Persistence;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Transport;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>
/// Covers what the boundary decides about the one folder surface: which status each refusal answers with and the name it
/// carries, a request naming no account or a role no folder may be created for refused before the use case is reached,
/// what a read and an accepted act answer, and the strict binding of every write body.
/// </summary>
public sealed class ClientManagedMailFoldersEndpointTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailUser.Deployment, MailAccountId.Create("primary"));

    private static readonly MailTransportSecurityPolicy RequiredTlsPolicy = MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.TlsOnConnect,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    [Theory]
    [InlineData(MailFolderActRefusal.AccountMissing, StatusCodes.Status404NotFound)]
    [InlineData(MailFolderActRefusal.FolderMissing, StatusCodes.Status404NotFound)]
    [InlineData(MailFolderActRefusal.ParentMissing, StatusCodes.Status404NotFound)]
    [InlineData(MailFolderActRefusal.NameInvalid, StatusCodes.Status400BadRequest)]
    [InlineData(MailFolderActRefusal.InboxNameAtTopLevel, StatusCodes.Status400BadRequest)]
    [InlineData(MailFolderActRefusal.AccountNotHeld, StatusCodes.Status409Conflict)]
    [InlineData(MailFolderActRefusal.ProtectedRole, StatusCodes.Status409Conflict)]
    [InlineData(MailFolderActRefusal.NameTaken, StatusCodes.Status409Conflict)]
    [InlineData(MailFolderActRefusal.NestedInItself, StatusCodes.Status409Conflict)]
    [InlineData(MailFolderActRefusal.TooDeep, StatusCodes.Status409Conflict)]
    [InlineData(MailFolderActRefusal.TooManyFolders, StatusCodes.Status409Conflict)]
    [InlineData(MailFolderActRefusal.RoleAlreadyPlayed, StatusCodes.Status409Conflict)]
    [InlineData(MailFolderActRefusal.NotDeclaredByTheAccount, StatusCodes.Status409Conflict)]
    [InlineData(MailFolderActRefusal.ServerRefused, StatusCodes.Status502BadGateway)]
    [InlineData(MailFolderActRefusal.ServerUnavailable, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(MailFolderActRefusal.NotRecorded, StatusCodes.Status500InternalServerError)]
    public void Refused_EachRefusal_AnswersItsStatusAndCarriesItsOwnName(MailFolderActRefusal refusal, int expectedStatus)
    {
        // Act
        var answer = ClientManagedMailFoldersEndpoint.Refused(refusal);

        // Assert
        Assert.Equal(expectedStatus, answer.StatusCode);
        Assert.NotNull(answer.ProblemDetails.Detail);
        Assert.Equal(refusal.ToString(), answer.ProblemDetails.Extensions["refusal"]);
    }

    /// <summary>A refusal added to the use case without an answer here would reach a client as an unexplained server failure.</summary>
    [Fact]
    public void Refused_EveryDeclaredRefusal_HasAnAnswer()
    {
        // Act
        var statuses = Enum.GetValues<MailFolderActRefusal>()
            .Select(refusal => ClientManagedMailFoldersEndpoint.Refused(refusal).StatusCode);

        // Assert
        Assert.All(statuses, status => Assert.InRange(status, 400, 599));
    }

    /// <summary>The report is the whole of what a client needs to draw its menus, and it names no storage mode.</summary>
    [Fact]
    public async Task ReadAsync_AHeldAccount_ReportsTheFoldersAndTheActsEachAllows()
    {
        // Arrange
        await using var deployment = new EndpointDeployment(MailAccountCustodyPhase.Held);

        // Act
        var result = await ClientManagedMailFoldersEndpoint.ReadAsync("primary", deployment.Editor, TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<ClientManagedMailFoldersResponse>>(result.Result).Value!;

        Assert.Equal(["Create"], answered.AllowedActs);
        Assert.Equal(["Drafts", "INBOX", "Junk", "Sent", "Trash"], answered.Folders.Select(folder => folder.Name));
        Assert.All(answered.Folders, folder => Assert.Empty(folder.AllowedActs));
    }

    [Fact]
    public async Task ReadAsync_AnAccountTheCallerDoesNotHold_AnswersNotFound()
    {
        // Arrange
        await using var deployment = new EndpointDeployment(MailAccountCustodyPhase.Held);

        // Act
        var result = await ClientManagedMailFoldersEndpoint.ReadAsync("other", deployment.Editor, TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status404NotFound, refusal.StatusCode);
    }

    [Fact]
    public async Task ReadAsync_NoAccountNamed_IsRefusedBeforeAnythingIsRead()
    {
        // Arrange
        await using var deployment = new EndpointDeployment(MailAccountCustodyPhase.Held);

        // Act
        var result = await ClientManagedMailFoldersEndpoint.ReadAsync("  ", deployment.Editor, TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_AFolderOnAHeldAccount_AnswersTheChangeAndTheFolderItMadeIt()
    {
        // Arrange
        await using var deployment = new EndpointDeployment(MailAccountCustodyPhase.Held);

        // Act
        var result = await ClientManagedMailFoldersEndpoint.CreateAsync(
            new ClientManagedMailFolderCreateRequest("primary", Name: "Projects"),
            deployment.Editor,
            TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<ClientManagedMailFolderActResponse>>(result.Result).Value!;

        Assert.Equal(nameof(MailFolderChangeKind.Created), answered.Change);
        Assert.Equal("Projects", answered.Folder.Name);
        Assert.False(answered.MailErasureDeferred);
    }

    /// <summary>A role outside the set the read publishes is a malformed request rather than an act to refuse afterwards.</summary>
    [Theory]
    [InlineData("Inbox")]
    [InlineData("Flagged")]
    [InlineData("nonsense")]
    public async Task CreateAsync_ARoleNoFolderMayBeCreatedFor_IsRefusedAsMalformed(string role)
    {
        // Arrange
        await using var deployment = new EndpointDeployment(MailAccountCustodyPhase.Mirrored);

        // Act
        var result = await ClientManagedMailFoldersEndpoint.CreateAsync(
            new ClientManagedMailFolderCreateRequest("primary", Role: role),
            deployment.Editor,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
    }

    /// <summary>A folder named by text no deployment issues is a folder the account does not have, not a malformed request.</summary>
    [Fact]
    public async Task DeleteAsync_AFolderIdentityNoDeploymentIssues_IsRefusedAsMissing()
    {
        // Arrange
        await using var deployment = new EndpointDeployment(MailAccountCustodyPhase.Held);

        // Act
        var result = await ClientManagedMailFoldersEndpoint.DeleteAsync(
            new ClientManagedMailFolderDeleteRequest("primary", "   "),
            deployment.Editor,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status404NotFound, refusal.StatusCode);
        Assert.Equal(nameof(MailFolderActRefusal.FolderMissing), refusal.ProblemDetails.Extensions["refusal"]);
    }

    [Theory]
    [InlineData(typeof(ClientManagedMailFolderCreateRequest), """{"account":"primary","name":"Projects","path":"Archive"}""")]
    [InlineData(typeof(ClientManagedMailFolderRenameRequest), """{"account":"primary","folderId":"PROJECTS","name":"Projects","path":"Archive"}""")]
    [InlineData(typeof(ClientManagedMailFolderMoveRequest), """{"account":"primary","folderId":"PROJECTS","path":"Archive/2026"}""")]
    [InlineData(typeof(ClientManagedMailFolderDeleteRequest), """{"account":"primary","folderId":"PROJECTS","path":"Archive"}""")]
    public void Deserialize_AWriteNamingAKeyNothingBinds_IsRefused(Type requestType, string body)
    {
        // Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(body, requestType, WebFormat));
    }

    [Fact]
    public void Deserialize_AMoveToTheTopOfTheHierarchy_BindsNoParent()
    {
        // Act
        var request = JsonSerializer.Deserialize<ClientManagedMailFolderMoveRequest>(
            """{"account":"primary","folderId":"PROJECTS","parentId":null}""",
            WebFormat);

        // Assert
        Assert.Equal("primary", request!.Account);
        Assert.Equal("PROJECTS", request.FolderId);
        Assert.Null(request.ParentId);
    }

    private static JsonSerializerOptions WebFormat => new(JsonSerializerDefaults.Web);

    /// <summary>The one use case over both editors, reached by a caller who may read and act on folders.</summary>
    private sealed class EndpointDeployment : IAsyncDisposable
    {
        private readonly ClientSignals signals;

        internal EndpointDeployment(MailAccountCustodyPhase phase)
        {
            var clock = new FakeTimeProvider();
            this.signals = new ClientSignals([new RecordingClientSignalChannel()], clock);

            var session = Substitute.For<IPersistenceSession>();
            session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
            var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
            sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(session);

            var store = Substitute.For<ILocalMailFolderStore>();
            var holding = new LocalMailFolderHolding(phase, phase is MailAccountCustodyPhase.Mirrored ? [] : ProtectedFolders(), []);
            store.ReadAsync(Account, Arg.Any<CancellationToken>()).Returns(holding);
            store.ReadAsync(Arg.Any<IPersistenceSession>(), Account, Arg.Any<CancellationToken>()).Returns(holding);

            var transportPolicies = Substitute.For<IMailTransportSecurityPolicyReader>();
            transportPolicies.GetPolicy(Arg.Any<MailAccountId>()).Returns(RequiredTlsPolicy);

            var declarations = Substitute.For<IMailFolderDeclarationWriter>();
            declarations
                .AliasesTheAccountDeclaresAsync(Arg.Any<MailAccountId>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<IReadOnlySet<MailFolderAlias>>(new HashSet<MailFolderAlias>()));

            var authorization = AccessAuthorizations.ForUserGranted(
                SyntheticMailUser.Deployment,
                [MailFathomPermission.MailRead, MailFathomPermission.MailFoldersWrite]);

            this.Editor = new MailFolderEditor(
                new LocalMailFolderEditor(
                    store,
                    new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), clock),
                    Substitute.For<ILocalMailFolderChangeAuditor>(),
                    this.signals,
                    Substitute.For<IJobStore>(),
                    authorization,
                    clock),
                new MirroredMailFolderEditor(
                    Substitute.For<IMailFolderMappingReader>(),
                    declarations,
                    Substitute.For<IRemoteFolderCreator>(),
                    Substitute.For<IRemoteFolderEditor>(),
                    transportPolicies,
                    Substitute.For<IAuthoredFolderDeleteDispositionReader>(),
                    this.signals,
                    Substitute.For<IJobStore>(),
                    authorization),
                authorization);
        }

        internal MailFolderEditor Editor { get; }

        public ValueTask DisposeAsync() => this.signals.DisposeAsync();

        private static IReadOnlyList<LocalMailFolder> ProtectedFolders() =>
        [
            Folder("INBOX", MailFolderSpecialUse.Inbox),
            Folder("Drafts", MailFolderSpecialUse.Drafts),
            Folder("Sent", MailFolderSpecialUse.Sent),
            Folder("Junk", MailFolderSpecialUse.Junk),
            Folder("Trash", MailFolderSpecialUse.Trash),
        ];

        private static LocalMailFolder Folder(string name, MailFolderSpecialUse role) => new(
            LocalMailFolderId.Create(Guid.CreateVersion7()),
            ParentId: null,
            LocalMailFolderName.Create(name),
            role,
            SourceFolderAlias: null);
    }
}
