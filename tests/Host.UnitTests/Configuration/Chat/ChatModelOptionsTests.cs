// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.AI.Chat;
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
    /// <summary>
    /// Each of these members has no useful default, so writing one is unambiguous intent that a provider be in use. An
    /// address in particular is what a private or cloud deployment is reached at, and dropping it silently would leave
    /// an operator believing their traffic goes somewhere it never does.
    /// </summary>
    /// <remarks>The case is named rather than passed, because the bound options type is internal to the host and a public test signature may not carry it.</remarks>
    [Theory]
    [InlineData("model")]
    [InlineData("address")]
    [InlineData("api-key")]
    [InlineData("entra-credential")]
    [InlineData("reasoning-effort")]
    [InlineData("api")]
    public void Validate_SettingsWithNoAlias_AreRefusedRatherThanIgnored(string writtenSetting)
    {
        // Arrange
        var settings = WrittenWithoutAnAlias(writtenSetting);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.False(settings.IsConfigured);
        Assert.Contains(errors, error => error.Contains("Alias", StringComparison.Ordinal));
    }

    /// <summary>
    /// A section left entirely alone is the ordinary deployment that generates nothing, and the bounds and the timeout
    /// carry defaults — so a deployment that accepted them is indistinguishable from one that never wrote the section,
    /// and neither may be refused.
    /// </summary>
    [Fact]
    public void Validate_ASectionCarryingOnlyDefaults_IsAcceptedAsNoProvider()
    {
        // Arrange
        var settings = new ChatModelOptions { MaxOutputTokens = 2048, RequestTimeout = TimeSpan.FromMinutes(3) };

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.False(settings.IsConfigured);
        Assert.Empty(errors);
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

    private static ChatModelOptions WrittenWithoutAnAlias(string writtenSetting) => writtenSetting switch
    {
        "model" => new ChatModelOptions { Model = "a-chat-model" },
        "address" => new ChatModelOptions { Address = "https://resource.cloud.invalid/openai/v1/" },
        "api-key" => new ChatModelOptions { ApiKey = new ConfiguredSecret { SecretReference = "env:CHAT_KEY" } },
        "reasoning-effort" => new ChatModelOptions { ReasoningEffort = ChatReasoningEffort.Low },
        "api" => new ChatModelOptions { Api = ChatProviderApi.Responses },
        _ => new ChatModelOptions
        {
            EntraCredential = new ProviderEntraCredentialOptions
            {
                Kind = ProviderEndpointCredentialKind.ManagedIdentity,
            },
        },
    };

    /// <summary>
    /// The binder accepts any number for an enum, so a value no member declares reads as a choice while naming nothing.
    /// For the API that is a request sent to a path this deployment cannot reach; for the effort it is a parameter the
    /// provider refuses. Both are learned at startup rather than from the first question a client is waiting on.
    /// </summary>
    [Fact]
    public void Validate_AnApiOrAReasoningEffortNamingNoValue_IsRefused()
    {
        // Arrange
        var withAnUndeclaredApi = Declared();
        withAnUndeclaredApi.Api = (ChatProviderApi)7;

        var withAnUndeclaredEffort = Declared();
        withAnUndeclaredEffort.ReasoningEffort = (ChatReasoningEffort)9;

        // Act
        var apiErrors = Validate(withAnUndeclaredApi);
        var effortErrors = Validate(withAnUndeclaredEffort);

        // Assert
        Assert.Contains(apiErrors, error => error.Contains("Api", StringComparison.Ordinal));
        Assert.Contains(effortErrors, error => error.Contains("ReasoningEffort", StringComparison.Ordinal));
    }

    /// <summary>Both APIs and every declared effort are accepted, so nothing an operator may legitimately write is refused.</summary>
    [Theory]
    [InlineData(ChatProviderApi.ChatCompletions, null)]
    [InlineData(ChatProviderApi.Responses, ChatReasoningEffort.None)]
    [InlineData(ChatProviderApi.Responses, ChatReasoningEffort.ExtraHigh)]
    public void Validate_ADeclaredApiAndEffort_AreAccepted(ChatProviderApi api, ChatReasoningEffort? effort)
    {
        // Arrange
        var settings = Declared();
        settings.Api = api;
        settings.ReasoningEffort = effort;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Empty(errors);
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
