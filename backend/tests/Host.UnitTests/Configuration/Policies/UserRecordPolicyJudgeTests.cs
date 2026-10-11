// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.TestSupport;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Policies;

public sealed class UserRecordPolicyJudgeTests
{
    private const string DefaultingPolish = """{"Users":{"Defaults":{"Language":"Polish"}}}""";
    private const string ForcingOffMcp = """{"Users":{"Forced":{"EndpointAccess":{"McpEndpoint":false}}}}""";
    private const string NotAPolicy = """{"Users":{"Forced":{"Dialect":"Silesian"}}}""";

    /// <summary>
    /// What a policy write is refused over is a record it breaks: a record relying on a default for the language a
    /// record must name is left without one the moment the default goes.
    /// </summary>
    [Fact]
    public void Judge_APolicyThatTakesAwayTheLanguageARecordReliedOn_RefusesNamingTheLanguage()
    {
        // Act
        var judgement = Judge(
            "{}",
            StatedPolicies.Governing(organization: DefaultingPolish),
            StatedPolicies.Governing(organization: "{}"));

        // Assert
        Assert.StartsWith("Language is not stated", Assert.Single(judgement.Refusals), StringComparison.Ordinal);
    }

    /// <summary>
    /// A record that was unservable before the write refuses nothing, because the write did not break it and its
    /// author could not act on a refusal about a row somebody else has yet to correct.
    /// </summary>
    [Fact]
    public void Judge_ARecordThatWasAlreadyUnservable_RefusesNothing()
    {
        // Act
        var judgement = Judge(
            "{}",
            StatedPolicies.Governing(),
            StatedPolicies.Governing(deployment: ForcingOffMcp));

        // Assert
        Assert.Empty(judgement.Refusals);
    }

    [Fact]
    public void Judge_APolicyThatLeavesTheRecordServable_RefusesNothing()
    {
        // Act
        var judgement = Judge(
            """{"Language":"English"}""",
            StatedPolicies.Governing(organization: DefaultingPolish),
            StatedPolicies.Governing(organization: "{}"));

        // Assert
        Assert.Empty(judgement.Refusals);
    }

    /// <summary>The switches answered are the ones the candidate policies resolve, which is what the user's row is rewritten to.</summary>
    [Fact]
    public void Judge_APolicyForcingASwitch_AnswersTheSwitchesTheRecordResolvesToUnderIt()
    {
        // Act
        var judgement = Judge(
            """{"Language":"English","EndpointAccess":{"McpEndpoint":true}}""",
            StatedPolicies.Governing(),
            StatedPolicies.Governing(deployment: ForcingOffMcp));

        // Assert
        Assert.Equal(new UserEndpointAccess(McpEndpoint: false, ClientEndpoint: true), judgement.EndpointAccess);
    }

    /// <summary>
    /// A policy or a record this build does not read is one it cannot resolve, so the row keeps the switches it has
    /// rather than being moved on no reading at all.
    /// </summary>
    [Theory]
    [InlineData("""{"Language":"English"}""", NotAPolicy)]
    [InlineData(null, ForcingOffMcp)]
    public void Judge_ARecordOrAPolicyThisBuildDoesNotRead_LeavesTheRowAsItStandsAndRefusesNothing(
        string? record,
        string candidatePolicy)
    {
        // Act
        var judgement = Judge(record, StatedPolicies.Governing(), StatedPolicies.Governing(deployment: candidatePolicy));

        // Assert
        Assert.Null(judgement.EndpointAccess);
        Assert.Empty(judgement.Refusals);
    }

    [Fact]
    public void Judge_SeveralRecords_AnswersOneJudgementEachInTheOrderGiven()
    {
        // Arrange
        var candidate = StatedPolicies.Governing(deployment: ForcingOffMcp);
        GovernedUserRecord[] records =
        [
            new(SyntheticUser.Deployment, "alex", """{"Language":"English"}""", StatedPolicies.Governing(), candidate),
            new(SyntheticUser.Deployment, "sam", null, StatedPolicies.Governing(), candidate),
        ];

        // Act
        var judgements = NewJudge().Judge(records);

        // Assert
        Assert.Equal(
            [new UserEndpointAccess(McpEndpoint: false, ClientEndpoint: true), null],
            judgements.Select(judgement => judgement.EndpointAccess));
    }

    private static UserRecordPolicyJudgement Judge(
        string? record,
        GoverningSettingsPolicies standing,
        GoverningSettingsPolicies candidate) =>
        Assert.Single(NewJudge().Judge([new GovernedUserRecord(SyntheticUser.Deployment, "alex", record, standing, candidate)]));

    private static UserRecordPolicyJudge NewJudge() => new(new UserAccountDocumentBinder(
        new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
        new FakeTimeProvider(),
        Options.Create(new SensitiveContentOptions())));
}
