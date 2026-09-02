// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Infrastructure.SensitiveContent.PersonalData;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.SensitiveContent.PersonalData;

/// <summary>Covers what the personal-data scanner declares, which is what an operator's configuration is judged against.</summary>
/// <remarks>
/// The default set is the product's opinion about what is worth hiding wherever it appears, and it is the one part of this
/// feature an operator inherits without writing anything. A category that quietly joined or left it would change what every
/// deployment stores and hands out, and nothing else in the system would report the change.
/// </remarks>
public sealed class PersonalDataContentCatalogTests
{
    private static readonly string[] ExpectedDefaultCategories =
    [
        "PaymentCard",
        "BankAccount",
        "NationalIdentifier",
        "IdentityDocument",
        "HealthIdentifier",
    ];

    private static readonly string[] ExpectedOptionalCategories =
    [
        "PersonName",
        "EmailAddress",
        "PostalAddress",
        "PhoneNumber",
        "Date",
        "NetworkAddress",
    ];

    [Fact]
    public void Scanner_IsThePersonalDataSwitch()
    {
        // Arrange
        var catalog = new PersonalDataContentCatalog();

        // Act
        var scanner = catalog.Scanner;

        // Assert
        Assert.Equal(SensitiveContentScannerKind.Pii, scanner);
    }

    /// <summary>The strong identifiers, and only those, are on for a deployment that names no categories.</summary>
    [Fact]
    public void Categories_DetectedByDefault_AreTheStrongIdentifiersAlone()
    {
        // Arrange
        var catalog = new PersonalDataContentCatalog();

        // Act
        var detectedByDefault = catalog.Categories
            .Where(definition => definition.DetectedByDefault)
            .Select(definition => definition.Category.Name);

        // Assert
        Assert.Equal(ExpectedDefaultCategories, detectedByDefault);
    }

    /// <summary>Everything a mailbox is made of stays off until an operator names it, because naming it empties a search.</summary>
    [Fact]
    public void Categories_WhatAMailboxIsMadeOf_IsOffUntilConfigured()
    {
        // Arrange
        var catalog = new PersonalDataContentCatalog();

        // Act
        var optional = catalog.Categories
            .Where(definition => !definition.DetectedByDefault)
            .Select(definition => definition.Category.Name);

        // Assert
        Assert.Equal(ExpectedOptionalCategories, optional);
    }

    /// <summary>A category with no rule could never match, and a suppression could never name anything inside it.</summary>
    [Fact]
    public void Categories_EveryDeclaredCategory_HoldsAtLeastOneAnalyzerEntity()
    {
        // Arrange
        var catalog = new PersonalDataContentCatalog();

        // Act
        var empty = catalog.Categories.Where(definition => definition.Rules.Count == 0);

        // Assert
        Assert.Empty(empty);
    }

    /// <summary>
    /// A rule is spelled exactly as the analyzer spells the entity, because the same list is both what the request asks for
    /// and what the answer is mapped back through. A rule name invented here would ask for an entity nothing recognises and
    /// find nothing, which reads identically to a clean mailbox.
    /// </summary>
    [Fact]
    public void Categories_EveryRule_IsSpelledAsAnAnalyzerEntityName()
    {
        // Arrange
        var catalog = new PersonalDataContentCatalog();

        // Act
        var misspelled = catalog.Categories
            .SelectMany(definition => definition.Rules)
            .Where(rule => !rule.Name.All(character => char.IsAsciiLetterUpper(character) || character == '_'));

        // Assert
        Assert.Empty(misspelled);
    }

    /// <summary>
    /// One entity belongs to one category. Two categories claiming it would make the placeholder a reader sees depend on
    /// which category the mapping happened to list first, and a suppression naming it silence only one of the two.
    /// </summary>
    [Fact]
    public void Categories_NoAnalyzerEntity_IsClaimedByTwoCategories()
    {
        // Arrange
        var catalog = new PersonalDataContentCatalog();

        // Act
        var shared = catalog.Categories
            .SelectMany(definition => definition.Rules)
            .GroupBy(rule => rule.Name, StringComparer.Ordinal)
            .Where(sameEntity => sameEntity.Count() > 1)
            .Select(sameEntity => sameEntity.Key);

        // Assert
        Assert.Empty(shared);
    }

    /// <summary>The revision travels with every finding, so a mapping change nobody recorded makes two results indistinguishable.</summary>
    [Fact]
    public void Detector_NamesTheMappingRevisionTheLanguageAndTheFloor()
    {
        // Arrange
        var profile = PersonalDataScanningPlans.Profile;

        // Act
        var detector = profile.Detector;

        // Assert
        Assert.Equal("mailfathom-personal-data", detector.Name);
        Assert.Equal(
            $"presidio+entities.{PresidioEntityCorpus.MappingRevision}+lang.en+floor.0.42",
            detector.Revision);
    }
}
