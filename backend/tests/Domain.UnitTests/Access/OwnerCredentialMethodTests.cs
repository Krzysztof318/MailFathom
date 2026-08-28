// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Domain.Access;
using Xunit;

namespace MailFathom.Domain.UnitTests.Access;

/// <summary>Covers the published set of credential methods, what each says about its own storage, and the two directions a written name travels.</summary>
/// <remarks>Everything here reads <see cref="OwnerCredentialMethod.All" /> rather than reflecting over the type, so a member declared and left out of that list fails as the missing coverage it is.</remarks>
public sealed class OwnerCredentialMethodTests
{
    [Fact]
    public void All_TheDeclaredMethods_PublishFourDistinctNames()
    {
        // Act
        var names = OwnerCredentialMethod.All.Select(method => method.Name).ToArray();

        // Assert
        Assert.Equal(4, names.Length);
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(["password", "api-key", "public-key", "oauth-subject"], names);
    }

    /// <summary>The three flags decide what a rotation may do and what a listing may publish, so each is asserted rather than inferred from the name.</summary>
    [Theory]
    [InlineData("password", true, true, false)]
    [InlineData("api-key", false, true, true)]
    [InlineData("public-key", true, true, false)]
    [InlineData("oauth-subject", false, false, false)]
    public void Members_EachPublishedMethod_StatesWhatItKeepsAndWhatMayBeReplaced(
        string name,
        bool storesMaterial,
        bool materialIsReplaceable,
        bool lookupIsDerivedFromTheSecret)
    {
        // Act
        var parsed = OwnerCredentialMethod.TryParse(name, out var method);

        // Assert
        Assert.True(parsed);
        Assert.Equal(storesMaterial, method.StoresMaterial);
        Assert.Equal(materialIsReplaceable, method.MaterialIsReplaceable);
        Assert.Equal(lookupIsDerivedFromTheSecret, method.LookupIsDerivedFromTheSecret);
    }

    /// <summary>The name is written by hand in a configuration file and on a command line, which is why the comparison ignores case.</summary>
    [Theory]
    [InlineData("api-key")]
    [InlineData("API-KEY")]
    [InlineData("Api-Key")]
    public void TryParse_APublishedNameInAnyCase_ReadsTheMethod(string written)
    {
        // Act
        var parsed = OwnerCredentialMethod.TryParse(written, out var method);

        // Assert
        Assert.True(parsed);
        Assert.Equal(OwnerCredentialMethod.ApiKey, method);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("apikey")]
    [InlineData("passwrod")]
    [InlineData("ldap")]
    public void TryParse_ANameNothingPublishes_AnswersTheUnspecifiedDefault(string? written)
    {
        // Act
        var parsed = OwnerCredentialMethod.TryParse(written, out var method);

        // Assert
        Assert.False(parsed);
        Assert.False(method.IsSpecified);
    }

    [Fact]
    public void Default_TheUnspecifiedValue_ReportsItselfAndRefusesToNameAMethod()
    {
        // Arrange
        var unspecified = default(OwnerCredentialMethod);

        // Act
        var naming = Record.Exception(() => unspecified.Name);

        // Assert
        Assert.False(unspecified.IsSpecified);
        Assert.IsType<InvalidOperationException>(naming);
        Assert.Equal("(unspecified)", unspecified.ToString());
    }

    [Fact]
    public void ToString_APublishedMethod_RendersTheNameALogAndAListingCarry()
    {
        // Assert
        Assert.Equal("oauth-subject", OwnerCredentialMethod.OAuthSubject.ToString());
    }

    [Fact]
    public void Serialization_EveryPublishedMethod_RoundTripsAsAValue()
    {
        // Act
        var roundTripped = OwnerCredentialMethod.All
            .Select(method => JsonSerializer.Deserialize<OwnerCredentialMethod>(JsonSerializer.Serialize(method)))
            .ToArray();

        // Assert
        Assert.Equal(OwnerCredentialMethod.All, roundTripped);
        Assert.Equal("\"public-key\"", JsonSerializer.Serialize(OwnerCredentialMethod.PublicKey));
    }

    [Fact]
    public void Serialization_EveryPublishedMethod_RoundTripsAsAPropertyName()
    {
        // Arrange
        var keyed = OwnerCredentialMethod.All.ToDictionary(method => method, method => method.Name);

        // Act
        var written = JsonSerializer.Serialize(keyed);
        var read = JsonSerializer.Deserialize<Dictionary<OwnerCredentialMethod, string>>(written);

        // Assert
        Assert.Contains("\"api-key\":", written, StringComparison.Ordinal);
        Assert.Equal(keyed, read);
    }

    /// <summary>A name nothing publishes is unknown rather than new, so reading one is a failure rather than a value nobody declared.</summary>
    [Theory]
    [InlineData("\"ldap\"")]
    [InlineData("7")]
    public void Deserialization_AValueNothingPublishes_IsRefused(string written)
    {
        // Act
        var read = Record.Exception(() => JsonSerializer.Deserialize<OwnerCredentialMethod>(written));

        // Assert
        Assert.IsType<JsonException>(read);
    }

    [Fact]
    public void Serialization_TheUnspecifiedDefault_IsRefusedRatherThanWritten()
    {
        // Act
        var written = Record.Exception(() => JsonSerializer.Serialize(default(OwnerCredentialMethod)));

        // Assert
        Assert.IsType<JsonException>(written);
    }

    [Fact]
    public void Serialization_TheUnspecifiedDefaultAsAPropertyName_IsRefusedRatherThanWritten()
    {
        // Arrange
        var keyed = new Dictionary<OwnerCredentialMethod, string> { [default] = "nothing" };

        // Act
        var written = Record.Exception(() => JsonSerializer.Serialize(keyed));

        // Assert
        Assert.IsType<JsonException>(written);
    }
}
