// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Records;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Observability.ClientTelemetry;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.TestSupport;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>
/// Covers how one user is composed from the record their own row holds, together with the mail accounts assigned to
/// them, and what a record this build will not bind costs: its own mailbox, or that one user, and never anybody else.
/// </summary>
public sealed class ServedUserResolutionTests
{
    /// <summary>The record a provisioning leaves behind, which declares nothing until its user asks for something.</summary>
    private const string EmptyRecord = "{}";

    private static readonly UserId Recorded = UserId.Create(new Guid("33333333-3333-3333-3333-333333333333"));

    private static readonly MailAccountRecord AlexWork = Mailbox(
        new Guid("0197a3c0-0000-7000-8000-000000000001"),
        "alex@example.test",
        "work");

    private static readonly MailAccountRecord SamWork = Mailbox(
        new Guid("0197a3c0-0000-7000-8000-000000000002"),
        "sam@example.test",
        "work");

    private readonly IUserSettingsDocumentReader documents = Substitute.For<IUserSettingsDocumentReader>();

    private readonly HeldBackRecords heldBack = new();

    private readonly RecordingLogger<ServedUserResolution> log = new();

    /// <summary>The ordinary user: one row, and the mailbox assigned to them, served under the identifier the deployment generated for it.</summary>
    [Fact]
    public async Task ResolveAsync_AUserAssignedAMailbox_ServesThemWithIt()
    {
        // Arrange
        this.Holding(Record(Recorded, EmptyRecord, AlexWork));

        // Act
        var served = await this.ResolveServedAsync(Recorded);

        // Assert
        Assert.Equal(Recorded, served.User);
        Assert.Equal($"user-{Recorded.Value:D}", served.DisplayName);
        Assert.Equal([AlexWork.Id.ToString("D")], served.MailAccounts.Select(account => account.AccountId));
    }

    /// <summary>
    /// The version a user was composed at is answered beside them, because that is what every later convergence compares
    /// the row against — a user held without it would be read and recomposed on the first interval.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_AUserServedFromTheirRecord_AnswersTheVersionTheRecordWasReadAt()
    {
        // Arrange
        this.Holding(Record(Recorded, EmptyRecord, AlexWork));

        // Act
        var resolved = await this.Resolution().ResolveAsync(Recorded, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, resolved?.Version);
    }

    /// <summary>Each user is composed from their own row alone, so two users are each served with their own mailboxes.</summary>
    [Fact]
    public async Task ResolveAsync_TwoUsersHeld_ServesEachOfThemWithTheirOwnMailboxes()
    {
        // Arrange
        this.Holding(Record(Recorded, EmptyRecord, AlexWork), Record(SyntheticUser.Another, EmptyRecord, SamWork));

        // Act
        var first = await this.ResolveServedAsync(Recorded);
        var second = await this.ResolveServedAsync(SyntheticUser.Another);

        // Assert
        Assert.Equal(
            [(Recorded, AlexWork.Id.ToString("D")), (SyntheticUser.Another, SamWork.Id.ToString("D"))],
            new[] { first, second }.Select(user => (user.User, Assert.Single(user.MailAccounts).AccountId)));
    }

    /// <summary>
    /// A declaration this build will not bind costs its own mailbox and nothing else: the user keeps every other
    /// mailbox they have, and the refusal is published rather than raised, because the alternative was one broken row
    /// taking the whole deployment's mail offline.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_AUserWhoseMailboxWillNotBind_ServesTheirOtherMailboxesAndHoldsThatOneBack()
    {
        // Arrange
        var unbindable = SamWork with
        {
            Document = """{"Host":"imap.example.test","Nonsense":"no property binds this"}""",
        };
        this.Holding(Record(Recorded, EmptyRecord, AlexWork, unbindable));

        // Act
        var served = await this.ResolveServedAsync(Recorded);

        // Assert
        Assert.Equal([AlexWork.Id.ToString("D")], served.MailAccounts.Select(account => account.AccountId));
        var refused = Assert.Single(this.heldBack.Current);
        Assert.Equal(HeldBackRecordKind.MailAccount, refused.Kind);
        Assert.Equal(unbindable.Id, refused.Identity);
        Assert.NotEmpty(refused.Corrections);
    }

