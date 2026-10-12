// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.Infrastructure.Persistence.Policies.MailAccounts;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Policies.MailAccounts;

public sealed class GovernedMailAccountJudgementsTests
{
    private static readonly GoverningSettingsPolicies Standing = GoverningSettingsPolicies.Unwritten;

    private static readonly GoverningSettingsPolicies Candidate = new(
        new SettingsPolicyDocument(OrganizationId: null, """{"MailAccounts":{"Forced":{"Mode":"Push"}}}""", Version: 1),
        Organization: null);

    /// <summary>The judge is asked about the account as its row holds it, the users it reaches, and both readings of the policies.</summary>
    [Fact]
    public void GovernedOf_ARowWhoseSettingsWereRead_ComposesTheAccountItsUsersAndBothReadingsOfThePolicies()
    {
        // Arrange
        var row = Row(1);

        // Act
        var governed = GovernedMailAccountJudgements.GovernedOf(row, Standing, Candidate);

        // Assert
        Assert.Equal(row.Id, governed.Account.Id);
        Assert.Equal("account-1@example.test", governed.Account.EmailAddress);
        Assert.Equal("account 1", governed.Account.DisplayName);
        Assert.Equal("{}", governed.Account.Document);
        Assert.Equal(4, governed.Account.Version);
        Assert.Equal([UserId.Create(row.Users[0])], governed.Users);
        Assert.Same(Standing, governed.Standing);
        Assert.Same(Candidate, governed.Candidate);
    }

    /// <summary>Settings past the bound are not sent, so such a row is one the caller leaves out rather than one judged on nothing.</summary>
    [Fact]
    public void GovernedOf_ARowWhoseSettingsWereNotRead_IsRefused()
    {
        // Arrange
        var row = Row(1);
        row.Document = null;

        // Act
        var refusal = Record.Exception(() => GovernedMailAccountJudgements.GovernedOf(row, Standing, Candidate));

        // Assert
        Assert.IsType<ArgumentException>(refusal);
    }

    /// <summary>An account in the way is named by the label this deployment tells it apart by, as a mail account, with what refuses it.</summary>
    [Fact]
    public void FindAccountsInTheWay_AnAccountTheWriteMayNotReach_IsNamedByItsLabelAsAMailAccount()
    {
        // Arrange
        StoredGovernedMailAccountRow[] accounts = [Row(1), Row(2)];
        MailAccountPolicyJudgement[] judgements = [new(Settings: null, []), new(Settings: null, ["Language is not stated."])];

        // Act
        var named = Assert.Single(GovernedMailAccountJudgements.FindAccountsInTheWay(accounts, judgements));

        // Assert
        Assert.Equal(accounts[1].Id, named.Identity);
        Assert.Equal("account 2", named.DisplayName);
        Assert.Equal(SettingsPolicyRecordKind.MailAccount, named.Kind);
        Assert.Equal(["Language is not stated."], named.Corrections);
    }

    /// <summary>The rows a write rewrites are the ones a judgement answers settings for, each at the version its row was read at.</summary>
    [Fact]
    public void FindMovedSettings_AccountsWhoseSettingsTheWriteMoves_AreAnsweredWithTheVersionTheirRowStandsAt()
    {
        // Arrange
        StoredGovernedMailAccountRow[] accounts = [Row(1), Row(2)];
        MailAccountPolicyJudgement[] judgements =
        [
            new(Settings: null, []),
            new(MailAccountQueryableSettings.Unreadable, []),
        ];

        // Act
        var moved = Assert.Single(GovernedMailAccountJudgements.FindMovedSettings(accounts, judgements));

        // Assert
        Assert.Equal(accounts[1].Id, moved.Id);
        Assert.Equal(4, moved.Version);
        Assert.Same(MailAccountQueryableSettings.Unreadable, moved.Settings);
    }

    /// <summary>A judge that answered for fewer accounts than it was handed would leave some unjudged, which a write never is.</summary>
    [Fact]
    public void FindAccountsInTheWay_FewerJudgementsThanAccounts_IsRefusedRatherThanReadAsNoRefusal()
    {
        // Arrange
        StoredGovernedMailAccountRow[] accounts = [Row(1), Row(2)];
        MailAccountPolicyJudgement[] judgements = [new(Settings: null, [])];

        // Act
        var refusal = Record.Exception(() => GovernedMailAccountJudgements.FindAccountsInTheWay(accounts, judgements));

        // Assert
        Assert.IsType<ArgumentException>(refusal);
    }

    /// <summary>One refusal counts the users and the mail accounts in the way together, and names the first of them only.</summary>
    [Fact]
    public void RefusedOver_MoreRecordsInTheWayThanOneAnswerNames_NamesTheFirstAndCountsThemAll()
    {
        // Arrange
        var refusing = SettingsPolicyCommit.MaximumRecordsNamed + 2;
        SettingsPolicyRecordInTheWay[] inTheWay =
        [
            .. Enumerable.Range(1, refusing).Select(number => new SettingsPolicyRecordInTheWay(Row(number).Id, $"account {number}", ["Refused."])),
        ];

        // Act
        var refusal = SettingsPolicyCommit.RefusedOver(governed: refusing + 5, inTheWay);
        var none = SettingsPolicyCommit.RefusedOver(governed: 3, []);

        // Assert
        Assert.NotNull(refusal);
        Assert.Equal(SettingsPolicyCommitResult.RecordsLeftInvalid, refusal.Result);
        Assert.Equal(refusing + 5, refusal.GovernedRecords);
        Assert.Equal(refusing, refusal.RecordsLeftInvalid);
        Assert.Equal(inTheWay.Take(SettingsPolicyCommit.MaximumRecordsNamed), refusal.RecordsInTheWay);
        Assert.Null(none);
    }

    private static StoredGovernedMailAccountRow Row(int number) => new()
    {
        Id = new Guid($"0199a0c0-0000-7000-8000-{number:D12}"),
        EmailAddress = $"account-{number}@example.test",
        DisplayName = $"account {number}",
        Document = "{}",
        Version = 4,
        Users = [new Guid($"0197a3c0-0000-7000-8000-{number:D12}")],
    };
}
