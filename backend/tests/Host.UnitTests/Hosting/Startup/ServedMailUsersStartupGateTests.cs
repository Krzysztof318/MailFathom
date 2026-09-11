// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.Mail;
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
/// composed from, and the refusals a roster it could not serve is stopped by. No configuration source declares a user
/// or a mailbox any more, so every case here is a row and the document beside it.
/// </summary>
public sealed class ServedMailUsersStartupGateTests
{
    private static readonly Guid RecordedIdentifier = new("33333333-3333-3333-3333-333333333333");

    /// <summary>
    /// The state a deployment whose every user was erased is in — a fresh database is seeded with one — and one a start
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
    /// its first recorded mailbox, so it is reported and the deployment runs. It refused a start until this release,
    /// which made the empty deployment unstartable for the one setting an operator turns on first.
    /// </summary>
    [Fact]
    public async Task StartAsync_SynchronizationOnAndNobodyRecordingAMailbox_ReportsItRatherThanRefusing()
    {
        // Arrange
        var roster = new ServedMailUsers();
        var startupLog = new RecordingLogger<ServedMailUsersStartupGate>();

        // Act
        await CreateGate(
                [Held(SyntheticMailUser.Deployment, "alex")],
                SynchronizationSwitchedOn(),
                servedUsers: roster,
                documents: RecordsHolding((SyntheticMailUser.Deployment, "{}")),
                startupLog: startupLog)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(roster.Users);
        Assert.Contains(
            startupLog.Messages,
            message => message.Contains("nothing to synchronize", StringComparison.Ordinal));
    }

    /// <summary>The ordinary deployment: one row, and the mailbox that user's own record names.</summary>
    [Fact]
    public async Task StartAsync_AUserWhoseRecordNamesAMailbox_ServesThemFromIt()
    {
        // Arrange
        var user = MailUserId.Create(RecordedIdentifier);
        var roster = new ServedMailUsers();

        // Act
        await CreateGate(
                [Held(user, "alex")],
                servedUsers: roster,
                documents: RecordsHolding((user, RecordDeclaring("work", "alex@example.test"))))
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var served = Assert.Single(roster.Users);
        Assert.Equal(user, served.User);
        Assert.Equal("alex", served.DisplayName);
        Assert.Equal(["work"], served.MailAccounts.Select(account => account.AccountId));
    }

