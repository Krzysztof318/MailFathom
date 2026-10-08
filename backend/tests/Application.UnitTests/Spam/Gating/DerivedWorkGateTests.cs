// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam.Gating;

public sealed class DerivedWorkGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 10, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Wait = SpamClassificationScope.DefaultMaximumClassificationWait;

    private static readonly MailAccountId Primary = MailAccountId.Create("primary");

    private static readonly MailAccountId Secondary = MailAccountId.Create("secondary");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Junk = MailFolderAlias.Create("JUNK");

    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("ARCHIVE");

    [Fact]
    public async Task Admit_ClassificationSwitchedOff_AdmitsMailInTheJunkFolderItself()
    {
        // Arrange
        var gate = Gate(SpamClassificationSettings.Disabled, StubJunkMailFolderCatalog.Naming(new MailFolderIdentity(Primary, Junk)));

        // Act
        var admission = await AdmitAsync(gate, Occurrence(Junk, Now, verdict: SpamVerdict.Spam));

        // Assert
        Assert.Equal(DerivedWorkAdmission.Admitted, admission);
        Assert.True(admission.PermitsDerivedWork());
    }

    /// <summary>Mail already sitting in the junk folder is junk with nothing having had to score it.</summary>
    [Fact]
    public async Task Admit_AnUnclassifiedOccurrenceInTheJunkFolder_IsWithheld()
    {
        // Arrange
        var gate = Gate(Enabled(Inbox), StubJunkMailFolderCatalog.Naming(new MailFolderIdentity(Primary, Junk)));

        // Act
        var admission = await AdmitAsync(gate, Occurrence(Junk, Now, verdict: null));

        // Assert
        Assert.Equal(DerivedWorkAdmission.WithheldAsJunk, admission);
        Assert.False(admission.PermitsDerivedWork());
    }

    /// <summary>Placement outranks the record, so a message somebody's filter took is withheld whatever a scan concluded.</summary>
    /// <remarks>
    /// The order of the two questions is what this asserts and the only thing that can prove it: with the questions
    /// swapped, every other test here still passes and a message the mail server filed as junk that a scanner scored as
    /// ordinary mail would quietly be chunked, embedded, and offered to the rules.
    /// </remarks>
    [Theory]
    [InlineData(SpamVerdict.NotSpam)]
    [InlineData(SpamVerdict.Undetermined)]
    public async Task Admit_AnOccurrenceInTheJunkFolderScoredAsAnythingElse_IsStillWithheld(SpamVerdict verdict)
    {
        // Arrange
        var gate = Gate(Enabled(Junk), StubJunkMailFolderCatalog.Naming(new MailFolderIdentity(Primary, Junk)));

        // Act
        var admission = await AdmitAsync(gate, Occurrence(Junk, Now, verdict));

        // Assert
        Assert.Equal(DerivedWorkAdmission.WithheldAsJunk, admission);
        Assert.False(admission.PermitsDerivedWork());
    }

    /// <summary>An alias is unique inside an account and nowhere else, so one account's junk folder is not another's.</summary>
    [Fact]
    public async Task Admit_AnotherAccountsFolderOfTheSameName_IsNotWithheldAsJunk()
    {
        // Arrange
        var gate = Gate(Enabled(Junk), StubJunkMailFolderCatalog.Naming(new MailFolderIdentity(Secondary, Junk)));

        // Act
        var admission = await AdmitAsync(gate, Occurrence(Junk, Now, verdict: SpamVerdict.NotSpam));

        // Assert
        Assert.Equal(DerivedWorkAdmission.Admitted, admission);
    }

    /// <summary>Classification is each user's own, so one user's posture never withholds another user's mail.</summary>
    /// <remarks>
    /// The per-account half of the gate, and the one thing that can prove it: with the account guard replaced by the
    /// deployment-wide switch, this message would be withheld as junk on the strength of a verdict recorded before its
    /// user switched classification off, and its chunking, embedding, and rule evaluation would stop for good.
    /// </remarks>
    [Fact]
    public async Task Admit_MailOfAnAccountThatDoesNotClassify_IsAdmittedWhateverWasRecordedAboutIt()
    {
        // Arrange
        var gate = Gate(
            Enabled(Inbox),
            StubJunkMailFolderCatalog.Naming(new MailFolderIdentity(Primary, Junk)),
            Secondary);

        // Act
        var admission = await AdmitAsync(gate, Occurrence(Junk, Now, verdict: SpamVerdict.Spam));

        // Assert
        Assert.Equal(DerivedWorkAdmission.Admitted, admission);
        Assert.True(admission.PermitsDerivedWork());
    }

    /// <summary>A verdict withholds wherever the message sits, which is what an operator who scores without filing gets.</summary>
    [Fact]
    public async Task Admit_AnOccurrenceScoredAsSpamAndNeverFiled_IsWithheld()
    {
        // Arrange
        var gate = Gate(Enabled(Inbox), StubJunkMailFolderCatalog.None);

        // Act
        var admission = await AdmitAsync(gate, Occurrence(Inbox, Now, SpamVerdict.Spam));

        // Assert
        Assert.Equal(DerivedWorkAdmission.WithheldAsJunk, admission);
    }

    [Theory]
    [InlineData(SpamVerdict.NotSpam)]
    [InlineData(SpamVerdict.Undetermined)]
    public async Task Admit_AVerdictThatIsNotSpam_Admits(SpamVerdict verdict)
    {
        // Arrange
        var gate = Gate(Enabled(Inbox), StubJunkMailFolderCatalog.None);

        // Act
        var admission = await AdmitAsync(gate, Occurrence(Inbox, Now, verdict));

        // Assert
        Assert.Equal(DerivedWorkAdmission.Admitted, admission);
    }

    [Fact]
    public async Task Admit_AnOccurrenceInsideTheScopeStillWithinItsWait_IsAwaitingClassification()
    {
        // Arrange
        var gate = Gate(Enabled(Inbox), StubJunkMailFolderCatalog.None);

        // Act
        var admission = await AdmitAsync(gate, Occurrence(Inbox, Now - Wait + TimeSpan.FromSeconds(1), verdict: null));

        // Assert
        Assert.Equal(DerivedWorkAdmission.AwaitingClassification, admission);
        Assert.False(admission.PermitsDerivedWork());
    }

    /// <summary>The failure mode the gate must not have: a wedged scanner delays the index rather than stopping it.</summary>
    [Fact]
    public async Task Admit_AnOccurrenceThatHasWaitedLongerThanTheBound_IsReleased()
    {
        // Arrange
        var gate = Gate(Enabled(Inbox), StubJunkMailFolderCatalog.None);

        // Act
        var admission = await AdmitAsync(gate, Occurrence(Inbox, Now - Wait, verdict: null));

        // Assert
        Assert.Equal(DerivedWorkAdmission.ReleasedAfterWaiting, admission);
        Assert.True(admission.PermitsDerivedWork());
    }

    /// <summary>Waiting and never being classifiable are separate answers, because they have separate remedies.</summary>
    [Fact]
    public async Task Admit_AnOccurrenceWhosePayloadWasNeverStored_IsReleasedAsUnclassifiableWithoutWaiting()
    {
        // Arrange
        var gate = Gate(Enabled(Inbox), StubJunkMailFolderCatalog.None);

        // Act
        var admission = await AdmitAsync(gate, new DerivedWorkCandidate(
            Primary,
            Inbox,
            Now,
            StoredEmailContentAvailability.ExceededSizeLimit,
            Verdict: null));

        // Assert
        Assert.Equal(DerivedWorkAdmission.ReleasedAsUnclassifiable, admission);
        Assert.True(admission.PermitsDerivedWork());
    }

    /// <summary>A payload a later run will fetch is still expected, so the message waits rather than being released now.</summary>
    [Fact]
    public async Task Admit_AnOccurrenceWhosePayloadIsStillComing_KeepsWaiting()
    {
        // Arrange
        var gate = Gate(Enabled(Inbox), StubJunkMailFolderCatalog.None);

        // Act
        var admission = await AdmitAsync(gate, new DerivedWorkCandidate(
            Primary,
            Inbox,
            Now,
            StoredEmailContentAvailability.AwaitingStorageHeadroom,
            Verdict: null));

        // Assert
        Assert.Equal(DerivedWorkAdmission.AwaitingClassification, admission);
    }

    /// <summary>Nothing is ever going to score mail outside the scope, so waiting on a verdict for it would never end.</summary>
    [Fact]
    public async Task Admit_AnUnclassifiedOccurrenceOutsideTheClassifiedScope_IsAdmittedWithoutWaiting()
    {
        // Arrange
        var gate = Gate(Enabled(Inbox), StubJunkMailFolderCatalog.None);

        // Act
        var admission = await AdmitAsync(gate, Occurrence(Archive, Now, verdict: null));

        // Assert
        Assert.Equal(DerivedWorkAdmission.Admitted, admission);
    }

    [Fact]
    public async Task ReadTermsAsync_ClassificationSwitchedOn_MeasuresTheWaitFromTheCurrentInstant()
    {
        // Arrange
        var gate = Gate(Enabled(Inbox), StubJunkMailFolderCatalog.Naming(new MailFolderIdentity(Primary, Junk)));

        // Act
        var terms = await gate.ReadTermsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(terms.IsApplied);
        Assert.Equal(Now - Wait, terms.ReleasedWhenStoredBefore);
        Assert.Equal([new MailFolderIdentity(Primary, Junk)], terms.JunkFolders);
        Assert.True(terms.Classifies(Primary, Inbox));
        Assert.False(terms.Classifies(Primary, Archive));
    }

    [Fact]
    public async Task ReadTermsAsync_ClassificationSwitchedOff_IsNotApplied()
    {
        // Arrange
        var gate = Gate(SpamClassificationSettings.Disabled, StubJunkMailFolderCatalog.None);

        // Act
        var terms = await gate.ReadTermsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(terms.IsApplied);
    }

    /// <summary>The junk folders of an account whose user does not classify are ordinary folders, so the terms never name them.</summary>
    [Fact]
    public async Task ReadTermsAsync_AJunkFolderOfAnAccountThatDoesNotClassify_IsLeftOutOfTheTerms()
    {
        // Arrange
        var gate = Gate(
            Enabled(Inbox),
            StubJunkMailFolderCatalog.Naming(new MailFolderIdentity(Primary, Junk), new MailFolderIdentity(Secondary, Junk)),
            Primary);

        // Act
        var terms = await gate.ReadTermsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([new MailFolderIdentity(Primary, Junk)], terms.JunkFolders);
    }

    [Fact]
    public async Task Admit_NoCandidate_Throws()
    {
        // Arrange
        var terms = await Gate(Enabled(Inbox), StubJunkMailFolderCatalog.None).ReadTermsAsync(TestContext.Current.CancellationToken);

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => DerivedWorkGate.Admit(terms, null!));
    }

    /// <summary>Only the two withholding answers stop derived work, and every reason for releasing a message is one.</summary>
    /// <remarks>
    /// Asserted over the whole enumeration rather than over the answers the tests above happen to produce, so a sixth
    /// answer added later has to state which side of this it falls on instead of silently defaulting to withheld.
    /// </remarks>
    [Theory]
    [InlineData(DerivedWorkAdmission.Admitted, true)]
    [InlineData(DerivedWorkAdmission.WithheldAsJunk, false)]
    [InlineData(DerivedWorkAdmission.AwaitingClassification, false)]
    [InlineData(DerivedWorkAdmission.ReleasedAsUnclassifiable, true)]
    [InlineData(DerivedWorkAdmission.ReleasedAfterWaiting, true)]
    public void PermitsDerivedWork_OneAdmission_AnswersWhetherTheWorkMayRun(
        DerivedWorkAdmission admission,
        bool permitted)
    {
        // Act
        var permitsDerivedWork = admission.PermitsDerivedWork();

        // Assert
        Assert.Equal(permitted, permitsDerivedWork);
    }

    private static SpamClassificationSettings Enabled(params MailFolderAlias[] scope) =>
        SpamClassificationSettings.Create(
            isEnabled: true,
            usesScanner: false,
            scope,
            scannerThreshold: null);

    private static DerivedWorkCandidate Occurrence(
        MailFolderAlias folderAlias,
        DateTimeOffset storedAt,
        SpamVerdict? verdict) =>
        new(Primary, folderAlias, storedAt, StoredEmailContentAvailability.Available, verdict);

    /// <summary>Builds the gate over the accounts whose users classify, which is every account unless a test says otherwise.</summary>
    private static DerivedWorkGate Gate(
        SpamClassificationSettings settings,
        StubJunkMailFolderCatalog junkFolders,
        params MailAccountId[] classifyingAccounts) =>
        new(
            new StubSpamClassificationSettingsReader(
                settings,
                classifyingAccounts.Length > 0 ? classifyingAccounts : [Primary, Secondary]),
            new StubDeploymentMailFolders(StubMailFolderParticipation.Nothing, junkFolders),
            new FakeTimeProvider(Now));

    /// <summary>Decides one occurrence under the terms the gate reads now, which is how a run that meets one message at a time decides it.</summary>
    private static async Task<DerivedWorkAdmission> AdmitAsync(DerivedWorkGate gate, DerivedWorkCandidate candidate) =>
        DerivedWorkGate.Admit(await gate.ReadTermsAsync(TestContext.Current.CancellationToken), candidate);
}
