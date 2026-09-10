// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Hosting.Startup;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Startup;

/// <summary>
/// Covers how a start settles who this deployment serves: the rows the database holds, which source reaches each of
/// them, and the refusals a roster it could not serve is stopped by.
/// </summary>
public sealed class ServedMailUsersStartupGateTests
{
    private static readonly Guid RecordedIdentifier = new("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task StartAsync_OneRowHeldReadingNoRecord_ServesThatUserFromTheDeploymentSection()
    {
        // Arrange
        var roster = new ServedMailUsers();

        // Act
        await CreateGate([Held(SyntheticMailUser.Deployment, "user")], servedUsers: roster)
            .StartAsync(CancellationToken.None);

        // Assert
        var served = Assert.Single(roster.Users);
        Assert.Equal(SyntheticMailUser.Deployment, served.User);
        Assert.Equal(MailUserAccountSource.DeploymentSection, served.Source);
        Assert.Equal(SyntheticMailUser.Deployment, roster.User);
    }

    /// <summary>
    /// The release's own migration provisions that row, so reaching this means the row is not there at all. Generating
    /// one is what keeps the deployment's configured mailboxes belonging to somebody rather than failing the start.
    /// </summary>
    [Fact]
    public async Task StartAsync_NoRowHeld_RecordsOneUnderAGeneratedVersionFourIdentifier()
    {
        // Arrange
        var provisioning = ProvisioningThatRecords();
        var roster = new ServedMailUsers();

        // Act
        await CreateGate([], provisioning: provisioning, servedUsers: roster).StartAsync(CancellationToken.None);

        // Assert
        var served = Assert.Single(roster.Users);
        Assert.Equal(4, served.User.Value.Version);
        await provisioning.Received(1).ProvisionAsync(served.User, "user", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Another replica of this deployment recorded the sole user between this one reading an empty directory and its
    /// own insert reaching the table. Serving the identifier this process minted would hang every mail account, stored
    /// message, and job on a row that is not there.
    /// </summary>
    [Fact]
    public async Task StartAsync_TheSoleUserAnotherReplicaRecordedFirst_ServesTheRowTheDeploymentHolds()
    {
        // Arrange
        var roster = new ServedMailUsers();
        var winner = Held(SyntheticMailUser.Another, "user");

        var directory = Substitute.For<IMailUserDirectory>();
        directory.ReadUsersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyList<MailUserRecord>>([]),
            Task.FromResult<IReadOnlyList<MailUserRecord>>([winner]));

        var provisioning = Substitute.For<IMailUserProvisioning>();
        provisioning
            .ProvisionAsync(Arg.Any<MailUserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        // Act
        await CreateGate(directory, provisioning: provisioning, servedUsers: roster)
            .StartAsync(CancellationToken.None);

        // Assert
        var served = Assert.Single(roster.Users);
        Assert.Equal(SyntheticMailUser.Another, served.User);
        Assert.Equal("user", served.DisplayName);
        Assert.Equal(MailUserAccountSource.DeploymentSection, served.Source);
    }

    /// <summary>
    /// Several rows still reading the deployment's own section is a deployment whose mailboxes are in the section that
    /// names no user, so nothing could say which of them a configured account is for.
    /// </summary>
    [Fact]
    public async Task StartAsync_SeveralRowsHeldAndNoneRecordingTheirOwnMailboxes_FailsStartupNamingWhatToRun()
    {
        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailUserUnresolvedException>(() =>
            CreateGate([Held(SyntheticMailUser.Deployment, "user"), Held(SyntheticMailUser.Another, "second")])
                .StartAsync(CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.DeploymentMailUserUnresolved, refusal.ErrorCode);
        Assert.Contains("Record each mailbox against the user who owns it", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A user-facing surface answers one person about their own mail, and a surface requiring no credential admits a
    /// caller who names nobody — which leaves the user to be supplied by the deployment, and it has one to supply only
    /// while it serves one person.
    /// </summary>
    [Fact]
    public async Task StartAsync_SeveralUsersServedWithAUserFacingSurfaceAuthenticatingNobody_FailsStartupSayingWhy()
    {
        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailUserUnresolvedException>(() =>
            CreateGate(
                    TwoRecordedUsers(),
                    documents: RecordsOfTwoUsers(),
                    mcpEndpointSettings: new McpEndpointOptions { Enabled = true })
                .StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains("requires no authentication", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The client surface is user-facing on exactly the terms the MCP one is, so a deployment serving several users
    /// is refused for having it open unauthenticated even where no MCP endpoint is served at all.
    /// </summary>
    [Fact]
    public async Task StartAsync_SeveralUsersServedWithTheClientEndpointAuthenticatingNobody_FailsStartupSayingWhy()
    {
        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailUserUnresolvedException>(() =>
            CreateGate(
                    TwoRecordedUsers(),
                    documents: RecordsOfTwoUsers(),
                    clientEndpointSettings: new ClientEndpointOptions { Enabled = true })
                .StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains("requires no authentication", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An administrator acts for the deployment rather than for a person, so every user-scoped act of theirs names the
    /// user it is for and none of them has to be resolved from the roster. The administrative surface is therefore not
    /// among the ones this refusal reads, whatever it is configured with — which is what makes recording a second user
    /// reachable at all — so a deployment serving nobody a user-facing surface serves several people.
    /// </summary>
    [Fact]
    public async Task StartAsync_SeveralUsersServedWithNoUserFacingSurfaceEnabled_ServesEveryRecordedUser()
    {
        // Arrange
        var servedUsers = new ServedMailUsers();

        // Act
        await CreateGate(TwoRecordedUsers(), documents: RecordsOfTwoUsers(), servedUsers: servedUsers)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, servedUsers.Users.Count);
    }

    /// <summary>
    /// Every credential a user-facing surface admits is a record naming its user, whichever method presents it, so a
    /// surface requiring one can say which user every caller acts for — which is the posture under which a deployment
    /// may serve several people, and it holds for each of the four methods rather than for one of them.
    /// </summary>
    /// <param name="method">The method the enabled surface accepts.</param>
    [Theory]
    [InlineData("password")]
    [InlineData("api-key")]
    [InlineData("public-key")]
    [InlineData("oauth-subject")]
    public async Task StartAsync_SeveralUsersServedWhereTheUserFacingSurfaceRequiresACredential_ServesEveryRecordedUser(
        string method)
    {
        // Arrange
        var mcp = new McpEndpointOptions { Enabled = true };
        var servedUsers = new ServedMailUsers();

        mcp.Authentication.Add(new() { Method = method });

        // Act
        await CreateGate(
                TwoRecordedUsers(),
                documents: RecordsOfTwoUsers(),
                servedUsers: servedUsers,
                mcpEndpointSettings: mcp)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, servedUsers.Users.Count);
    }

    /// <summary>
    /// A worker switched on with no work is the deployment's own defect, and the roster is the first place both sources
    /// of a mailbox are together: the deployment's own section and a user's own record.
    /// </summary>
    [Fact]
    public async Task StartAsync_SynchronizationOnAndNoServedUserHoldingAMailbox_FailsStartupNamingWhereToDeclareOne()
    {
        // Arrange
        var declared = Configuration(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{MailSynchronizationOptions.SectionName}:{nameof(MailSynchronizationOptions.Enabled)}"] = "true",
        });

        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailUserUnresolvedException>(() =>
            CreateGate([], declared, servedUsers: new ServedMailUsers()).StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains("no user this deployment serves has a mail account", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("mfctl user account add", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The case reading the file alone would refuse: the deployment's own section is empty and the mailbox this
    /// deployment exists to synchronize is in the one place a file never carries, which is the user's own record.
    /// </summary>
    [Fact]
    public async Task StartAsync_SynchronizationOnAndTheOnlyMailboxDeclaredInAUsersRecord_ServesThem()
    {
        // Arrange
        var user = MailUserId.Create(RecordedIdentifier);
        var roster = new ServedMailUsers();
        var documents = Substitute.For<IUserSettingsDocumentReader>();

        DocumentOf(documents, user, "recorded", "alex@example.test");

        var declared = Configuration(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{MailSynchronizationOptions.SectionName}:{nameof(MailSynchronizationOptions.Enabled)}"] = "true",
        });

        // Act
        await CreateGate([Adopted(user, "alex")], declared, servedUsers: roster, documents: documents)
            .StartAsync(CancellationToken.None);

        // Assert
        var served = Assert.Single(roster.Users);

        Assert.Equal(["recorded"], served.MailAccounts.Select(account => account.AccountId));
    }

    /// <summary>A deployment that asked for nothing to be refreshed is served whatever its users hold, including nothing.</summary>
    [Fact]
    public async Task StartAsync_SynchronizationOffAndNoMailboxAnywhere_ServesTheUserAnyway()
    {
        // Arrange
        var roster = new ServedMailUsers();

        // Act
        await CreateGate([], servedUsers: roster).StartAsync(CancellationToken.None);

        // Assert
        Assert.Single(roster.Users);
    }

    /// <summary>
    /// The marker is what a committed record sets, and from then on that user's mailboxes are the document's rather
    /// than any file's — permanently, and for that user alone.
    /// </summary>
    [Fact]
    public async Task StartAsync_AUserWhoseDocumentWasWrittenAtRuntime_ServesThemFromIt()
    {
        // Arrange
        var user = MailUserId.Create(RecordedIdentifier);
        var roster = new ServedMailUsers();
        var documents = DocumentsHolding(
            user,
            """
            {"MailAccounts":[{"AccountId":"adopted","DisplayName":"Adopted at work","Host":"imap.example.test",
            "UserName":"alex@example.test",
            "Secrets":{"Password":{"Name":"imap-adopted-password","SecretReference":"systemd-credential:imap-adopted-password"}}}]}
            """);

        // Act
        await CreateGate([Adopted(user, "alex")], servedUsers: roster, documents: documents)
            .StartAsync(CancellationToken.None);

        // Assert
        var served = Assert.Single(roster.Users);
        Assert.Equal(MailUserAccountSource.UserDocument, served.Source);
        Assert.False(served.ReadFromConfiguration);
        Assert.Equal(["adopted"], served.MailAccounts.Select(account => account.AccountId));
    }

    /// <summary>
    /// The alternative to failing is a deployment quietly synchronizing the mailboxes an adoption was meant to replace,
    /// because the user's declared section has stopped being read and their document says nothing usable.
    /// </summary>
    [Fact]
    public async Task StartAsync_AnAdoptedUserWhoseDocumentWillNotBind_FailsStartupNamingTheUser()
    {
        // Arrange
        var user = MailUserId.Create(RecordedIdentifier);
        var documents = DocumentsHolding(user, """{"MailAccounts":[{"AccountId":"adopted","Nonsense":"no property binds this"}]}""");

        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailUserUnresolvedException>(() =>
            CreateGate([Adopted(user, "alex")], documents: documents).StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains("'alex'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("served from it rather than from configuration", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A user an administrator recorded is declared in no file, so nothing that reads the declarations reaches them.
    /// Leaving them unserved would be a deployment holding a row whose mail it never synchronizes and whose caller it
    /// could not compose, which is what every provisioned user would be until somebody edited a file.
    /// </summary>
    [Fact]
    public async Task StartAsync_AUserRecordedAtRuntimeThatNoFileDeclares_ServesThemFromTheirOwnRecord()
    {
        // Arrange
        var provisioned = SyntheticMailUser.Another;
        var roster = new ServedMailUsers();
        var documents = DocumentsHolding(
            provisioned,
            """
            {"MailAccounts":[{"AccountId":"recorded","DisplayName":"Recorded at work","Host":"imap.example.test",
            "UserName":"sam@example.test",
            "Secrets":{"Password":{"Name":"imap-recorded-password","SecretReference":"systemd-credential:imap-recorded-password"}}}]}
            """);

        // Act
        await CreateGate(
                [Held(SyntheticMailUser.Deployment, "user"), Adopted(provisioned, "sam")],
                servedUsers: roster,
                documents: documents)
            .StartAsync(CancellationToken.None);

        // Assert
        var served = Assert.Single(roster.Users, user => user.User == provisioned);
        Assert.Equal(MailUserAccountSource.UserDocument, served.Source);
        Assert.Equal(["recorded"], served.MailAccounts.Select(account => account.AccountId));
    }

    /// <summary>
    /// The deployment's own mail section is the one part of the roster a file still decides, and a user outside it has
    /// no place in that order to take — so a recorded user is served after the user that section belongs to.
    /// </summary>
    [Fact]
    public async Task StartAsync_AUserRecordedAtRuntimeBesideTheDeploymentSectionsOwn_ServesThemAfterIt()
    {
        // Arrange
        var sectionUser = MailUserId.Create(RecordedIdentifier);
        var roster = new ServedMailUsers();

        // Act
        await CreateGate(
                [Adopted(SyntheticMailUser.Another, "sam"), Held(sectionUser, "alex")],
                servedUsers: roster,
                documents: DocumentsHolding(SyntheticMailUser.Another, "{}"))
            .StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal([sectionUser, SyntheticMailUser.Another], roster.Users.Select(user => user.User));
    }

    /// <summary>
    /// The deployment-wide naming rule a file is already held to, asked of the roster a start would serve — which is
    /// where a collision two users wrote into their own records while a process ran first becomes visible. A write is
    /// judged against the roster this process settled, so two such writes in one run are each judged against a roster
    /// the other had not moved; this start is the first moment both records are in one place, and an account name
    /// reaching two users resolves to whichever of them the lookup met first.
    /// </summary>
    [Fact]
    public async Task StartAsync_TwoUsersWhoseOwnRecordsNameOneMailAccount_FailsStartupNamingTheAccount()
    {
        // Arrange
        var documents = Substitute.For<IUserSettingsDocumentReader>();

        DocumentOf(documents, SyntheticMailUser.Deployment, "work", "alex@example.test");
        DocumentOf(documents, SyntheticMailUser.Another, "work", "sam@example.test");

        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailUserUnresolvedException>(() =>
            CreateGate(
                    [Adopted(SyntheticMailUser.Deployment, "alex"), Adopted(SyntheticMailUser.Another, "sam")],
                    servedUsers: new ServedMailUsers(),
                    documents: documents)
                .StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains("work", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A user served from the deployment's own section carries no accounts on their roster entry, so a check reading
    /// that entry alone would never see <c>MailSynchronization:Accounts</c> — and the one collision the write-time
    /// reading cannot catch, a record naming an account the section already declares, would start clean and resolve one
    /// person's mailbox for another's.
    /// </summary>
    [Fact]
    public async Task StartAsync_ARecordNamingAnAccountTheDeploymentSectionDeclares_FailsStartupNamingTheAccount()
    {
        // Arrange
        var documents = Substitute.For<IUserSettingsDocumentReader>();

        DocumentOf(documents, SyntheticMailUser.Another, "work", "sam@example.test");

        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailUserUnresolvedException>(() =>
            CreateGate(
                    [Held(SyntheticMailUser.Deployment, "user"), Adopted(SyntheticMailUser.Another, "sam")],
                    declared: DeploymentSectionDeclaring("work"),
                    servedUsers: new ServedMailUsers(),
                    documents: documents)
                .StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains("work", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>The same two mailboxes named apart is the ordinary case, and it starts.</summary>
    [Fact]
    public async Task StartAsync_ARecordNamingAnAccountTheDeploymentSectionDoesNot_ServesBothUsers()
    {
        // Arrange
        var roster = new ServedMailUsers();
        var documents = Substitute.For<IUserSettingsDocumentReader>();

        DocumentOf(documents, SyntheticMailUser.Another, "sam-work", "sam@example.test");

        // Act
        await CreateGate(
                [Held(SyntheticMailUser.Deployment, "user"), Adopted(SyntheticMailUser.Another, "sam")],
                declared: DeploymentSectionDeclaring("alex-work"),
                servedUsers: roster,
                documents: documents)
            .StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, roster.Users.Count);
    }

    /// <summary>
    /// What an adoption leaves on a deployment that declares no users: the accounts are copied into the record and
    /// the section they were copied from is still bound. Nothing serves it any more, and the per-account lookup still
    /// searches it before any record — so a start that accepted this would synchronize each mailbox under the file the
    /// operator was told had stopped being read.
    /// </summary>
    [Fact]
    public async Task StartAsync_ADeploymentSectionNoServedUserReads_FailsStartupNamingTheSectionToClear()
    {
        // Arrange
        var documents = Substitute.For<IUserSettingsDocumentReader>();

        DocumentOf(documents, SyntheticMailUser.Deployment, "work", "alex@example.test");

        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailUserUnresolvedException>(() =>
            CreateGate(
                    [Adopted(SyntheticMailUser.Deployment, "alex")],
                    declared: DeploymentSectionDeclaring("work"),
                    servedUsers: new ServedMailUsers(),
                    documents: documents)
                .StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains("MailSynchronization:Accounts", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("no user this deployment serves reads that section", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal above is about which declaration a mailbox's settings are read from, and every per-account read
    /// resolves that whether or not a synchronization run is what asked. Switching synchronization off therefore
    /// changes nothing about it, which is what keeps the abandoned section from being a rule only a worker enforces.
    /// </summary>
    [Fact]
    public async Task StartAsync_ADeploymentSectionNoServedUserReadsWithSynchronizationOff_IsRefusedJustTheSame()
    {
        // Arrange
        var documents = Substitute.For<IUserSettingsDocumentReader>();
        var keys = DeploymentSectionKeys("work");

        DocumentOf(documents, SyntheticMailUser.Deployment, "work", "alex@example.test");
        keys[$"{MailSynchronizationOptions.SectionName}:{nameof(MailSynchronizationOptions.Enabled)}"] = "false";

        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailUserUnresolvedException>(() =>
            CreateGate(
                    [Adopted(SyntheticMailUser.Deployment, "alex")],
                    declared: Configuration(keys),
                    servedUsers: new ServedMailUsers(),
                    documents: documents)
                .StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains("MailSynchronization:Accounts", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>The same two users naming their mailboxes apart is the ordinary case, and it starts.</summary>
    [Fact]
    public async Task StartAsync_TwoUsersWhoseOwnRecordsNameTheirMailAccountsApart_ServesBothOfThem()
    {
        // Arrange
        var roster = new ServedMailUsers();
        var documents = Substitute.For<IUserSettingsDocumentReader>();

        DocumentOf(documents, SyntheticMailUser.Deployment, "alex-work", "alex@example.test");
        DocumentOf(documents, SyntheticMailUser.Another, "sam-work", "sam@example.test");

        // Act
        await CreateGate(
                [Adopted(SyntheticMailUser.Deployment, "alex"), Adopted(SyntheticMailUser.Another, "sam")],
                servedUsers: roster,
                documents: documents)
            .StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, roster.Users.Count);
    }

    /// <summary>
    /// Only a user still reading the deployment's own section contends for it, so a deployment whose every user has
    /// adopted has no sole user to serve — and minting one would record a person nobody asked for on every start.
    /// </summary>
    [Fact]
    public async Task StartAsync_EveryHeldUserReadingTheirOwnRecord_RecordsNobodyNew()
    {
        // Arrange
        var provisioning = ProvisioningThatRecords();
        var roster = new ServedMailUsers();

        // Act
        await CreateGate(
                [Adopted(SyntheticMailUser.Deployment, "user")],
                provisioning: provisioning,
                servedUsers: roster,
                documents: DocumentsHolding(SyntheticMailUser.Deployment, "{}"))
            .StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal(SyntheticMailUser.Deployment, Assert.Single(roster.Users).User);
        await provisioning.DidNotReceiveWithAnyArgs().ProvisionAsync(default, default!, CancellationToken.None);
    }

    /// <summary>
    /// Two users reading one section is a deployment with no answer to whose mailboxes those are, and picking either
    /// would hang one person's mail on the other's row.
    /// </summary>
    [Fact]
    public async Task StartAsync_TwoHeldUsersStillReadingTheSection_FailsStartup()
    {
        // Act & Assert
        await Assert.ThrowsAsync<DeploymentMailUserUnresolvedException>(() =>
            CreateGate([Held(SyntheticMailUser.Deployment, "user"), Held(SyntheticMailUser.Another, "sam")])
                .StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_AServedRoster_ReportsTheUserGateToTheStartupProbe()
    {
        // Arrange
        var startupGates = new HostStartupGates(HostStartupGate.ServedMailUsers);

        // Act
        await CreateGate([Held(SyntheticMailUser.Deployment, "user")], startupGates: startupGates)
            .StartAsync(CancellationToken.None);

        // Assert
        Assert.True(startupGates.Completed);
    }

    /// <summary>A gate that failed took the host down with it, so nothing may report the host as having come up.</summary>
    [Fact]
    public async Task StartAsync_ARosterItCannotServe_LeavesTheUserGateOutstanding()
    {
        // Arrange
        var startupGates = new HostStartupGates(HostStartupGate.ServedMailUsers);

        // Act
        await Assert.ThrowsAsync<DeploymentMailUserUnresolvedException>(() =>
            CreateGate(
                    [Held(SyntheticMailUser.Deployment, "user"), Held(SyntheticMailUser.Another, "second")],
                    startupGates: startupGates)
                .StartAsync(CancellationToken.None));

        // Assert
        Assert.False(startupGates.Completed);
    }

    /// <summary>Reading one row more than a deployment may hold is what makes a roster past the bound observable rather than silently truncated.</summary>
    [Fact]
    public async Task StartAsync_AlwaysGiven_ReadsOneUserMoreThanADeploymentMayHold()
    {
        // Arrange
        var directory = DirectoryOf([Held(SyntheticMailUser.Deployment, "user")]);

        // Act
        await CreateGate(directory).StartAsync(CancellationToken.None);

        // Assert
        await directory.Received(1)
            .ReadUsersAsync(ServedMailUsers.MaximumUsers + 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_TheCallersToken_PropagatesItToTheDirectory()
    {
        // Arrange
        var directory = DirectoryOf([Held(SyntheticMailUser.Deployment, "user")]);
        using var cancellation = new CancellationTokenSource();

        // Act
        await CreateGate(directory).StartAsync(cancellation.Token);

        // Assert
        await directory.Received(1).ReadUsersAsync(Arg.Any<int>(), cancellation.Token);
    }

    private static MailUserRecord Held(MailUserId user, string displayName) =>
        new(user, displayName, DocumentWrittenAtRuntime: false);

    /// <summary>A user whose document an adoption has written, which is what makes it the source their mailboxes come from.</summary>
    private static MailUserRecord Adopted(MailUserId user, string displayName) =>
        new(user, displayName, DocumentWrittenAtRuntime: true);

    private static IUserSettingsDocumentReader DocumentsHolding(MailUserId user, string json)
    {
        var documents = Substitute.For<IUserSettingsDocumentReader>();

        documents.ReadAsync(user, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<UserSettingsDocument?>(
                new UserSettingsDocument(user, "alex", json, Version: 2, WrittenAtRuntime: true)));

        return documents;
    }

    /// <summary>States one user's own record, holding a single mail account named as the test asks.</summary>
    /// <summary>The same for the other source a served user's settings come from, which is their own record.</summary>
    [Fact]
    public async Task StartAsync_AnAdoptedUserWhoseRecordAsksForAScanner_PublishesWhatTheyAskedFor()
    {
        // Arrange
        var user = MailUserId.Create(RecordedIdentifier);
        var roster = new ServedMailUsers();
        var documents = Substitute.For<IUserSettingsDocumentReader>();

        ScanningDocumentOf(documents, user);

        // Act
        await CreateGate([Adopted(user, "alex")], servedUsers: roster, documents: documents)
            .StartAsync(CancellationToken.None);

        // Assert
        var served = Assert.Single(roster.Users);

        Assert.True(served.SensitiveContent!.Secrets.Enabled);
        Assert.Equal(["Secrets"], served.SensitiveContent!.ScreenOutgoingMailFor!);
    }

    /// <summary>A user's record declaring one scanner and no mailbox at all, which is what the block alone looks like.</summary>
    private static void ScanningDocumentOf(IUserSettingsDocumentReader documents, MailUserId user) =>
        documents.ReadAsync(user, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<UserSettingsDocument?>(new UserSettingsDocument(
                user,
                $"user-{user.Value:D}",
                """{"MailAccounts":[],"SensitiveContent":{"Secrets":{"Enabled":true},"ScreenOutgoingMailFor":["Secrets"]}}""",
                Version: 2,
                WrittenAtRuntime: true)));

    private static void DocumentOf(
        IUserSettingsDocumentReader documents,
        MailUserId user,
        string accountId,
        string userName) =>
        documents.ReadAsync(user, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<UserSettingsDocument?>(new UserSettingsDocument(
                user,
                $"user-{user.Value:D}",
                $$"""
                  {
                    "MailAccounts": [
                      {
                        "AccountId": "{{accountId}}",
                        "DisplayName": "{{accountId}}",
                        "Host": "imap.example.test",
                        "UserName": "{{userName}}",
                        "Secrets": { "Password": { "Name": "imap-password", "SecretReference": "systemd-credential:imap-password" } }
                      }
                    ]
                  }
                  """,
                Version: 2,
                WrittenAtRuntime: true)));

    private static IConfiguration Configuration(Dictionary<string, string?> keys) =>
        new ConfigurationBuilder().AddInMemoryCollection(keys).Build();

    /// <summary>The deployment's own mail section, declaring one account under the identifier a test names.</summary>
    private static IConfiguration DeploymentSectionDeclaring(string accountId) =>
        Configuration(DeploymentSectionKeys(accountId));

    /// <summary>The same section as keys, for a test that states one more setting beside it.</summary>
    private static Dictionary<string, string?> DeploymentSectionKeys(string accountId) =>
        new(StringComparer.Ordinal)
        {
            [$"{MailSynchronizationOptions.SectionName}:Accounts:0:AccountId"] = accountId,
            [$"{MailSynchronizationOptions.SectionName}:Accounts:0:DisplayName"] = accountId,
            [$"{MailSynchronizationOptions.SectionName}:Accounts:0:Host"] = "imap.example.test",
            [$"{MailSynchronizationOptions.SectionName}:Accounts:0:UserName"] = "alex@example.test",
            [$"{MailSynchronizationOptions.SectionName}:Accounts:0:Secrets:Password:Name"] = "imap-password",
            [$"{MailSynchronizationOptions.SectionName}:Accounts:0:Secrets:Password:SecretReference"] = "systemd-credential:imap-password",
        };

    /// <summary>Two users whose records are their own, which is the only roster of more than one this deployment holds.</summary>
    private static MailUserRecord[] TwoRecordedUsers() =>
    [
        Adopted(MailUserId.Create(RecordedIdentifier), "alex"),
        Adopted(SyntheticMailUser.Another, "sam"),
    ];

    /// <summary>The records those two users are served from, each naming a mailbox of their own.</summary>
    private static IUserSettingsDocumentReader RecordsOfTwoUsers()
    {
        var documents = Substitute.For<IUserSettingsDocumentReader>();

        DocumentOf(documents, MailUserId.Create(RecordedIdentifier), "alex-work", "alex@example.test");
        DocumentOf(documents, SyntheticMailUser.Another, "sam-work", "sam@example.test");

        return documents;
    }

    private static IMailUserDirectory DirectoryOf(IReadOnlyList<MailUserRecord> held)
    {
        var directory = Substitute.For<IMailUserDirectory>();

        directory.ReadUsersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(held));

        return directory;
    }

    private static ServedMailUsersStartupGate CreateGate(
        IReadOnlyList<MailUserRecord> held,
        IConfiguration? declared = null,
        IMailUserProvisioning? provisioning = null,
        ServedMailUsers? servedUsers = null,
        HostStartupGates? startupGates = null,
        McpEndpointOptions? mcpEndpointSettings = null,
        IUserSettingsDocumentReader? documents = null,
        ClientEndpointOptions? clientEndpointSettings = null,
        ILogger<ServedMailUsersStartupGate>? startupLog = null) =>
        CreateGate(
            DirectoryOf(held),
            declared,
            provisioning,
            servedUsers,
            startupGates,
            mcpEndpointSettings,
            documents,
            clientEndpointSettings,
            startupLog);

    private static ServedMailUsersStartupGate CreateGate(
        IMailUserDirectory directory,
        IConfiguration? declared = null,
        IMailUserProvisioning? provisioning = null,
        ServedMailUsers? servedUsers = null,
        HostStartupGates? startupGates = null,
        McpEndpointOptions? mcpEndpointSettings = null,
        IUserSettingsDocumentReader? documents = null,
        ClientEndpointOptions? clientEndpointSettings = null,
        ILogger<ServedMailUsersStartupGate>? startupLog = null)
    {
        var services = new ServiceCollection();

        services.AddScoped(_ => directory);
        services.AddScoped(_ => provisioning ?? ProvisioningThatRecords());
        services.AddScoped(_ => documents ?? Substitute.For<IUserSettingsDocumentReader>());
        services.AddSingleton(new UserAccountDocumentBinder(
            new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
            new FakeTimeProvider(),
            Options.Create(new SensitiveContentOptions())));
        services.AddSingleton(SecretValidator());

        return new ServedMailUsersStartupGate(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            declared ?? new ConfigurationBuilder().Build(),
            servedUsers ?? new ServedMailUsers(),
            startupGates ?? new HostStartupGates(HostStartupGate.ServedMailUsers),
            new SeveralUserAdmission(
                Options.Create(mcpEndpointSettings ?? new McpEndpointOptions()),
                Options.Create(clientEndpointSettings ?? new ClientEndpointOptions())),
            startupLog ?? NullLogger<ServedMailUsersStartupGate>.Instance);
    }

    /// <summary>A provisioning that reports the row as recorded, which is what every case but the contested label is.</summary>
    private static IMailUserProvisioning ProvisioningThatRecords() => ProvisioningThatAccepts();

    /// <summary>
    /// A provisioning standing for a database that accepts what it is given. Both of its writes are conditional and
    /// report what the row carries afterwards, so the default a substitute returns — false — is the contested label
    /// rather than the ordinary case, and a test arranging neither would be arranging a race it did not mean to.
    /// </summary>
    private static IMailUserProvisioning ProvisioningThatAccepts()
    {
        var provisioning = Substitute.For<IMailUserProvisioning>();

        provisioning
            .ProvisionAsync(Arg.Any<MailUserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        provisioning
            .RelabelAsync(Arg.Any<MailUserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        return provisioning;
    }

    /// <summary>Resolves a reference under any scheme a deployment registers, which is what a user's secrets are proven through here.</summary>
    /// <remarks>Composed where the record administration's tests compose it too, because the same validator judges a user's mail accounts at a start and at a write.</remarks>
    private static SecretConfigurationValidator SecretValidator() => SecretValidation.OverRegisteredSchemes();
}
