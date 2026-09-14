// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Records;
using MailFathom.Host.Configuration.Rules;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Hosting.Startup;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Rules;
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
/// Covers how a start settles who this deployment serves: the rows the database holds, the record each of them is
/// composed from together with the mail accounts assigned to them, and the refusals a roster it could not serve is
/// stopped by. No configuration source declares a user or a mailbox any more, so every case here is a row and the
/// records beside it.
/// </summary>
public sealed class ServedMailUsersStartupGateTests
{
    /// <summary>A record of a user's language and nothing else, which is what a provisioning leaves behind.</summary>
    private const string LanguageOnlyRecord = """{"Language":"English"}""";

    private static readonly Guid RecordedIdentifier = new("33333333-3333-3333-3333-333333333333");

    private static readonly MailAccountRecord AlexWork = Mailbox(
        new Guid("0197a3c0-0000-7000-8000-000000000001"),
        "alex@example.test",
        "work");

    private static readonly MailAccountRecord SamWork = Mailbox(
        new Guid("0197a3c0-0000-7000-8000-000000000002"),
        "sam@example.test",
        "work");

    /// <summary>
    /// The state a fresh database is in, and so is a deployment whose every user was erased, and one a start
    /// admits: it starts, completes its gate, serves nobody, and says so. Refusing here would leave that deployment
    /// with no start to record a user from.
    /// </summary>
    [Fact]
    public async Task StartAsync_NoUserHeld_ServesNobodyAndCompletesTheGate()
    {
        // Arrange
        var roster = new ServedMailUsers();
        var startupGates = new HostStartupGates(HostStartupGate.ServedMailUsers);
        var startupLog = new RecordingLogger<ServedMailUsersStartupGate>();

        // Act
        await CreateGate([], servedUsers: roster, startupGates: startupGates, startupLog: startupLog)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(roster.Users);
        Assert.True(startupGates.Completed);
        Assert.Contains(
            startupLog.Messages,
            message => message.Contains("holds no user", StringComparison.Ordinal)
                && message.Contains("mfctl user add", StringComparison.Ordinal));
    }

    /// <summary>
    /// A switch left on with nothing to synchronize is what every deployment looks like between its first start and
    /// its first recorded mailbox, so it is reported and the deployment runs.
    /// </summary>
    [Fact]
    public async Task StartAsync_SynchronizationOnAndNobodyAssignedAMailbox_ReportsItRatherThanRefusing()
    {
        // Arrange
        var roster = new ServedMailUsers();
        var startupLog = new RecordingLogger<ServedMailUsersStartupGate>();

        // Act
        await CreateGate(
                [Held(SyntheticMailUser.Deployment, "alex")],
                SynchronizationSwitchedOn(),
                servedUsers: roster,
                documents: RecordsHolding(Record(SyntheticMailUser.Deployment, LanguageOnlyRecord)),
                startupLog: startupLog)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(roster.Users);
        Assert.Contains(
            startupLog.Messages,
            message => message.Contains("nothing to synchronize", StringComparison.Ordinal));
    }

    /// <summary>The ordinary deployment: one row, and the mailbox assigned to that user, served under the identifier the deployment generated for it.</summary>
    [Fact]
    public async Task StartAsync_AUserAssignedAMailbox_ServesThemWithIt()
    {
        // Arrange
        var user = MailUserId.Create(RecordedIdentifier);
        var roster = new ServedMailUsers();

        // Act
        await CreateGate(
                [Held(user, "alex")],
                servedUsers: roster,
                documents: RecordsHolding(Record(user, LanguageOnlyRecord, AlexWork)))
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var served = Assert.Single(roster.Users);
        Assert.Equal(user, served.User);
        Assert.Equal("alex", served.DisplayName);
        Assert.Equal([AlexWork.Id.ToString("D")], served.MailAccounts.Select(account => account.AccountId));
    }

