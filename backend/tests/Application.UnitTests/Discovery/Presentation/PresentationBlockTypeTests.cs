// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MailFathom.Application.Discovery.Presentation;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Presentation;

/// <summary>Covers the closed catalogue of block types, and that the wire and the code agree about it.</summary>
public sealed class PresentationBlockTypeTests
{
    /// <summary>Nine is the catalogue, and a tenth is a decision rather than an addition somebody made in passing.</summary>
    [Fact]
    public void All_TheCatalogue_HoldsTheNineDeclaredTypes()
    {
        // Act, Assert
        Assert.Equal(9, PresentationBlockType.All.Count);
    }

    [Fact]
    public void All_TheCatalogue_AllocatesEachIdentityOnce()
    {
        // Act
        var identities = PresentationBlockType.All.Select(blockType => blockType.Identity).ToArray();

        // Assert
        Assert.Equal(identities.Length, identities.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>A version below one would say a block was written against no revision of its own contract.</summary>
    [Fact]
    public void All_TheCatalogue_GivesEveryTypeAVersion()
    {
        // Act, Assert
        Assert.All(PresentationBlockType.All, blockType => Assert.True(blockType.Version >= 1));
    }

    /// <summary>The one thing that would break a client silently: a discriminator that stopped naming the type it stands for.</summary>
    [Fact]
    public void Type_EveryDeclaredBlock_CarriesTheIdentityItsDiscriminatorWrites()
    {
        // Arrange
        var discriminators = typeof(PresentationBlock)
            .GetCustomAttributes<JsonDerivedTypeAttribute>()
            .ToDictionary(derived => derived.DerivedType, derived => derived.TypeDiscriminator);

        // Act
        var declared = PresentationPlanExample.EveryBlock()
            .Select(block => (block.Type.Identity, Discriminator: discriminators[block.GetType()]))
            .ToArray();

        // Assert
        Assert.Equal(PresentationBlockType.All.Count, declared.Length);
        Assert.All(declared, pair => Assert.Equal(pair.Identity, pair.Discriminator));
    }

    /// <summary>A block cannot claim a revision it did not write, because it does not carry one of its own.</summary>
    [Fact]
    public void Version_EveryDeclaredBlock_ReportsTheCataloguesVersionForItsType()
    {
        // Act, Assert
        Assert.All(
            PresentationPlanExample.EveryBlock(),
            block => Assert.Equal(block.Type.Version, block.Version));
    }

    /// <summary>
    /// What makes the catalogue closed rather than conventionally closed: no constructor outside this assembly brings a
    /// block into being from data. The one reachable constructor is the copy constructor every non-sealed record has,
    /// which C# requires to be protected and which can only copy a block this assembly already composed.
    /// </summary>
    [Fact]
    public void PresentationBlock_TheHierarchy_ExposesNothingButTheRecordCopyConstructor()
    {
        // Act
        var reachable = typeof(PresentationBlock)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(constructor => constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly)
            .Select(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray())
            .ToArray();

        // Assert
        Assert.Equal([[typeof(PresentationBlock)]], reachable);
    }


    [Fact]
    public void TryParse_ADeclaredIdentity_ReturnsTheCataloguedType()
    {
        // Act
        var parsed = PresentationBlockType.TryParse(PresentationBlockType.FactTableIdentity, out var blockType);

        // Assert
        Assert.True(parsed);
        Assert.Equal(PresentationBlockType.FactTable, blockType);
    }

    /// <summary>An identity nothing declares is a service ahead of this build, not a member to reconstruct.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("chart")]
    [InlineData("FactTable")]
    public void TryParse_AnIdentityTheCatalogueDoesNotHold_ReturnsTheUnspecifiedDefault(string? identity)
    {
        // Act
        var parsed = PresentationBlockType.TryParse(identity, out var blockType);

        // Assert
        Assert.False(parsed);
        Assert.False(blockType.IsSpecified);
    }

    [Fact]
    public void IsSpecified_TheStructDefault_ReportsItselfUnspecified()
    {
        // Arrange
        PresentationBlockType unspecified = default;

        // Act, Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Equal("(unspecified)", unspecified.ToString());
        Assert.Throws<InvalidOperationException>(() => unspecified.Identity);
    }

    [Fact]
    public void ToString_ACataloguedType_NamesTheIdentityAndTheVersion()
    {
        // Act, Assert
        Assert.Equal("answer v1", PresentationBlockType.Answer.ToString());
    }

    [Fact]
    public void Serialization_ACataloguedType_RoundTripsAsItsIdentity()
    {
        // Act
        var json = JsonSerializer.Serialize(PresentationBlockType.Timeline);
        var read = JsonSerializer.Deserialize<PresentationBlockType>(json);

        // Assert
        Assert.Equal("\"timeline\"", json);
        Assert.Equal(PresentationBlockType.Timeline, read);
    }

    [Fact]
    public void Serialization_ACataloguedTypeAsAPropertyName_RoundTripsAsItsIdentity()
    {
        // Arrange
        var written = new Dictionary<PresentationBlockType, int> { [PresentationBlockType.Draft] = 1 };

        // Act
        var json = JsonSerializer.Serialize(written);
        var read = JsonSerializer.Deserialize<Dictionary<PresentationBlockType, int>>(json);

        // Assert
        Assert.Equal("{\"draft\":1}", json);
        Assert.Equal(written, read);
    }

    [Theory]
    [InlineData("\"chart\"")]
    [InlineData("7")]
    public void Deserialization_AnIdentityTheCatalogueDoesNotHold_IsRefused(string json)
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PresentationBlockType>(json));
    }

    [Fact]
    public void Serialization_TheStructDefault_IsRefused()
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize<PresentationBlockType>(default));
    }
}
