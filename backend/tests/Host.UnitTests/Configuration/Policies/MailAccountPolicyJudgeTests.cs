// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Synchronization;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.Infrastructure.Persistence.Policies.MailAccounts;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.TestSupport;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Policies;

public sealed class MailAccountPolicyJudgeTests
{
    private const string ForcingPush = """{"MailAccounts":{"Forced":{"Mode":"Push"}}}""";
    private const string DefaultingTheHost = """{"MailAccounts":{"Defaults":{"Host":"mail.example.org"}}}""";
    private const string DefaultingPolish = """{"MailAccounts":{"Defaults":{"Language":"Polish"}}}""";
    private const string NotAPolicy = """{"MailAccounts":{"Forced":{"Dialect":"Silesian"}}}""";

    /// <summary>
    /// A write that states a default binds no account stating that setting itself, because such an account is served
    /// exactly what it was: nothing is refused over it and its columns are left alone.
    /// </summary>
    [Fact]
    public void Judge_APolicyThatLeavesTheAccountServedWhatItWas_RefusesNothingAndMovesNoColumn()
    {
        // Act
        var judgement = Judge(Mailbox(), StatedPolicies.Governing(), StatedPolicies.Governing(deployment: DefaultingTheHost));

        // Assert
        Assert.Empty(judgement.Refusals);
        Assert.Null(judgement.Settings);
    }

    /// <summary>What a policy write is refused over is an account it breaks: one relying on a default for the language a mailbox must state is left without one the moment the default goes.</summary>
    [Fact]
    public void Judge_APolicyThatTakesAwayTheLanguageAnAccountReliedOn_RefusesNamingTheLanguage()
    {
        // Act
        var judgement = Judge(
            Mailbox(language: null),
            StatedPolicies.Governing(organization: DefaultingPolish),
            StatedPolicies.Governing(organization: "{}"));

        // Assert
        Assert.Contains("Language", Assert.Single(judgement.Refusals), StringComparison.Ordinal);
        Assert.Null(judgement.Settings);
    }

    /// <summary>
    /// An account that was unservable before the write refuses nothing, because the write did not break it and its
    /// author could not act on a refusal about a row somebody else has yet to correct. Its columns still follow.
    /// </summary>
    [Fact]
    public void Judge_AnAccountThatWasAlreadyUnservable_RefusesNothingAndAnswersTheColumnsItResolvesTo()
    {
        // Act
        var judgement = Judge(
            Mailbox(language: null),
            StatedPolicies.Governing(),
            StatedPolicies.Governing(deployment: ForcingPush));

        // Assert
        Assert.Empty(judgement.Refusals);
        Assert.Equal(MailSynchronizationMode.Push, judgement.Settings?.SynchronizationMode);
    }

    /// <summary>A candidate policy this build does not read resolves nothing, so a grant held at the deployment is refused nothing over it and there is nothing to derive a column from.</summary>
    [Fact]
    public void Judge_ACandidatePolicyThisBuildDoesNotRead_RefusesNothingAndMovesNoColumn()
    {
        // Act
        var judgement = Judge(Mailbox(), StatedPolicies.Governing(), StatedPolicies.Governing(deployment: NotAPolicy));

        // Assert
        Assert.Empty(judgement.Refusals);
        Assert.Null(judgement.Settings);
    }

    /// <summary>
    /// Under a grant held below the deployment, a candidate policy this build does not read is not the benefit of the
    /// doubt: what the write would move cannot be told, a replica that reads the policy would serve it, and so an
    /// account naming a secret that is not its users' refuses the write while one naming only its user's does not.
    /// </summary>
    [Theory]
    [InlineData("file:/run/secrets/shared-password", true)]
    [InlineData(null, false)]
    public void Judge_ACandidatePolicyThisBuildDoesNotReadUnderANarrowerGrant_IsRefusedOverAnAccountNamingASecretNotItsUsers(
        string? secretReference,
        bool refused)
    {
        // Arrange
        var account = Mailbox(
            secretReference: secretReference ?? $"file:/run/secrets/user-{SyntheticUser.Deployment.Value:D}-work");

        // Act
        var judgement = Judge(
            account,
            StatedPolicies.Governing(),
            StatedPolicies.Governing(deployment: NotAPolicy, organization: ForcingPush),
            underANarrowerGrant: true);

        // Assert
        Assert.Equal(refused, judgement.Refusals.Count > 0);
        Assert.Null(judgement.Settings);
    }

    /// <summary>
    /// What an account was refused under a standing policy this build does not read is unknown, so everything the
    /// candidate leaves wrong counts as the write's own.
    /// </summary>
    [Fact]
    public void Judge_AStandingPolicyThisBuildDoesNotRead_RefusesOverEverythingTheCandidateLeavesWrong()
    {
        // Act
        var judgement = Judge(
            Mailbox(language: null),
            StatedPolicies.Governing(deployment: NotAPolicy),
            StatedPolicies.Governing());

        // Assert
        Assert.Contains("Language", Assert.Single(judgement.Refusals), StringComparison.Ordinal);
    }

    /// <summary>
    /// A grant held below the deployment may not move the effective settings of an account naming a secret that was
    /// not provisioned for one of its users, and the grant held over the deployment is not asked.
    /// </summary>
    [Theory]
    [InlineData(true, "file:/run/secrets/shared-password", true)]
    [InlineData(false, "file:/run/secrets/shared-password", false)]
    [InlineData(true, null, false)]
    public void Judge_APolicyMovingAnAccountsEffectiveSettings_IsRefusedOnlyUnderANarrowerGrantOverASecretNotItsUsers(
        bool underANarrowerGrant,
        string? secretReference,
        bool refused)
    {
        // Arrange
        var account = Mailbox(
            secretReference: secretReference ?? $"file:/run/secrets/user-{SyntheticUser.Deployment.Value:D}-work");

        // Act
        var judgement = Judge(
            account,
            StatedPolicies.Governing(),
            StatedPolicies.Governing(deployment: ForcingPush),
            underANarrowerGrant);

        // Assert
        Assert.Equal(refused, judgement.Refusals.Count > 0);
        Assert.Equal(refused ? null : MailSynchronizationMode.Push, judgement.Settings?.SynchronizationMode);
    }

    private static MailAccountPolicyJudgement Judge(
        MailAccountRecord account,
        GoverningSettingsPolicies standing,
        GoverningSettingsPolicies candidate,
        bool underANarrowerGrant = false) =>
        Assert.Single(NewJudge().Judge(
            [new GovernedMailAccount(account, [SyntheticUser.Deployment], standing, candidate)],
            underANarrowerGrant));

    private static MailAccountPolicyJudge NewJudge() => new(new UserAccountDocumentBinder(
        new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
        new FakeTimeProvider(),
        Options.Create(new SensitiveContentOptions())));

    private static MailAccountRecord Mailbox(
        string? language = "English",
        string secretReference = "file:/run/secrets/work-password")
    {
        var statedLanguage = language is null ? string.Empty : $"\"Language\": \"{language}\",";

        return new MailAccountRecord(
            Guid.NewGuid(),
            "work@example.test",
            "work",
            $$"""
              {
                {{statedLanguage}}
                "Host": "imap.example.test",
                "UserName": "work@example.test",
                "Secrets": { "Password": { "Name": "work-password", "SecretReference": "{{secretReference}}" } }
              }
              """,
            Version: 1);
    }
}