    /// <summary>Every held row is served, because a row this deployment held and did not serve would be somebody whose mail it stores and never synchronizes.</summary>
    [Fact]
    public async Task StartAsync_SeveralUsersHeld_ServesEachOfThemFromTheirOwnRecord()
    {
        // Arrange
        var roster = new ServedMailUsers();

        // Act
        await CreateGate(TwoRecordedUsers(), servedUsers: roster, documents: RecordsOfTwoUsers())
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [(MailUserId.Create(RecordedIdentifier), "alex-work"), (SyntheticMailUser.Another, "sam-work")],
            roster.Users.Select(user => (user.User, Assert.Single(user.MailAccounts).AccountId)));
    }

    /// <summary>
    /// The alternative to failing is a deployment reporting itself started while synchronizing none of the mailboxes
    /// that user's record names, because the record it was to read them from says nothing usable.
    /// </summary>
    [Fact]
    public async Task StartAsync_AUserWhoseRecordWillNotBind_FailsStartupNamingTheUser()
    {
        // Arrange
        var user = MailUserId.Create(RecordedIdentifier);

        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailUserUnresolvedException>(() =>
            CreateGate(
                    [Held(user, "alex")],
                    documents: RecordsHolding(
                        (user, """{"MailAccounts":[{"AccountId":"work","Nonsense":"no property binds this"}]}""")))
                .StartAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.DeploymentMailUserUnresolved, refusal.ErrorCode);
        Assert.Contains("'alex'", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A mailbox is a user's record rather than a configuration key, so no reading of the files walks the secrets it
    /// names. Without this the deployment would start clean and fail one mailbox connection at a time.
    /// </summary>
    [Fact]
    public async Task StartAsync_AUserWhoseMailboxNamesASecretNoSchemeResolves_FailsStartupNamingTheUser()
    {
        // Arrange
        var user = MailUserId.Create(RecordedIdentifier);

        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailUserUnresolvedException>(() =>
            CreateGate(
                    [Held(user, "alex")],
                    documents: RecordsHolding(
                        (user, RecordDeclaring("work", "alex@example.test", "no-such-scheme:imap-password"))))
                .StartAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("'alex'", refusal.Message, StringComparison.Ordinal);
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
                documents: RecordsHolding((user, "{}")),
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
    /// accepted — its user is erased, or their record stops declaring it. A start refusing then could be undone only
    /// through the host it refused, so it starts and names the rule rather than letting it reach no mail in silence.
    /// </summary>
    [Fact]
    public async Task StartAsync_ARuleScopedToAMailboxNobodyRecords_StartsAndReportsTheRule()
    {
        // Arrange
        var roster = new ServedMailUsers();
        var startupLog = new RecordingLogger<ServedMailUsersStartupGate>();

        // Act
        await CreateGate(
                [Held(SyntheticMailUser.Deployment, "alex")],
                RuleScopedTo("nobody-records-this"),
                servedUsers: roster,
                documents: RecordsHolding(
                    (SyntheticMailUser.Deployment, RecordDeclaring("work", "alex@example.test"))),
                startupLog: startupLog)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(roster.Users);
        Assert.Contains(
            startupLog.Messages,
            message => message.Contains("a mail account named 'nobody-records-this'", StringComparison.Ordinal));
    }

    /// <summary>The control for the report above: a rule scoped to a mailbox a served user's record names is reported by nothing.</summary>
    [Fact]
    public async Task StartAsync_ARuleScopedToAMailboxAServedUserRecords_ReportsNothingAboutTheRule()
    {
        // Arrange
        var startupLog = new RecordingLogger<ServedMailUsersStartupGate>();

        // Act
        await CreateGate(
                [Held(SyntheticMailUser.Deployment, "alex")],
                RuleScopedTo("work"),
                documents: RecordsHolding(
                    (SyntheticMailUser.Deployment, RecordDeclaring("work", "alex@example.test"))),
                startupLog: startupLog)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(
            startupLog.Messages,
            message => message.Contains(MailRulesOptions.SectionName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Two users naming one mailbox identifier no longer stops a start. The deployment-wide naming rule went with the
    /// section that made it deployment-wide, and what is left is a per-account settings lookup keyed by the identifier
    /// alone, which resolves such a name to whichever record it meets first —
    /// <see href="https://github.com/Krzysztof318/MailFathom/issues/1325">issue 1325</see> is what keys it by the user
    /// beside the identifier and ends the ambiguity.
    /// </summary>
    [Fact]
    public async Task StartAsync_TwoUsersWhoseRecordsNameOneMailboxIdentifier_StartsTheDeployment()
    {
        // Arrange
        var roster = new ServedMailUsers();

        // Act
        await CreateGate(
                [Held(SyntheticMailUser.Deployment, "alex"), Held(SyntheticMailUser.Another, "sam")],
                servedUsers: roster,
                documents: RecordsHolding(
                    (SyntheticMailUser.Deployment, RecordDeclaring("work", "alex@example.test")),
                    (SyntheticMailUser.Another, RecordDeclaring("work", "sam@example.test"))))
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, roster.Users.Count);
    }

    /// <summary>The scanning posture a user asked for in their own record reaches the roster beside their mailboxes.</summary>
    [Fact]
    public async Task StartAsync_AUserWhoseRecordAsksForAScanner_PublishesWhatTheyAskedFor()
    {
        // Arrange
        var user = MailUserId.Create(RecordedIdentifier);
        var roster = new ServedMailUsers();

        // Act
        await CreateGate(
                [Held(user, "alex")],
                servedUsers: roster,
                documents: RecordsHolding((
                    user,
                    """{"MailAccounts":[],"SensitiveContent":{"Secrets":{"Enabled":true},"ScreenOutgoingMailFor":["Secrets"]}}""")))
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var served = Assert.Single(roster.Users);

        Assert.True(served.SensitiveContent!.Secrets.Enabled);
        Assert.Equal(["Secrets"], served.SensitiveContent!.ScreenOutgoingMailFor!);
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
                documents: RecordsHolding((SyntheticMailUser.Deployment, "{}")))
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
                    [Held(SyntheticMailUser.Deployment, "alex")],
                    startupGates: startupGates,
                    documents: Substitute.For<IUserSettingsDocumentReader>())
                .StartAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.False(startupGates.Completed);
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
    /// Two people may each call a mailbox <c>work</c>, and nothing refuses it — but the catalogue of served accounts
    /// still keys by the name alone and keeps the first, so a start says so, naming the name and both people.
    /// </summary>
    [Fact]
    public async Task StartAsync_TwoUsersRecordingOneMailAccountName_ReportsTheNameAndBothUsers()
    {
        // Arrange
        var startupLog = new RecordingLogger<ServedMailUsersStartupGate>();

        // Act
        await CreateGate(
                TwoRecordedUsers(),
                documents: RecordsHolding(
                    (MailUserId.Create(RecordedIdentifier), RecordDeclaring("work", "alex@example.test")),
                    (SyntheticMailUser.Another, RecordDeclaring("work", "sam@example.test"))),
                startupLog: startupLog)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            startupLog.Messages,
            message => message.Contains("'work'", StringComparison.Ordinal)
                && message.Contains("'alex'", StringComparison.Ordinal)
                && message.Contains("'sam'", StringComparison.Ordinal));
    }

    private static MailUserRecord Held(MailUserId user, string displayName) =>
        new(user, displayName);

    /// <summary>A record declaring one mailbox, which is the shape every one of these tests states a mailbox in.</summary>
    private static string RecordDeclaring(
        string accountId,
        string userName,
        string secretReference = "systemd-credential:imap-password") =>
        $$"""
          {
            "MailAccounts": [
              {
                "AccountId": "{{accountId}}",
                "DisplayName": "{{accountId}}",
                "Host": "imap.example.test",
                "UserName": "{{userName}}",
                "Secrets": { "Password": { "Name": "imap-password", "SecretReference": "{{secretReference}}" } }
              }
            ]
          }
          """;

    /// <summary>The reader answering each named user with the record beside them, and nobody else with anything.</summary>
    private static IUserSettingsDocumentReader RecordsHolding(params (MailUserId User, string Json)[] records)
    {
        var documents = Substitute.For<IUserSettingsDocumentReader>();

        foreach (var record in records)
        {
            documents.ReadAsync(record.User, Arg.Any<CancellationToken>()).Returns(
                Task.FromResult<UserSettingsDocument?>(new UserSettingsDocument(
                    record.User,
                    $"user-{record.User.Value:D}",
                    record.Json,
                    Version: 2)));
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

    /// <summary>The records those two users are served from, each naming a mailbox of their own.</summary>
    private static IUserSettingsDocumentReader RecordsOfTwoUsers() =>
        RecordsHolding(
            (MailUserId.Create(RecordedIdentifier), RecordDeclaring("alex-work", "alex@example.test")),
            (SyntheticMailUser.Another, RecordDeclaring("sam-work", "sam@example.test")));

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
        ILogger<ServedMailUsersStartupGate>? startupLog = null) =>
        CreateGate(
            DirectoryOf(held),
            declared,
            servedUsers,
            startupGates,
            mcpEndpointSettings,
            documents,
            clientEndpointSettings,
            startupLog);

    private static ServedMailUsersStartupGate CreateGate(
        IMailUserDirectory directory,
        IConfiguration? declared = null,
        ServedMailUsers? servedUsers = null,
        HostStartupGates? startupGates = null,
        McpEndpointOptions? mcpEndpointSettings = null,
        IUserSettingsDocumentReader? documents = null,
        ClientEndpointOptions? clientEndpointSettings = null,
        ILogger<ServedMailUsersStartupGate>? startupLog = null)
    {
        var services = new ServiceCollection();

        services.AddScoped(_ => directory);
        services.AddScoped(_ => documents ?? Substitute.For<IUserSettingsDocumentReader>());
        services.AddSingleton(new UserAccountDocumentBinder(
            new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
            new FakeTimeProvider(),
            Options.Create(new SensitiveContentOptions())));
        services.AddSingleton(SecretValidation.OverRegisteredSchemes());

        return new ServedMailUsersStartupGate(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            declared ?? new ConfigurationBuilder().Build(),
            new NCalcMailRuleConditionCompiler(),
            servedUsers ?? new ServedMailUsers(),
            startupGates ?? new HostStartupGates(HostStartupGate.ServedMailUsers),
            new SeveralUserAdmission(
                Options.Create(mcpEndpointSettings ?? new McpEndpointOptions()),
                Options.Create(clientEndpointSettings ?? new ClientEndpointOptions())),
            startupLog ?? NullLogger<ServedMailUsersStartupGate>.Instance);
    }
}
