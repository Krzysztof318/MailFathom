// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.AI.Providers;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.Configuration.Providers;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Chat;

/// <summary>Covers what a chat declaration has to say before an instance will start on it.</summary>
public sealed class ChatModelOptionsTests
{
    /// <summary>An instance that generates nothing is a working instance, so an absent section starts the service.</summary>
    [Fact]
    public void Validate_AnAbsentSection_IsAcceptedAndConfiguresNoProvider()
    {
        // Arrange
        var settings = new ChatModelOptions();

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.False(settings.IsConfigured);
        Assert.Empty(errors);
    }

    /// <summary>
    /// A section carrying a model and a key but no alias reads to an operator as a configured provider, and nothing
    /// would ever call it. That is the one absent-alias shape worth refusing rather than passing over.
    /// </summary>
    [Fact]
    public void Validate_SettingsWithNoAlias_AreRefusedRatherThanIgnored()
    {
        // Arrange
        var settings = new ChatModelOptions
        {
            Model = "a-chat-model",
            ApiKey = new ConfiguredSecret { SecretReference = "env:CHAT_KEY" },
        };

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.False(settings.IsConfigured);
        Assert.Contains(errors, error => error.Contains("Alias", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ADeclaredEndpoint_IsAccepted()
    {
        // Arrange
        var settings = Declared();

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.True(settings.IsConfigured);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_AnEndpointWithNoModel_IsRefused()
    {
        // Arrange
        var settings = Declared();
        settings.Model = string.Empty;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.Contains("Model", StringComparison.Ordinal));
    }

    /// <summary>The request carries a credential, so an unencrypted address would publish it to anyone on the path.</summary>
    [Theory]
    [InlineData("http://provider.invalid/v1/")]
    [InlineData("/openai/v1/")]
    [InlineData("not an address")]
    public void Validate_AnAddressThatIsNotAbsoluteHttps_IsRefused(string address)
    {
        // Arrange
        var settings = Declared();
        settings.Address = address;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.Contains("Address", StringComparison.Ordinal));
    }

    /// <summary>Exactly one credential authenticates an endpoint, so both and neither are equally wrong.</summary>
    [Fact]
    public void Validate_NeitherCredential_IsRefused()
    {
        // Arrange
        var settings = Declared();
        settings.ApiKey = null;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.Contains("Exactly one", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_BothCredentials_IsRefused()
    {
        // Arrange
        var settings = Declared();
        settings.EntraCredential = new ProviderEntraCredentialOptions
        {
            Kind = ProviderEndpointCredentialKind.ManagedIdentity,
        };

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.Contains("Exactly one", StringComparison.Ordinal));
    }

    /// <summary>A key is declared in its own block, so naming it as a Microsoft Entra shape is a declaration to correct.</summary>
    [Fact]
    public void Validate_AnEntraCredentialOfKindApiKey_IsRefused()
    {
        // Arrange
        var settings = Declared();
        settings.ApiKey = null;
        settings.EntraCredential = new ProviderEntraCredentialOptions
        {
            Kind = ProviderEndpointCredentialKind.ApiKey,
        };

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.Contains("ApiKey", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ARequestTimeoutThatIsNotPositive_IsRefused()
    {
        // Arrange
        var settings = Declared();
        settings.RequestTimeout = TimeSpan.Zero;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.Contains("RequestTimeout", StringComparison.Ordinal));
    }

    /// <summary>The endpoint the adapter runs on takes its routing name from the declared model and trims what an operator typed.</summary>
    [Fact]
    public void ToEndpoint_ADeclaration_CarriesTheAliasAddressAndRoutedModel()
    {
        // Arrange
        var settings = Declared();
        settings.Alias = "  answering  ";

        // Act
        var endpoint = settings.ToEndpoint();

        // Assert
        Assert.Equal("answering", endpoint.Alias);
        Assert.Equal("a-chat-model", endpoint.RoutedModelName);
        Assert.Equal(new Uri("https://provider.invalid/v1/"), endpoint.Address);
    }

    /// <summary>An endpoint with no address of its own is the provider's first-party API at the library's default.</summary>
    [Fact]
    public void ToEndpoint_WithNoAddress_LeavesTheProviderDefaultInPlace()
    {
        // Arrange
        var settings = Declared();
        settings.Address = string.Empty;

        // Act
        var endpoint = settings.ToEndpoint();

        // Assert
        Assert.Null(endpoint.Address);
    }

    /// <summary>
    /// The sampling parameters are bounded by an annotation rather than by the rules above, and an annotation reads as
    /// a rule while enforcing nothing until the framework's own validation runs it. This goes through that validation
    /// rather than through <see cref="IValidatableObject" /> alone, so a value the provider would reject on every call
    /// is learned from configuration instead of from a paid request.
    /// </summary>
    [Theory]
    [InlineData(-0.5f, null)]
    [InlineData(2.5f, null)]
    [InlineData(null, -0.5f)]
    [InlineData(null, 1.5f)]
    public void Validate_ASamplingParameterOutsideItsRange_IsRefusedByTheAnnotation(float? temperature, float? topP)
    {
        // Arrange
        var settings = Declared();
        settings.Temperature = temperature;
        settings.TopP = topP;

        // Act
        var accepted = Validator.TryValidateObject(
            settings,
            new ValidationContext(settings),
            validationResults: null,
            validateAllProperties: true);

        // Assert
        Assert.False(accepted);
    }

    /// <summary>An unset sampling parameter is what a model that rejects the parameter needs, so it may not be refused.</summary>
    [Fact]
    public void Validate_UnsetSamplingParameters_AreAccepted()
    {
        // Arrange
        var settings = Declared();

        // Act
        var accepted = Validator.TryValidateObject(
            settings,
            new ValidationContext(settings),
            validationResults: null,
            validateAllProperties: true);

        // Assert
        Assert.True(accepted);
    }

    private static ChatModelOptions Declared() => new()
    {
        Alias = "answering",
        Model = "a-chat-model",
        Address = "https://provider.invalid/v1/",
        ApiKey = new ConfiguredSecret { SecretReference = "env:CHAT_KEY" },
    };

    private static IReadOnlyList<string> Validate(ChatModelOptions settings) =>
    [
        .. settings
            .Validate(new ValidationContext(settings))
            .Select(result => result.ErrorMessage ?? string.Empty),
    ];
}
