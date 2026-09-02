// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;
using Xunit;

namespace MailFathom.Application.UnitTests.EmailContent.Rendering.Document;

/// <summary>Covers the closed catalogue of blocks a mail body reduces to, and that the wire and the code agree on it.</summary>
/// <remarks>
/// The identity is what a client keys its renderers by and the version is what lets it refuse one block instead of the
/// whole message, so both are contract rather than implementation: neither moves without the change being visible here.
/// </remarks>
public sealed class MailDocumentBlockTypeTests
{
    /// <summary>Eight is the catalogue, and a ninth is a decision rather than an addition somebody made in passing.</summary>
    [Fact]
    public void All_TheCatalogue_HoldsTheEightDeclaredTypes()
    {
        // Act, Assert
        Assert.Equal(8, MailDocumentBlockType.All.Count);
    }

    [Fact]
    public void All_TheCatalogue_AllocatesEachIdentityOnce()
    {
        // Act
        var identities = MailDocumentBlockType.All.Select(blockType => blockType.Identity).ToArray();

        // Assert
        Assert.Equal(identities.Length, identities.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>An identity is written into a document and read back out of one, so it carries no case and no spacing.</summary>
    [Fact]
    public void All_TheCatalogue_WritesEveryIdentityInLowerCaseLetters()
    {
        // Act, Assert
        Assert.All(MailDocumentBlockType.All, blockType => Assert.Matches("^[a-z]+$", blockType.Identity));
    }

    /// <summary>A version below one would say a block was written against no revision of its own contract.</summary>
    [Fact]
    public void All_TheCatalogue_GivesEveryTypeAVersion()
    {
        // Act, Assert
        Assert.All(MailDocumentBlockType.All, blockType => Assert.True(blockType.Version >= 1));
    }

    /// <summary>The one thing that would break a client silently: a block whose type stopped naming what its discriminator writes.</summary>
    [Fact]
    public void Type_EveryDeclaredBlock_CarriesTheIdentityItsDiscriminatorWrites()
    {
        // Arrange
        var discriminators = typeof(MailDocumentBlock)
            .GetCustomAttributes<JsonDerivedTypeAttribute>()
            .ToDictionary(derived => derived.DerivedType, derived => derived.TypeDiscriminator);

        // Act
        var declared = EveryBlock()
            .Select(block => (block.Type.Identity, Discriminator: discriminators[block.GetType()]))
            .ToArray();

        // Assert
        Assert.Equal(MailDocumentBlockType.All.Count, declared.Length);
        Assert.All(declared, pair => Assert.Equal(pair.Identity, pair.Discriminator));
    }

    /// <summary>A block cannot claim a revision it did not write, because it does not carry one of its own.</summary>
    [Fact]
    public void Version_EveryDeclaredBlock_ReportsTheCataloguesVersionForItsType()
    {
        // Act, Assert
        Assert.All(EveryBlock(), block => Assert.Equal(block.Type.Version, block.Version));
    }

    /// <summary>Reading a newer revision as this one would drop what it added and draw the block as though nothing were missing.</summary>
    [Fact]
    public void Version_ADocumentClaimingARevisionThisBuildDoesNotImplement_IsRefused()
    {
        // Arrange
        var block = new MailParagraphBlock([Run("Words")], MailBlockAlignment.Inherited);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => block with { Version = block.Version + 1 });
    }

    [Fact]
    public void TryParse_ADeclaredIdentity_ReturnsTheCataloguedType()
    {
        // Act
        var parsed = MailDocumentBlockType.TryParse(MailDocumentBlockType.QuoteIdentity, out var blockType);

        // Assert
        Assert.True(parsed);
        Assert.Equal(MailDocumentBlockType.Quote, blockType);
    }

    /// <summary>An identity nothing declares is a service ahead of this build, not a member to reconstruct.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("figure")]
    [InlineData("Paragraph")]
    public void TryParse_AnIdentityTheCatalogueDoesNotHold_ReturnsTheUnspecifiedDefault(string? identity)
    {
        // Act
        var parsed = MailDocumentBlockType.TryParse(identity, out var blockType);

        // Assert
        Assert.False(parsed);
        Assert.False(blockType.IsSpecified);
    }

    [Fact]
    public void IsSpecified_TheStructDefault_ReportsItselfUnspecified()
    {
        // Arrange
        MailDocumentBlockType unspecified = default;

        // Act, Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Equal("(unspecified)", unspecified.ToString());
        Assert.Throws<InvalidOperationException>(() => unspecified.Identity);
    }

    [Fact]
    public void ToString_ACataloguedType_NamesTheIdentityAndTheVersion()
    {
        // Act, Assert
        Assert.Equal("table v1", MailDocumentBlockType.Table.ToString());
    }

    [Fact]
    public void Serialization_ACataloguedType_RoundTripsAsItsIdentity()
    {
        // Act
        var json = JsonSerializer.Serialize(MailDocumentBlockType.Preformatted);
        var read = JsonSerializer.Deserialize<MailDocumentBlockType>(json);

        // Assert
        Assert.Equal("\"preformatted\"", json);
        Assert.Equal(MailDocumentBlockType.Preformatted, read);
    }

    [Theory]
    [InlineData("\"figure\"")]
    [InlineData("7")]
    public void Deserialization_AValueTheCatalogueDoesNotHold_IsRefused(string json)
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MailDocumentBlockType>(json));
    }

    [Fact]
    public void Serialization_TheStructDefault_IsRefused()
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize<MailDocumentBlockType>(default));
    }

    /// <summary>One block of every catalogued kind, which is what makes the two assertions over them cover the whole set.</summary>
    private static IReadOnlyList<MailDocumentBlock> EveryBlock() =>
    [
        new MailParagraphBlock([Run("Words")], MailBlockAlignment.Inherited),
        new MailHeadingBlock(2, [Run("A heading")], MailBlockAlignment.Inherited),
        new MailListBlock(
            ordered: false,
            [new MailListItem([new MailParagraphBlock([Run("In a list")], MailBlockAlignment.Inherited)])]),
        new MailTableBlock(
            [new MailTableColumn(WidthShare: null)],
            [
                new MailTableRow(
                    IsHeader: false,
                    [
                        new MailTableCell(
                            1,
                            1,
                            MailBlockAlignment.Inherited,
                            Background: null,
                            [new MailParagraphBlock([Run("In a cell")], MailBlockAlignment.Inherited)]),
                    ]),
            ]),
        new MailQuoteBlock(1, [new MailParagraphBlock([Run("In a quote")], MailBlockAlignment.Inherited)]),
        new MailImageBlock(
            new MailInlineImage("data:image/png;base64,AAAA", "The picture", Width: null, Height: null),
            link: null,
            MailBlockAlignment.Inherited),
        new MailSeparatorBlock(),
        new MailPreformattedBlock("  preformatted  "),
    ];

    private static MailInlineRun Run(string text) => new(text, MailTextEmphasis.None, Foreground: null, Link: null);
}
