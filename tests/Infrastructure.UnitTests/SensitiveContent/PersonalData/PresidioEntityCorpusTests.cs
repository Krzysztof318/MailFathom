// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Infrastructure.SensitiveContent.PersonalData;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.SensitiveContent.PersonalData;

/// <summary>Covers which entities a plan actually asks the analyzer about.</summary>
/// <remarks>
/// One answer serves the scanner and the startup probe, so this is where a configured category list and a suppression stop
/// being configuration and become a request. Getting it wrong is silent in both directions: too few entities is protection
/// nobody notices is missing, and too many is a redaction of something the operator deliberately left out.
/// </remarks>
public sealed class PresidioEntityCorpusTests
{
    [Fact]
    public void RequestedRules_DefaultCategories_AskForEveryEntityInsideThem()
    {
        // Arrange
        var expected = PersonalDataScanningPlans.DefaultCategories()
            .SelectMany(PresidioEntityCorpus.RulesOf)
            .Select(rule => rule.Name)
            .Order(StringComparer.Ordinal);

        // Act
        var requested = PresidioEntityCorpus.RequestedRules(PersonalDataScanningPlans.Default);

        // Assert
        Assert.Equal(expected, requested.Keys.Order(StringComparer.Ordinal));
    }

    /// <summary>A category left out of a configured list is asked about at all, which is what makes it redact nothing.</summary>
    [Fact]
    public void RequestedRules_CategoryLeftOutOfTheConfiguredList_AsksForNoneOfItsEntities()
    {
        // Arrange
        var plan = PersonalDataScanningPlans.For([PersonalDataScanningPlans.Category("PaymentCard")]);

        // Act
        var requested = PresidioEntityCorpus.RequestedRules(plan);

        // Assert
        Assert.Equal(["CREDIT_CARD"], requested.Keys);
    }

    /// <summary>A suppressed entity is left out of the request rather than filtered out of the answer.</summary>
    [Fact]
    public void RequestedRules_SuppressedRule_IsNotAskedAbout()
    {
        // Arrange
        var plan = PersonalDataScanningPlans.For(
            [PersonalDataScanningPlans.Category("BankAccount")],
            [PersonalDataScanningPlans.Rule("BankAccount", "US_BANK_NUMBER")]);

        // Act
        var requested = PresidioEntityCorpus.RequestedRules(plan);

        // Assert
        Assert.Equal(["IBAN_CODE"], requested.Keys);
    }

    /// <summary>
    /// A plan without this scanner is refused rather than answered with an empty request. An empty entity list asks the
    /// analyzer for everything it can recognise, so the failure mode of guessing here is a scanner that redacts categories
    /// nobody switched on.
    /// </summary>
    [Fact]
    public void RequestedRules_PlanThatDoesNotSwitchTheScannerOn_IsRefused()
    {
        // Arrange
        var secretsOnly = SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Default,
            [
                SensitiveContentScannerPlan.Create(
                    SensitiveContentScannerKind.Secrets,
                    [SensitiveContentCategory.Create("ProviderToken")],
                    []),
            ]);

        // Act
        var failure = Assert.Throws<ArgumentException>(() => PresidioEntityCorpus.RequestedRules(secretsOnly));

        // Assert
        Assert.Contains("does not switch it on", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Every rule the catalog publishes comes from the mapping, so neither can name an entity the other does not.</summary>
    [Fact]
    public void RulesOf_EveryDeclaredCategory_MatchesWhatTheCatalogPublishes()
    {
        // Arrange
        var catalog = new PersonalDataContentCatalog();

        // Act
        var disagreements = catalog.Categories
            .Where(definition => !definition.Rules.SequenceEqual(PresidioEntityCorpus.RulesOf(definition.Category)));

        // Assert
        Assert.Empty(disagreements);
    }
}