    /// <summary>
    /// The version each record was served at is kept beside it, because that is what every later convergence compares
    /// the rows against — a roster settled without it would read and republish every record on the first interval.
    /// </summary>
    [Fact]
    public async Task StartAsync_AUserServedFromTheirRecord_KeepsTheVersionTheRecordWasReadAt()
    {
        // Arrange
        var user = MailUserId.Create(RecordedIdentifier);
        var roster = new ServedMailUsers();

        // Act
        await CreateGate(
                [Held(user, "alex")],
                servedUsers: roster,
                documents: RecordsHolding(Record(user, LanguageOnlyRecord, AlexWork)))
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, roster.PublishedVersionOf(user));
    }

    /// <summary>Every held row is served, because a row this deployment held and did not serve would be somebody whose mail it stores and never synchronizes.</summary>
    [Fact]
    public async Task StartAsync_SeveralUsersHeld_ServesEachOfThemWithTheirOwnMailboxes()
    {
        // Arrange
        var roster = new ServedMailUsers();

        // Act
        await CreateGate(TwoRecordedUsers(), servedUsers: roster, documents: RecordsOfTwoUsers())
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [(MailUserId.Create(RecordedIdentifier), AlexWork.Id.ToString("D")), (SyntheticMailUser.Another, SamWork.Id.ToString("D"))],
            roster.Users.Select(user => (user.User, Assert.Single(user.MailAccounts).AccountId)));
    }

    /// <summary>
    /// A declaration this build will not bind costs its own mailbox and nothing else: the user keeps every other
    /// mailbox they have, and the refusal is published rather than raised, because the alternative was one broken row
    /// taking the whole deployment's mail offline.
    /// </summary>
    [Fact]
    public async Task StartAsync_AUserWhoseMailboxWillNotBind_ServesTheirOtherMailboxesAndHoldsThatOneBack()
    {
        // Arrange
        var user = MailUserId.Create(RecordedIdentifier);
        var roster = new ServedMailUsers();
        var heldBack = new HeldBackRecords();
        var unbindable = SamWork with
        {
            Document = """{"Host":"imap.example.test","Nonsense":"no property binds this"}""",
        };

        // Act
        await CreateGate(
                [Held(user, "alex")],
                servedUsers: roster,
                documents: RecordsHolding(Record(user, LanguageOnlyRecord, AlexWork, unbindable)),
                heldBack: heldBack)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var served = Assert.Single(roster.Users);
        Assert.Equal([AlexWork.Id.ToString("D")], served.MailAccounts.Select(account => account.AccountId));
        var refused = Assert.Single(heldBack.Current);
        Assert.Equal(HeldBackRecordKind.MailAccount, refused.Kind);
        Assert.Equal(unbindable.Id, refused.Identity);
        Assert.NotEmpty(refused.Corrections);
    }

    /// <summary>
    /// A user's own document is the whole of what they are served from, so one that is not a record leaves that user
    /// unserved — and every other user served exactly as they were, which is the guarantee the start used to break.
    /// </summary>
    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"Nonsense":"no property binds this"}""")]
    [InlineData("""{"Language":"Klingon"}""")]
    public async Task StartAsync_AUserWhoseOwnRecordIsNotOne_ServesEverybodyElseAndHoldsThatUserBack(string document)
    {
        // Arrange
        var broken = MailUserId.Create(RecordedIdentifier);
        var roster = new ServedMailUsers();
        var heldBack = new HeldBackRecords();

        // Act
        await CreateGate(
                [Held(broken, "alex"), Held(SyntheticMailUser.Another, "sam")],
                servedUsers: roster,
                documents: RecordsHolding(
                    Record(broken, document, AlexWork),
                    Record(SyntheticMailUser.Another, LanguageOnlyRecord, SamWork)),
                heldBack: heldBack)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var served = Assert.Single(roster.Users);
        Assert.Equal(SyntheticMailUser.Another, served.User);
        var refused = Assert.Single(heldBack.Current);
        Assert.Equal(HeldBackRecordKind.User, refused.Kind);
        Assert.Equal(broken.Value, refused.Identity);
    }

    /// <summary>
    /// A conflict is introduced by the second declaration rather than by both, so the one recorded first is served and
    /// the one that collided with it is what an operator is told to correct.
    /// </summary>
    [Fact]
    public async Task StartAsync_TwoMailboxesSharingADisplayName_ServesTheFirstAndHoldsTheSecondBack()
    {
        // Arrange
        var user = MailUserId.Create(RecordedIdentifier);
        var roster = new ServedMailUsers();
        var heldBack = new HeldBackRecords();
        var colliding = SamWork with { DisplayName = AlexWork.DisplayName };

        // Act
        await CreateGate(
                [Held(user, "alex")],
                servedUsers: roster,
                documents: RecordsHolding(Record(user, LanguageOnlyRecord, AlexWork, colliding)),
                heldBack: heldBack)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var served = Assert.Single(roster.Users);
        Assert.Equal([AlexWork.Id.ToString("D")], served.MailAccounts.Select(account => account.AccountId));
        Assert.Equal(colliding.Id, Assert.Single(heldBack.Current).Identity);
    }

    /// <summary>
    /// A mailbox is a record rather than a configuration key, so no reading of the files walks the secrets it names.
    /// Without this the deployment would start clean and fail one mailbox connection at a time; with it, the mailbox
    /// whose reference nothing resolves is left out and every other mailbox of that user keeps synchronizing.
    /// </summary>
    [Fact]
    public async Task StartAsync_AUserWhoseMailboxNamesASecretNoSchemeResolves_LeavesItOutAndHoldsItBack()
    {
        // Arrange
        var user = MailUserId.Create(RecordedIdentifier);
        var roster = new ServedMailUsers();
        var heldBack = new HeldBackRecords();
        var unresolvable = Mailbox(SamWork.Id, "sam@example.test", "spare", "no-such-scheme:imap-password");

        // Act
        await CreateGate(
                [Held(user, "alex")],
                servedUsers: roster,
                documents: RecordsHolding(Record(user, LanguageOnlyRecord, AlexWork, unresolvable)),
                heldBack: heldBack)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var served = Assert.Single(roster.Users);
        Assert.Equal([AlexWork.Id.ToString("D")], served.MailAccounts.Select(account => account.AccountId));
        var refused = Assert.Single(heldBack.Current);
        Assert.Equal(HeldBackRecordKind.MailAccount, refused.Kind);
        Assert.Equal(unresolvable.Id, refused.Identity);
    }

    /// <summary>A record that binds again once it is repaired leaves nothing behind, so an operator is not told about a row they already fixed.</summary>
    [Fact]
    public async Task StartAsync_EveryRecordBinding_HoldsNothingBack()
    {
        // Arrange
        var heldBack = new HeldBackRecords();

        heldBack.Replace(
            MailUserId.Create(RecordedIdentifier),
            [new HeldBackRecord(HeldBackRecordKind.User, RecordedIdentifier, "alex", 1, ["stale"])]);

        // Act
        await CreateGate(
                [Held(MailUserId.Create(RecordedIdentifier), "alex")],
                documents: RecordsHolding(Record(MailUserId.Create(RecordedIdentifier), LanguageOnlyRecord, AlexWork)),
                heldBack: heldBack)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(heldBack.Current);
    }

    /// <summary>
    /// An account an upgrade could derive no address for is not served, and the operator is told which user it
    /// belongs to and how to state one, rather than finding a mailbox that is silently never synchronized.
    /// </summary>
    [Fact]
    public async Task StartAsync_AUserAssignedAMailboxHoldingNoAddress_ServesThemWithoutItAndSaysHowToStateOne()
    {
        // Arrange
        var user = MailUserId.Create(RecordedIdentifier);
        var roster = new ServedMailUsers();
        var startupLog = new RecordingLogger<ServedMailUsersStartupGate>();

        // Act
        await CreateGate(
                [Held(user, "alex")],
                servedUsers: roster,
                documents: RecordsHolding(Record(user, LanguageOnlyRecord, AlexWork with { EmailAddress = null })),
                startupLog: startupLog)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(Assert.Single(roster.Users).MailAccounts);
        Assert.Contains(
            startupLog.Messages,
            message => message.Contains("hold no email address", StringComparison.Ordinal)
                && message.Contains("mfctl account edit", StringComparison.Ordinal));
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
                .StartAsync(TestContext.Current.CancellationToken));

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
                .StartAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("requires no authentication", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal above counts users rather than forbidding the posture, so a deployment serving one person or nobody
    /// keeps an unauthenticated surface — which is what lets a first run reach a client with no credential and record
    /// the person it is for.
    /// </summary>
    /// <param name="held">Whether the deployment holds its first user yet.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartAsync_AtMostOneUserOnAnUnauthenticatedUserFacingSurface_IsServedAnyway(bool held)
    {
        // Arrange
        var user = MailUserId.Create(RecordedIdentifier);
        var roster = new ServedMailUsers();

        // Act
        await CreateGate(
                held ? [Held(user, "alex")] : [],
                servedUsers: roster,
                documents: RecordsHolding(Record(user, LanguageOnlyRecord)),
                mcpEndpointSettings: new McpEndpointOptions { Enabled = true },
                clientEndpointSettings: new ClientEndpointOptions { Enabled = true })
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(held ? 1 : 0, roster.Users.Count);
    }

    /// <summary>
    /// An administrator acts for the deployment rather than for a person, so every user-scoped act of theirs names the
    /// user it is for and none of them has to be resolved from the roster. The administrative surface is therefore not
    /// among the ones this refusal reads, which is what makes recording a second user reachable at all.
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
    /// A rule's scope is a claim about somebody's mailbox, and the roster can lose that mailbox after the rule was
    /// accepted — its user is erased, or the account is. A start refusing then could be undone only through the host
    /// it refused, so it starts and names the rule rather than letting it reach no mail in silence.
    /// </summary>
    [Fact]
    public async Task StartAsync_ARuleScopedToAMailboxNobodyIsAssigned_StartsAndReportsTheRule()
    {
        // Arrange
        var roster = new ServedMailUsers();
        var startupLog = new RecordingLogger<ServedMailUsersStartupGate>();

        // Act
        await CreateGate(
                [Held(SyntheticMailUser.Deployment, "alex")],
                RuleScopedTo("nobody-records-this"),
                servedUsers: roster,
                documents: RecordsHolding(Record(SyntheticMailUser.Deployment, LanguageOnlyRecord, AlexWork)),
                startupLog: startupLog)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(roster.Users);
        Assert.Contains(
            startupLog.Messages,
            message => message.Contains("a mail account named 'nobody-records-this'", StringComparison.Ordinal));
    }

    /// <summary>The control for the report above: a rule scoped to a mailbox assigned to a served user is reported by nothing.</summary>
    [Fact]
    public async Task StartAsync_ARuleScopedToAMailboxAServedUserIsAssigned_ReportsNothingAboutTheRule()
    {
        // Arrange
        var startupLog = new RecordingLogger<ServedMailUsersStartupGate>();

        // Act
        await CreateGate(
                [Held(SyntheticMailUser.Deployment, "alex")],
                RuleScopedTo(AlexWork.Id.ToString("D")),
                documents: RecordsHolding(Record(SyntheticMailUser.Deployment, LanguageOnlyRecord, AlexWork)),
                startupLog: startupLog)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(
            startupLog.Messages,
            message => message.Contains(MailRulesOptions.SectionName, StringComparison.Ordinal));
    }

    /// <summary>
    /// One account is a mailbox several people may share, and it is served to each of them under the one identifier
    /// the deployment generated for it, so two users assigned it start the deployment rather than colliding.
    /// </summary>
    [Fact]
    public async Task StartAsync_TwoUsersAssignedOneMailbox_ServesItToBoth()
    {
        // Arrange
        var roster = new ServedMailUsers();

        // Act
        await CreateGate(
                [Held(SyntheticMailUser.Deployment, "alex"), Held(SyntheticMailUser.Another, "sam")],
                servedUsers: roster,
                documents: RecordsHolding(
                    Record(SyntheticMailUser.Deployment, LanguageOnlyRecord, AlexWork),
                    Record(SyntheticMailUser.Another, LanguageOnlyRecord, AlexWork)))
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.All(
            roster.Users,
            user => Assert.Equal(AlexWork.Id.ToString("D"), Assert.Single(user.MailAccounts).AccountId));
        Assert.Equal(2, roster.Users.Count);
    }

    /// <summary>The language a user's record states reaches the roster, which is where every derivation reads it from.</summary>
    [Theory]
    [InlineData("Polish", MailUserLanguage.Polish)]
    [InlineData("English", MailUserLanguage.English)]
    public async Task StartAsync_AUserWhoseRecordNamesALanguage_PublishesIt(string written, MailUserLanguage expected)
    {
        // Arrange
        var user = MailUserId.Create(RecordedIdentifier);
        var roster = new ServedMailUsers();

        // Act
        await CreateGate(
                [Held(user, "alex")],
                servedUsers: roster,
                documents: RecordsHolding(Record(user, $$"""{"Language":"{{written}}"}""")))
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, Assert.Single(roster.Users).Language);
    }

    /// <summary>
    /// A record committed before the property existed names no language, and nothing could have added one to it in
    /// advance, so the start reads it as English rather than refusing the deployment its own administrative surface.
    /// </summary>
    [Fact]
    public async Task StartAsync_AUserWhoseRecordNamesNoLanguage_ServesThemInEnglish()
    {
        // Arrange
        var user = MailUserId.Create(RecordedIdentifier);
        var roster = new ServedMailUsers();

        // Act
        await CreateGate(
                [Held(user, "alex")],
                servedUsers: roster,
                documents: RecordsHolding(Record(user, "{}")))
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailUserLanguage.English, Assert.Single(roster.Users).Language);
    }

    /// <summary>The scanning posture an account asked for in the record reaches the roster on that mailbox.</summary>
    [Fact]
    public async Task StartAsync_AnAccountWhoseRecordAsksForAScanner_PublishesWhatItAskedFor()
    {
        // Arrange
        var user = MailUserId.Create(RecordedIdentifier);
        var roster = new ServedMailUsers();
        var scanned = AlexWork with
        {
            Document = """
                {
                  "Host": "imap.example.test",
                  "UserName": "alex@example.test",
                  "Secrets": { "Password": { "Name": "imap-password", "SecretReference": "systemd-credential:imap-password" } },
                  "SensitiveContent": { "Secrets": { "Enabled": true }, "ScreenOutgoingMailFor": [ "Secrets" ] }
                }
                """,
        };

        // Act
        await CreateGate(
                [Held(user, "alex")],
                servedUsers: roster,
                documents: RecordsHolding(Record(user, LanguageOnlyRecord, scanned)))
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var account = Assert.Single(Assert.Single(roster.Users).MailAccounts);

        Assert.True(account.SensitiveContent.Secrets.Enabled);
        Assert.Equal(["Secrets"], account.SensitiveContent.ScreenOutgoingMailFor!);
    }

    [Fact]
    public async Task StartAsync_AServedRoster_ReportsTheUserGateToTheStartupProbe()
    {
        // Arrange
        var startupGates = new HostStartupGates(HostStartupGate.ServedMailUsers);

        // Act
        await CreateGate(
                [Held(SyntheticMailUser.Deployment, "alex")],
                startupGates: startupGates,
                documents: RecordsHolding(Record(SyntheticMailUser.Deployment, LanguageOnlyRecord)))
            .StartAsync(TestContext.Current.CancellationToken);

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
                    [.. Enumerable
                        .Range(1, ServedMailUsers.MaximumUsers + 1)
                        .Select(position => Held(
                            MailUserId.Create(new Guid(position, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0])),
                            $"user-{position}"))],
                    startupGates: startupGates)
                .StartAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.False(startupGates.Completed);
    }

    /// <summary>
    /// A user erased between the statement that listed the roster and the read of their record is a race rather than a
    /// record to correct, so the start completes, nobody is held back over it, and the next start reads a roster
    /// without them.
    /// </summary>
    [Fact]
    public async Task StartAsync_AUserErasedWhileTheRosterWasRead_CompletesTheGateHoldingNothingBack()
    {
        // Arrange
        var startupGates = new HostStartupGates(HostStartupGate.ServedMailUsers);
        var roster = new ServedMailUsers();
        var heldBack = new HeldBackRecords();

        // Act
        await CreateGate(
                [Held(SyntheticMailUser.Deployment, "alex")],
                servedUsers: roster,
                startupGates: startupGates,
                documents: Substitute.For<IUserSettingsDocumentReader>(),
                heldBack: heldBack)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(roster.Users);
        Assert.Empty(heldBack.Current);
        Assert.True(startupGates.Completed);
    }

    /// <summary>Reading one row more than a deployment may hold is what makes a roster past the bound observable rather than silently truncated.</summary>
    [Fact]
    public async Task StartAsync_ARosterPastTheBound_FailsStartupNamingWhatADeploymentMayHold()
    {
        // Arrange
        var held = Enumerable
            .Range(0, ServedMailUsers.MaximumUsers + 1)
            .Select(position => Held(MailUserId.Create(Guid.NewGuid()), $"user-{position}"))
            .ToArray();

        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailUserUnresolvedException>(() =>
            CreateGate(held).StartAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(
            ServedMailUsers.MaximumUsers.ToString(CultureInfo.InvariantCulture),
            refusal.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_AlwaysGiven_ReadsOneUserMoreThanADeploymentMayHold()
    {
        // Arrange
        var directory = DirectoryOf([]);

        // Act
        await CreateGate(directory).StartAsync(TestContext.Current.CancellationToken);

        // Assert
        await directory.Received(1)
            .ReadUsersAsync(ServedMailUsers.MaximumUsers + 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_TheCallersToken_PropagatesItToTheDirectory()
    {
        // Arrange
        var directory = DirectoryOf([]);
        using var cancellation = new CancellationTokenSource();

        // Act
        await CreateGate(directory).StartAsync(cancellation.Token);

        // Assert
        await directory.Received(1).ReadUsersAsync(Arg.Any<int>(), cancellation.Token);
    }

    /// <summary>
    /// The ordinary reading: a path naming one account's index refuses that account alone, and the mailboxes nothing
    /// was said about go on synchronizing.
    /// </summary>
    [Fact]
    public void MailAccountsTheErrorsLeaveUsable_AnErrorNamingOneAccount_LeavesEveryOtherAccountUsable()
    {
        // Arrange
        var heldBack = new List<HeldBackRecord>();
        var accounts = new[] { Declared(AlexWork.Id, "work"), Declared(SamWork.Id, "spare") };

        // Act
        var usable = ServedMailUsersStartupGate.MailAccountsTheErrorsLeaveUsable(
            ["document:MailAccounts:1:Secrets:Password: the reference resolves to nothing."],
            accounts,
            new Dictionary<Guid, long> { [AlexWork.Id] = 3, [SamWork.Id] = 4 },
            heldBack);

        // Assert
        Assert.Equal([AlexWork.Id.ToString("D")], usable.Select(account => account.AccountId));
        var refused = Assert.Single(heldBack);
        Assert.Equal(SamWork.Id, refused.Identity);
        Assert.Equal(4, refused.RejectedVersion);
    }

    /// <summary>
    /// The prefixes are mutually exclusive, so an error under a path naming no account is a validator this gate no
    /// longer understands. Serving a mailbox on the strength of an error nobody could read would be exactly the
    /// unproven secret the resolution exists to catch, so every mailbox of that user is held back with everything that
    /// was said — and the shortfall is what decides it, which is why the error count rather than the wording is
    /// asserted here.
    /// </summary>
    [Fact]
    public void MailAccountsTheErrorsLeaveUsable_AnErrorNamingNoAccount_HoldsEveryMailboxOfThatUserBack()
    {
        // Arrange
        var heldBack = new List<HeldBackRecord>();
        var accounts = new[] { Declared(AlexWork.Id, "work"), Declared(SamWork.Id, "spare") };
        string[] errors =
        [
            "document:Elsewhere:TransportSecurity: the trust anchor resolves to nothing.",
            "document:MailAccounts:1:Secrets:Password: the reference resolves to nothing.",
        ];

        // Act
        var usable = ServedMailUsersStartupGate.MailAccountsTheErrorsLeaveUsable(
            errors,
            accounts,
            new Dictionary<Guid, long> { [AlexWork.Id] = 3, [SamWork.Id] = 4 },
            heldBack);

        // Assert
        Assert.Empty(usable);
        Assert.Equal([SamWork.Id, AlexWork.Id], heldBack.Select(record => record.Identity));
        Assert.Equal(errors, heldBack.Single(record => record.Identity == AlexWork.Id).Corrections);
    }

    private static MailUserRecord Held(MailUserId user, string displayName) =>
        new(user, displayName);

    /// <summary>A mailbox as the composition hands it to the secret resolution, which keys it by the record's identifier.</summary>
    private static MailSynchronizationAccountOptions Declared(Guid id, string displayName) =>
        new() { AccountId = id.ToString("D"), DisplayName = displayName };

    /// <summary>A mailbox as its own record holds it, which is the shape every one of these tests states a mailbox in.</summary>
    private static MailAccountRecord Mailbox(
        Guid id,
        string emailAddress,
        string displayName,
        string secretReference = "systemd-credential:imap-password") =>
        new(
            id,
            emailAddress,
            displayName,
            $$"""
              {
                "Host": "imap.example.test",
                "UserName": "{{emailAddress}}",
                "Secrets": { "Password": { "Name": "imap-password", "SecretReference": "{{secretReference}}" } }
              }
              """,
            Version: 1);

    /// <summary>One user's record, and the mail accounts assigned to them.</summary>
    private static UserSettingsDocument Record(MailUserId user, string json, params MailAccountRecord[] accounts) =>
        new(user, $"user-{user.Value:D}", json, Version: 2) { MailAccounts = accounts };

    /// <summary>The reader answering each named user with the record beside them, and nobody else with anything.</summary>
    private static IUserSettingsDocumentReader RecordsHolding(params UserSettingsDocument[] records)
    {
        var documents = Substitute.For<IUserSettingsDocumentReader>();

        foreach (var record in records)
        {
            documents.ReadAsync(record.User, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<UserSettingsDocument?>(record));
        }

        return documents;
    }

    private static IConfiguration Configuration(Dictionary<string, string?> keys) =>
        new ConfigurationBuilder().AddInMemoryCollection(keys).Build();

    /// <summary>The one switch a deployment with no mailbox recorded yet is most likely to have turned on.</summary>
    private static IConfiguration SynchronizationSwitchedOn() =>
        Configuration(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{MailSynchronizationOptions.SectionName}:{nameof(MailSynchronizationOptions.Enabled)}"] = "true",
        });

    /// <summary>One declared rule, scoped to the mailbox identifier a test names.</summary>
    private static IConfiguration RuleScopedTo(string accountId) =>
        Configuration(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{MailRulesOptions.SectionName}:{nameof(MailRulesOptions.Rules)}:0:Name"] = "file-invoices",
            [$"{MailRulesOptions.SectionName}:{nameof(MailRulesOptions.Rules)}:0:Condition"] = "isSeen",
            [$"{MailRulesOptions.SectionName}:{nameof(MailRulesOptions.Rules)}:0:Accounts:0"] = accountId,
        });

    /// <summary>Two users, each holding a record of their own.</summary>
    private static MailUserRecord[] TwoRecordedUsers() =>
    [
        Held(MailUserId.Create(RecordedIdentifier), "alex"),
        Held(SyntheticMailUser.Another, "sam"),
    ];

    /// <summary>The records those two users are served from, each assigned a mailbox of their own.</summary>
    private static IUserSettingsDocumentReader RecordsOfTwoUsers() =>
        RecordsHolding(
            Record(MailUserId.Create(RecordedIdentifier), LanguageOnlyRecord, AlexWork),
            Record(SyntheticMailUser.Another, LanguageOnlyRecord, SamWork));

    private static IMailUserDirectory DirectoryOf(IReadOnlyList<MailUserRecord> held)
    {
        var directory = Substitute.For<IMailUserDirectory>();

        directory.ReadUsersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(held));

        return directory;
    }

    private static ServedMailUsersStartupGate CreateGate(
        IReadOnlyList<MailUserRecord> held,
        IConfiguration? declared = null,
        ServedMailUsers? servedUsers = null,
        HostStartupGates? startupGates = null,
        McpEndpointOptions? mcpEndpointSettings = null,
        IUserSettingsDocumentReader? documents = null,
        ClientEndpointOptions? clientEndpointSettings = null,
        ILogger<ServedMailUsersStartupGate>? startupLog = null,
        HeldBackRecords? heldBack = null) =>
        CreateGate(
            DirectoryOf(held),
            declared,
            servedUsers,
            startupGates,
            mcpEndpointSettings,
            documents,
            clientEndpointSettings,
            startupLog,
            heldBack);

    private static ServedMailUsersStartupGate CreateGate(
        IMailUserDirectory directory,
        IConfiguration? declared = null,
        ServedMailUsers? servedUsers = null,
        HostStartupGates? startupGates = null,
        McpEndpointOptions? mcpEndpointSettings = null,
        IUserSettingsDocumentReader? documents = null,
        ClientEndpointOptions? clientEndpointSettings = null,
        ILogger<ServedMailUsersStartupGate>? startupLog = null,
        HeldBackRecords? heldBack = null)
    {
        var services = new ServiceCollection();
        var binder = new UserAccountDocumentBinder(
            new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
            new FakeTimeProvider(),
            Options.Create(new SensitiveContentOptions()));

        services.AddScoped(_ => directory);
        services.AddScoped(_ => documents ?? Substitute.For<IUserSettingsDocumentReader>());
        services.AddSingleton(binder);
        services.AddSingleton(new ServedUserRecordComposition(binder));
        services.AddSingleton(SecretValidation.OverRegisteredSchemes());

        return new ServedMailUsersStartupGate(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            declared ?? new ConfigurationBuilder().Build(),
            new NCalcMailRuleConditionCompiler(),
            servedUsers ?? new ServedMailUsers(),
            heldBack ?? new HeldBackRecords(),
            startupGates ?? new HostStartupGates(HostStartupGate.ServedMailUsers),
            new SeveralUserAdmission(
                Options.Create(mcpEndpointSettings ?? new McpEndpointOptions()),
                Options.Create(clientEndpointSettings ?? new ClientEndpointOptions())),
            startupLog ?? NullLogger<ServedMailUsersStartupGate>.Instance);
    }
}