    /// <summary>
    /// A user's own document is the whole of what they are served from, so one that is not a record leaves that user
    /// unserved and holds them back — and every other user served exactly as they were.
    /// </summary>
    /// <param name="document">The document the broken user's row holds.</param>
    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"Nonsense":"no property binds this"}""")]
    [InlineData("""{"Portrait":"not an identifier"}""")]
    public async Task ResolveAsync_AUserWhoseOwnRecordIsNotOne_ServesEverybodyElseAndHoldsThatUserBack(string document)
    {
        // Arrange
        this.Holding(Record(Recorded, document, AlexWork), Record(SyntheticUser.Another, EmptyRecord, SamWork));
        var resolution = this.Resolution();

        // Act
        var broken = await resolution.ResolveAsync(Recorded, TestContext.Current.CancellationToken);
        var other = await resolution.ResolveAsync(SyntheticUser.Another, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(broken);
        Assert.Null(broken.Value.User);
        Assert.Equal(SyntheticUser.Another, other?.User?.User);
        var refused = Assert.Single(this.heldBack.Current);
        Assert.Equal(HeldBackRecordKind.User, refused.Kind);
        Assert.Equal(Recorded.Value, refused.Identity);
    }

    /// <summary>
    /// Two of one person's mailboxes on one server are provisioned from one credential, and a name is what a rotation
    /// instruction calls it. Requiring a second name for the same material would leave one credential answering to two
    /// names in the log, which is the ambiguity the uniqueness rule exists to prevent.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_TwoMailboxesNamingOneCredentialIdentically_ServesBoth()
    {
        // Arrange
        var second = SamWork with { DisplayName = "spare" };
        this.Holding(Record(Recorded, EmptyRecord, AlexWork, second));

        // Act
        var served = await this.ResolveServedAsync(Recorded);

        // Assert
        Assert.Equal(
            [AlexWork.Id.ToString("D"), second.Id.ToString("D")],
            served.MailAccounts.Select(account => account.AccountId));
        Assert.Empty(this.heldBack.Current);
    }

    /// <summary>
    /// A conflict is introduced by the second declaration rather than by both, so the one recorded first is served and
    /// the one that collided with it is what an operator is told to correct.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_TwoMailboxesSharingADisplayName_ServesTheFirstAndHoldsTheSecondBack()
    {
        // Arrange
        var colliding = SamWork with { DisplayName = AlexWork.DisplayName };
        this.Holding(Record(Recorded, EmptyRecord, AlexWork, colliding));

        // Act
        var served = await this.ResolveServedAsync(Recorded);

        // Assert
        Assert.Equal([AlexWork.Id.ToString("D")], served.MailAccounts.Select(account => account.AccountId));
        Assert.Equal(colliding.Id, Assert.Single(this.heldBack.Current).Identity);
    }

    /// <summary>
    /// A mailbox is a record rather than a configuration key, so no reading of the files walks the secrets it names.
    /// Without this the user would be served cleanly and fail one mailbox connection at a time; with it, the mailbox
    /// whose reference nothing resolves is left out and every other mailbox of that user keeps synchronizing.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_AUserWhoseMailboxNamesASecretNoSchemeResolves_LeavesItOutAndHoldsItBack()
    {
        // Arrange
        var unresolvable = Mailbox(SamWork.Id, "sam@example.test", "spare", "no-such-scheme:imap-password");
        this.Holding(Record(Recorded, EmptyRecord, AlexWork, unresolvable));

        // Act
        var served = await this.ResolveServedAsync(Recorded);

        // Assert
        Assert.Equal([AlexWork.Id.ToString("D")], served.MailAccounts.Select(account => account.AccountId));
        var refused = Assert.Single(this.heldBack.Current);
        Assert.Equal(HeldBackRecordKind.MailAccount, refused.Kind);
        Assert.Equal(unresolvable.Id, refused.Identity);
    }

    /// <summary>A record that binds again once it is repaired leaves nothing behind, so an operator is not told about a row they already fixed.</summary>
    [Fact]
    public async Task ResolveAsync_EveryRecordBinding_HoldsNothingBack()
    {
        // Arrange
        this.heldBack.Replace(
            Recorded,
            [new HeldBackRecord(HeldBackRecordKind.User, Recorded.Value, "alex", 1, ["stale"])]);
        this.Holding(Record(Recorded, EmptyRecord, AlexWork));

        // Act
        await this.ResolveServedAsync(Recorded);

        // Assert
        Assert.Empty(this.heldBack.Current);
    }

    /// <summary>
    /// An account an upgrade could derive no address for is not served, and the operator is told which user it
    /// belongs to and how to state one, rather than finding a mailbox that is silently never synchronized.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_AUserAssignedAMailboxHoldingNoAddress_ServesThemWithoutItAndSaysHowToStateOne()
    {
        // Arrange
        this.Holding(Record(Recorded, EmptyRecord, AlexWork with { EmailAddress = null }));

        // Act
        var served = await this.ResolveServedAsync(Recorded);

        // Assert
        Assert.Empty(served.MailAccounts);
        Assert.Contains(
            this.log.Messages,
            message => message.Contains("hold no email address", StringComparison.Ordinal)
                && message.Contains("mfctl account edit", StringComparison.Ordinal));
    }

    /// <summary>
    /// One account is a mailbox several people may share, and it is served to each of them under the one identifier
    /// the deployment generated for it, so two users assigned it are both served rather than colliding.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_TwoUsersAssignedOneMailbox_ServesItToBoth()
    {
        // Arrange
        this.Holding(
            Record(SyntheticUser.Deployment, EmptyRecord, AlexWork),
            Record(SyntheticUser.Another, EmptyRecord, AlexWork));

        // Act
        var first = await this.ResolveServedAsync(SyntheticUser.Deployment);
        var second = await this.ResolveServedAsync(SyntheticUser.Another);

        // Assert
        Assert.All(
            new[] { first, second },
            user => Assert.Equal(AlexWork.Id.ToString("D"), Assert.Single(user.MailAccounts).AccountId));
    }

    /// <summary>The language a user's own record states reaches the served user, which is where a card and a first draft read it from.</summary>
    /// <param name="written">The language the record states.</param>
    /// <param name="expected">The language the user is served in.</param>
    [Theory]
    [InlineData("Polish", UserLanguage.Polish)]
    [InlineData("English", UserLanguage.English)]
    public async Task ResolveAsync_AUserWhoseRecordNamesALanguage_ServesItWithThem(string written, UserLanguage expected)
    {
        // Arrange
        this.Holding(Record(Recorded, $$"""{"Language":"{{written}}"}""", AlexWork));

        // Act
        var served = await this.ResolveServedAsync(Recorded);

        // Assert
        Assert.Equal(expected, served.Language);
    }

    /// <summary>
    /// A record held from before the property existed names no language, and nothing could have added one to it in
    /// advance, so it reads as English rather than leaving its user unserved.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_AUserWhoseRecordNamesNoLanguage_ServesThemInEnglish()
    {
        // Arrange
        this.Holding(Record(Recorded, EmptyRecord, AlexWork));

        // Act
        var served = await this.ResolveServedAsync(Recorded);

        // Assert
        Assert.Equal(UserLanguage.English, served.Language);
    }

    /// <summary>A level raised before the process last started is still raised after it, read off the same record every other reader reads.</summary>
    [Fact]
    public async Task ResolveAsync_AUserWhoseRecordStatesAClientTelemetryLevel_ServesItWithThem()
    {
        // Arrange
        this.Holding(Record(Recorded, """{"Language":"English","ClientTelemetryLevel":"Debug"}""", AlexWork));

        // Act
        var served = await this.ResolveServedAsync(Recorded);

        // Assert
        Assert.Equal(ClientTelemetryLevel.Debug, served.ClientTelemetryLevel);
    }

    /// <summary>
    /// A record held from before the key existed states no level and reads as nothing, which is what has the session
    /// route go on answering the deployment's own level for everybody nobody raised.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_AUserWhoseRecordStatesNoClientTelemetryLevel_ServesNoLevel()
    {
        // Arrange
        this.Holding(Record(Recorded, EmptyRecord, AlexWork));

        // Act
        var served = await this.ResolveServedAsync(Recorded);

        // Assert
        Assert.Null(served.ClientTelemetryLevel);
    }

    /// <summary>The language an account's declaration states reaches the served mailbox, which is where every derivation reads it from.</summary>
    /// <param name="written">The language the declaration states.</param>
    /// <param name="expected">The language the mailbox is read in.</param>
    [Theory]
    [InlineData("Polish", MailAccountLanguage.Polish)]
    [InlineData("English", MailAccountLanguage.English)]
    public async Task ResolveAsync_AnAccountWhoseDeclarationNamesALanguage_ServesItWithTheMailbox(
        string written,
        MailAccountLanguage expected)
    {
        // Arrange
        this.Holding(Record(Recorded, EmptyRecord, MailboxReading(written)));

        // Act
        var served = await this.ResolveServedAsync(Recorded);

        // Assert
        Assert.Equal(expected, Assert.Single(served.MailAccounts).ReadingLanguage);
    }

    /// <summary>An account recorded before the property existed names no language, and is served with the language left unstated rather than refused.</summary>
    [Fact]
    public async Task ResolveAsync_AnAccountWhoseDeclarationNamesNoLanguage_LeavesTheLanguageUnstated()
    {
        // Arrange
        this.Holding(Record(Recorded, EmptyRecord, AlexWork));

        // Act
        var served = await this.ResolveServedAsync(Recorded);

        // Assert
        Assert.Null(Assert.Single(served.MailAccounts).ReadingLanguage);
    }

    /// <summary>The scanning posture an account asked for in its record reaches the served mailbox.</summary>
    [Fact]
    public async Task ResolveAsync_AnAccountWhoseRecordAsksForAScanner_ServesWhatItAskedFor()
    {
        // Arrange
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
        this.Holding(Record(Recorded, EmptyRecord, scanned));

        // Act
        var served = await this.ResolveServedAsync(Recorded);

        // Assert
        var account = Assert.Single(served.MailAccounts);
        Assert.True(account.SensitiveContent.Secrets.Enabled);
        Assert.Equal(["Secrets"], account.SensitiveContent.ScreenOutgoingMailFor!);
    }

    /// <summary>
    /// A user erased between the statement that named them and the read of their record is a race rather than a record
    /// to correct, so nobody is served and nothing is held back over it — including what an earlier read reported.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_AUserErasedWhileTheirRecordWasRead_ServesNobodyAndHoldsNothingBack()
    {
        // Arrange
        this.heldBack.Replace(
            Recorded,
            [new HeldBackRecord(HeldBackRecordKind.User, Recorded.Value, "alex", 1, ["stale"])]);

        // Act
        var resolved = await this.Resolution().ResolveAsync(Recorded, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(resolved);
        Assert.Empty(this.heldBack.Current);
    }

    /// <summary>
    /// The ordinary reading: a path naming one account's index refuses that account alone, and the mailboxes nothing
    /// was said about go on synchronizing.
    /// </summary>
    [Fact]
    public void MailAccountsTheErrorsLeaveUsable_AnErrorNamingOneAccount_LeavesEveryOtherAccountUsable()
    {
        // Arrange
        var heldBackRecords = new List<HeldBackRecord>();
        var accounts = new[] { Declared(AlexWork.Id, "work"), Declared(SamWork.Id, "spare") };

        // Act
        var usable = ServedUserResolution.MailAccountsTheErrorsLeaveUsable(
            ["document:MailAccounts:1:Secrets:Password: the reference resolves to nothing."],
            accounts,
            new Dictionary<Guid, long> { [AlexWork.Id] = 3, [SamWork.Id] = 4 },
            heldBackRecords);

        // Assert
        Assert.Equal([AlexWork.Id.ToString("D")], usable.Select(account => account.AccountId));
        var refused = Assert.Single(heldBackRecords);
        Assert.Equal(SamWork.Id, refused.Identity);
        Assert.Equal(4, refused.RejectedVersion);
    }

    /// <summary>
    /// The prefixes are mutually exclusive, so an error under a path naming no account is a validator this resolution no
    /// longer understands. Serving a mailbox on the strength of an error nobody could read would be exactly the
    /// unproven secret the resolution exists to catch, so every mailbox of that user is held back with everything that
    /// was said — and the shortfall is what decides it, which is why the error count rather than the wording is
    /// asserted here.
    /// </summary>
    [Fact]
    public void MailAccountsTheErrorsLeaveUsable_AnErrorNamingNoAccount_HoldsEveryMailboxOfThatUserBack()
    {
        // Arrange
        var heldBackRecords = new List<HeldBackRecord>();
        var accounts = new[] { Declared(AlexWork.Id, "work"), Declared(SamWork.Id, "spare") };
        string[] errors =
        [
            "document:Elsewhere:TransportSecurity: the trust anchor resolves to nothing.",
            "document:MailAccounts:1:Secrets:Password: the reference resolves to nothing.",
        ];

        // Act
        var usable = ServedUserResolution.MailAccountsTheErrorsLeaveUsable(
            errors,
            accounts,
            new Dictionary<Guid, long> { [AlexWork.Id] = 3, [SamWork.Id] = 4 },
            heldBackRecords);

        // Assert
        Assert.Empty(usable);
        Assert.Equal([SamWork.Id, AlexWork.Id], heldBackRecords.Select(record => record.Identity));
        Assert.Equal(errors, heldBackRecords.Single(record => record.Identity == AlexWork.Id).Corrections);
    }

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

    /// <summary>One mailbox declaring the language its mail is read in.</summary>
    private static MailAccountRecord MailboxReading(string language) =>
        new(
            new Guid("0197a3c0-0000-7000-8000-000000000003"),
            "alex@example.test",
            "work",
            $$"""
              {
                "Language": "{{language}}",
                "Host": "imap.example.test",
                "UserName": "alex@example.test",
                "Secrets": { "Password": { "Name": "imap-password", "SecretReference": "systemd-credential:imap-password" } }
              }
              """,
            Version: 1);

    /// <summary>One user's record, and the mail accounts assigned to them.</summary>
    private static UserSettingsDocument Record(UserId user, string json, params MailAccountRecord[] accounts) =>
        new(user, $"user-{user.Value:D}", json, Version: 2) { MailAccounts = accounts };

    /// <summary>Answers each named user with the record beside them, and nobody else with anything.</summary>
    private void Holding(params UserSettingsDocument[] records)
    {
        foreach (var record in records)
        {
            this.documents.ReadAsync(record.User, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<UserSettingsDocument?>(record));
        }
    }

    private async Task<ServedUser> ResolveServedAsync(UserId user)
    {
        var resolved = await this.Resolution().ResolveAsync(user, TestContext.Current.CancellationToken);

        return Assert.IsType<ServedUser>(resolved?.User);
    }

    /// <summary>The resolution a deployment composes, over the records this test holds and the schemes every test registers.</summary>
    private ServedUserResolution Resolution()
    {
        var binder = new UserAccountDocumentBinder(
            new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
            new FakeTimeProvider(),
            Options.Create(new SensitiveContentOptions()));

        return new ServedUserResolution(
            this.documents,
            new ServedUserRecordComposition(binder),
            SecretValidation.OverRegisteredSchemes(),
            this.heldBack,
            this.log);
    }
}
