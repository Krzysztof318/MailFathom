// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.SensitiveContent;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.SensitiveContent;

/// <summary>Covers what one owner may ask for over their own mail, which is the write every other path trusts.</summary>
public sealed class OwnerSensitiveContentRulesTests
{
    private const string Path = "Accounts:0:SensitiveContent";

    private const string AnalyzerAddress = "http://presidio-analyzer:3000";

    /// <summary>The ordinary case, and the one a deployment upgrading into this feature is entirely made of.</summary>
    [Fact]
    public void FindRefusals_ARecordThatSaysNothingAtAll_IsAccepted()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;

        // Act
        var refusals = OwnerSensitiveContentRules.FindRefusals(new OwnerSensitiveContentOptions(), deployment, Path);

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>Tightening is the whole point of the block: a scanner nobody was paying for runs over the mail of whoever asked.</summary>
    [Fact]
    public void FindRefusals_AnOwnerSwitchingOnAScannerTheDeploymentLeftOff_IsAccepted()
    {
        // Arrange
        var owner = new OwnerSensitiveContentOptions();
        owner.Secrets.Enabled = true;

        // Act
        var refusals = OwnerSensitiveContentRules.FindRefusals(owner, new SensitiveContentOptions(), Path);

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>
    /// The obligation belongs to whoever holds the mail, so a record cannot decline a scanner the deployment requires.
    /// The refusal names the deployment setting rather than the owner's own words, which is what makes it actionable to
    /// the operator holding that switch and safe to log.
    /// </summary>
    [Fact]
    public void FindRefusals_AnOwnerSwitchingOffAScannerTheDeploymentRequires_IsRefusedNamingTheSetting()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;
        var owner = new OwnerSensitiveContentOptions();
        owner.Secrets.Enabled = false;

        // Act
        var refusal = Assert.Single(OwnerSensitiveContentRules.FindRefusals(owner, deployment, Path));

        // Assert
        Assert.StartsWith($"{Path}:Secrets:Enabled", refusal, StringComparison.Ordinal);
        Assert.Contains("never off", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// The personal-data scanner reaches an analyzer an operator deploys beside the service. Asking for it where none
    /// was configured is refused at the write, because the alternative is a record that reads as accepted and a mailbox
    /// that fails closed on its next message.
    /// </summary>
    [Fact]
    public void FindRefusals_AnOwnerAskingForThePersonalDataScannerWithNoAnalyzer_IsRefusedNamingTheMissingSetting()
    {
        // Arrange
        var owner = new OwnerSensitiveContentOptions();
        owner.Pii.Enabled = true;

        // Act
        var refusal = Assert.Single(OwnerSensitiveContentRules.FindRefusals(owner, new SensitiveContentOptions(), Path));

        // Assert
        Assert.StartsWith($"{Path}:Pii:Enabled", refusal, StringComparison.Ordinal);
        Assert.Contains("SensitiveContent:PersonalDataAnalyzer:Endpoint", refusal, StringComparison.Ordinal);
    }

    /// <summary>The other half: an analyzer the deployment stood up is one an owner may ask to be scanned by.</summary>
    [Fact]
    public void FindRefusals_AnOwnerAskingForThePersonalDataScannerWhereAnAnalyzerIsConfigured_IsAccepted()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.PersonalDataAnalyzer.Endpoint = AnalyzerAddress;
        var owner = new OwnerSensitiveContentOptions();
        owner.Pii.Enabled = true;

        // Act
        var refusals = OwnerSensitiveContentRules.FindRefusals(owner, deployment, Path);

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>An entry naming no scanner would be dropped in silence and read as a record that screens more than it does.</summary>
    [Fact]
    public void FindRefusals_AnOwnerScreeningForSomethingNoScannerIsCalled_IsRefusedNamingWhatIsAccepted()
    {
        // Arrange
        var owner = new OwnerSensitiveContentOptions { ScreenOutgoingMailFor = ["Sekrety"] };

        // Act
        var refusal = Assert.Single(OwnerSensitiveContentRules.FindRefusals(owner, new SensitiveContentOptions(), Path));

        // Assert
        Assert.DoesNotContain("Sekrety", refusal, StringComparison.Ordinal);
        Assert.Contains("names 1 entry", refusal, StringComparison.Ordinal);
        Assert.Contains("Secrets", refusal, StringComparison.Ordinal);
        Assert.Contains("Pii", refusal, StringComparison.Ordinal);
    }

    /// <summary>The list is the owner's whole answer, so one naming fewer scanners than the deployment stops mail for is a narrowing.</summary>
    [Fact]
    public void FindRefusals_AnOwnerScreeningForFewerScannersThanTheDeployment_IsRefusedNamingWhatIsMissing()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;
        deployment.ScreenOutgoingMailFor = ["Secrets"];
        var owner = new OwnerSensitiveContentOptions { ScreenOutgoingMailFor = [] };

        // Act
        var refusal = Assert.Single(OwnerSensitiveContentRules.FindRefusals(owner, deployment, Path));

        // Assert
        Assert.StartsWith($"{Path}:ScreenOutgoingMailFor", refusal, StringComparison.Ordinal);
        Assert.Contains("Secrets", refusal, StringComparison.Ordinal);
    }

    /// <summary>Naming what the deployment stops mail for and something beside it is an addition, which is allowed.</summary>
    [Fact]
    public void FindRefusals_AnOwnerAddingAScannerToWhatTheDeploymentAlreadyStopsMailFor_IsAccepted()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;
        deployment.PersonalDataAnalyzer.Endpoint = AnalyzerAddress;
        deployment.ScreenOutgoingMailFor = ["Secrets"];
        var owner = new OwnerSensitiveContentOptions { ScreenOutgoingMailFor = ["secrets", "Pii"] };
        owner.Pii.Enabled = true;

        // Act
        var refusals = OwnerSensitiveContentRules.FindRefusals(owner, deployment, Path);

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>Both halves of a record are judged in one pass, so whoever wrote it reads everything wrong with it at once.</summary>
    [Fact]
    public void FindRefusals_ARecordWrongInSeveralWays_ReportsEveryOneOfThem()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;
        deployment.ScreenOutgoingMailFor = ["Secrets"];
        var owner = new OwnerSensitiveContentOptions { ScreenOutgoingMailFor = [] };
        owner.Secrets.Enabled = false;
        owner.Pii.Enabled = true;

        // Act
        var refusals = OwnerSensitiveContentRules.FindRefusals(owner, deployment, Path);

        // Assert
        Assert.Equal(3, refusals.Count);
    }

    [Fact]
    public void FindRefusals_WithoutItsArguments_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => OwnerSensitiveContentRules.FindRefusals(null!, new SensitiveContentOptions(), Path));
        Assert.Throws<ArgumentNullException>(
            () => OwnerSensitiveContentRules.FindRefusals(new OwnerSensitiveContentOptions(), null!, Path));
        Assert.Throws<ArgumentNullException>(
            () => OwnerSensitiveContentRules.FindRefusals(
                new OwnerSensitiveContentOptions(),
                new SensitiveContentOptions(),
                null!));
    }
}
