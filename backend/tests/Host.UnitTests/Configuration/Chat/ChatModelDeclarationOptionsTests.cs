// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Providers;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.Configuration.Providers;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Chat;

/// <summary>Covers what one declared chat model has to say before a request could be sent to it.</summary>
public sealed class ChatModelDeclarationOptionsTests
{
    [Fact]
    public void FindConfigurationErrors_ADeclaredModel_IsAccepted()
    {
        // Act
        var errors = Validate(DeclaredChatModels.Model());

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>The alias is the name every reference, log line, and credential reaches this model by, so a block without one could be named by nothing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FindConfigurationErrors_AModelWithNoAlias_IsRefused(string alias)
    {
        // Arrange
        var model = DeclaredChatModels.Model();
        model.Alias = alias;

        // Act
        var errors = Validate(model);

        // Assert
        Assert.Contains(errors, error => error.Contains("Alias", StringComparison.Ordinal));
    }

    [Fact]
    public void FindConfigurationErrors_AModelWithNoRoutedName_IsRefused()
    {
        // Arrange
        var model = DeclaredChatModels.Model();
        model.Model = string.Empty;

        // Act
        var errors = Validate(model);

        // Assert
        Assert.Contains(errors, error => error.Contains("Model", StringComparison.Ordinal));
    }

    /// <summary>Only the two schemes a request could be sent over are addresses at all.</summary>
    [Theory]
    [InlineData("/openai/v1/")]
    [InlineData("not an address")]
    [InlineData("ftp://provider.invalid/v1/")]
    public void FindConfigurationErrors_AnAddressThatIsNotAbsoluteHttpOrHttps_IsRefused(string address)
    {
        // Arrange
        var model = DeclaredChatModels.Model();
        model.Address = address;

        // Act
        var errors = Validate(model);

        // Assert
        Assert.Contains(errors, error => error.Contains("Address", StringComparison.Ordinal));
    }

    /// <summary>The declared model carries a credential, so an unencrypted address would publish it to anything on the path.</summary>
    [Fact]
    public void FindConfigurationErrors_ACredentialOverAPlainAddress_IsRefused()
    {
        // Arrange
        var model = DeclaredChatModels.Model();
        model.Address = "http://127.0.0.1:11434/v1";

        // Act
        var errors = Validate(model);

        // Assert
        Assert.Contains(errors, error => error.Contains("plain http Address", StringComparison.Ordinal));
    }

    /// <summary>
    /// The shape of a model server the operator runs themselves, and the reason this role reaches the shared rule rather
    /// than keeping a copy: a scheme rule of its own would refuse what the other role accepts.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_AModelNeedingNoCredentialOnAPlainAddress_IsAccepted()
    {
        // Arrange
        var model = DeclaredChatModels.Model(address: "http://model-server:8000/v1", authenticated: false);

        // Act
        var errors = Validate(model);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Needing no credential is one of the three shapes rather than a fourth thing beside them.</summary>
    [Fact]
    public void FindConfigurationErrors_AModelDeclaringBothAKeyAndNoCredential_IsRefused()
    {
        // Arrange
        var model = DeclaredChatModels.Model();
        model.Unauthenticated = true;

        // Act
        var errors = Validate(model);

        // Assert
        Assert.Contains(errors, error => error.Contains("more than one", StringComparison.Ordinal));
    }

    /// <summary>Exactly one credential authenticates an endpoint, so both and neither are equally wrong.</summary>
    [Fact]
    public void FindConfigurationErrors_NeitherCredential_IsRefused()
    {
        // Arrange
        var model = DeclaredChatModels.Model();
        model.ApiKey = null;

        // Act
        var errors = Validate(model);

        // Assert
        Assert.Contains(errors, error => error.Contains("Exactly one", StringComparison.Ordinal));
    }

    [Fact]
    public void FindConfigurationErrors_BothCredentials_IsRefused()
    {
        // Arrange
        var model = DeclaredChatModels.Model();
        model.EntraCredential = new ProviderEntraCredentialOptions
        {
            Kind = ProviderEndpointCredentialKind.ManagedIdentity,
        };

        // Act
        var errors = Validate(model);

        // Assert
        Assert.Contains(errors, error => error.Contains("Exactly one", StringComparison.Ordinal));
    }

    /// <summary>A key is declared in its own block, so naming it as a Microsoft Entra shape is a declaration to correct.</summary>
    [Fact]
    public void FindConfigurationErrors_AnEntraCredentialOfKindApiKey_IsRefused()
    {
        // Arrange
        var model = DeclaredChatModels.Model();
        model.ApiKey = null;
        model.EntraCredential = new ProviderEntraCredentialOptions { Kind = ProviderEndpointCredentialKind.ApiKey };

        // Act
        var errors = Validate(model);

        // Assert
        Assert.Contains(errors, error => error.Contains("ApiKey", StringComparison.Ordinal));
    }

    /// <summary>A model needing no credential says so with Unauthenticated, so naming that as a Microsoft Entra shape declares nothing.</summary>
    [Fact]
    public void FindConfigurationErrors_AnEntraCredentialOfKindUnauthenticated_IsRefused()
    {
        // Arrange
        var model = DeclaredChatModels.Model();
        model.ApiKey = null;
        model.EntraCredential = new ProviderEntraCredentialOptions
        {
            Kind = ProviderEndpointCredentialKind.Unauthenticated,
        };

        // Act
        var errors = Validate(model);

        // Assert
        Assert.Contains(errors, error => error.Contains("Unauthenticated", StringComparison.Ordinal));
    }

    [Fact]
    public void FindConfigurationErrors_ARequestTimeoutThatIsNotPositive_IsRefused()
    {
        // Arrange
        var model = DeclaredChatModels.Model();
        model.RequestTimeout = TimeSpan.Zero;

        // Act
        var errors = Validate(model);

        // Assert
        Assert.Contains(errors, error => error.Contains("RequestTimeout", StringComparison.Ordinal));
    }

    /// <summary>
    /// The bounds are stated as annotations, and the options framework never descends into the elements of a collection —
    /// so a block that did not run them itself would carry a value the provider rejects on every call this deployment
    /// made. This is what proves the block runs them.
    /// </summary>
    [Theory]
    [InlineData(-0.5f, null)]
    [InlineData(2.5f, null)]
    [InlineData(null, -0.5f)]
    [InlineData(null, 1.5f)]
    public void FindConfigurationErrors_ASamplingParameterOutsideItsRange_IsRefused(float? temperature, float? topP)
    {
        // Arrange
        var model = DeclaredChatModels.Model();
        model.Temperature = temperature;
        model.TopP = topP;

        // Act
        var errors = Validate(model);

        // Assert
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void FindConfigurationErrors_AnOutputBudgetOutsideItsRange_IsRefused()
    {
        // Arrange
        var model = DeclaredChatModels.Model();
        model.MaxOutputTokens = 0;

        // Act
        var errors = Validate(model);

        // Assert
        Assert.Contains(errors, error => error.Contains("MaxOutputTokens", StringComparison.Ordinal));
    }

    /// <summary>The binder accepts any number for an enum, so a value naming no API would read as a choice while naming nothing.</summary>
    [Fact]
    public void FindConfigurationErrors_AnApiNamingNoValue_IsRefused()
    {
        // Arrange
        var model = DeclaredChatModels.Model();
        model.Api = (ChatProviderApi)7;

        // Act
        var errors = Validate(model);

        // Assert
        Assert.Contains(errors, error => error.Contains("Api", StringComparison.Ordinal));
    }

    /// <summary>The effort's shape is checked and its vocabulary is not, so what startup refuses is a value no provider could read as a level whatever it supports.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("very high")]
    [InlineData(" high")]
    public void FindConfigurationErrors_AReasoningEffortNoProviderCouldRead_IsRefused(string effort)
    {
        // Arrange
        var model = DeclaredChatModels.Model();
        model.ReasoningEffort = effort;

        // Act
        var errors = Validate(model);

        // Assert
        Assert.Contains(errors, error => error.Contains("ReasoningEffort", StringComparison.Ordinal));
    }

    /// <summary>
    /// Both APIs are accepted, and so is a level this build has never heard of: which levels a model offers is the
    /// model's, and refusing the next one a provider adds would make a release the price of using it.
    /// </summary>
    [Theory]
    [InlineData(ChatProviderApi.ChatCompletions, null)]
    [InlineData(ChatProviderApi.Responses, "none")]
    [InlineData(ChatProviderApi.Responses, "xhigh")]
    [InlineData(ChatProviderApi.Responses, "a-level-released-later")]
    public void FindConfigurationErrors_ADeclaredApiAndEffort_AreAccepted(ChatProviderApi api, string? effort)
    {
        // Arrange
        var model = DeclaredChatModels.Model();
        model.Api = api;
        model.ReasoningEffort = effort;

        // Act
        var errors = Validate(model);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A header travels beside the credential, so what it needs is a field name and a reference to read the value from.</summary>
    [Fact]
    public void FindConfigurationErrors_ADeclaredHeader_IsAccepted()
    {
        // Arrange
        var model = WithHeader("x-tenant", "env:TENANT");

        // Act
        var errors = Validate(model);

        // Assert
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("x tenant")]
    [InlineData("x:tenant")]
    [InlineData("x-tenant\nX-Evil: 1")]
    public void FindConfigurationErrors_AHeaderNameThatIsNotAFieldName_IsRefused(string name)
    {
        // Arrange
        var model = WithHeader(name, "env:TENANT");

        // Act
        var errors = Validate(model);

        // Assert
        Assert.Contains(errors, error => error.Contains("not a field name", StringComparison.Ordinal));
    }

    /// <summary>
    /// The credential is what the client construction writes, and the framing headers are the transport's. A declaration
    /// on either would be overwritten without a word or would describe a request other than the one actually sent.
    /// </summary>
    [Theory]
    [InlineData("Authorization")]
    [InlineData("authorization")]
    [InlineData("Content-Length")]
    [InlineData("Host")]
    public void FindConfigurationErrors_AHeaderTheRequestWritesForItself_IsRefused(string name)
    {
        // Arrange
        var model = WithHeader(name, "env:TENANT");

        // Act
        var errors = Validate(model);

        // Assert
        Assert.Contains(errors, error => error.Contains("writes for itself", StringComparison.Ordinal));
    }

    /// <summary>The value is a secret reference like every other credential here, so a header naming none says nothing to send.</summary>
    [Fact]
    public void FindConfigurationErrors_AHeaderWithNoValue_IsRefused()
    {
        // Arrange
        var model = DeclaredChatModels.Model();
        model.ExtraHeaders.Add(new ChatModelHeaderOptions { Name = "x-tenant" });

        // Act
        var errors = Validate(model);

        // Assert
        Assert.Contains(errors, error => error.Contains("no Value", StringComparison.Ordinal));
    }

    [Fact]
    public void FindConfigurationErrors_AHeaderWithNoName_IsRefused()
    {
        // Arrange
        var model = DeclaredChatModels.Model();
        model.ExtraHeaders.Add(new ChatModelHeaderOptions
        {
            Value = new ConfiguredSecret { SecretReference = "env:TENANT" },
        });

        // Act
        var errors = Validate(model);

        // Assert
        Assert.Contains(errors, error => error.Contains("no Name", StringComparison.Ordinal));
    }

    /// <summary>A field name is sent once, so the repetitions would be resolved, paid for, and discarded.</summary>
    [Fact]
    public void FindConfigurationErrors_AHeaderDeclaredTwice_IsRefused()
    {
        // Arrange
        var model = WithHeader("x-tenant", "env:TENANT");
        model.ExtraHeaders.Add(new ChatModelHeaderOptions
        {
            Name = "X-Tenant",
            Value = new ConfiguredSecret { SecretReference = "env:OTHER" },
        });

        // Act
        var errors = Validate(model);

        // Assert
        Assert.Contains(errors, error => error.Contains("more than once", StringComparison.Ordinal));
    }

    /// <summary>The endpoint the adapter runs on takes its routing name from the declared model and trims what an operator typed.</summary>
    [Fact]
    public void ToEndpoint_ADeclaration_CarriesTheAliasAddressAndRoutedModel()
    {
        // Arrange
        var model = DeclaredChatModels.Model();
        model.Alias = "  answering  ";

        // Act
        var endpoint = model.ToEndpoint();

        // Assert
        Assert.Equal("answering", endpoint.Alias);
        Assert.Equal("a-chat-model", endpoint.RoutedModelName);
        Assert.Equal(new Uri("https://provider.invalid/v1/"), endpoint.Address);
    }

    /// <summary>A model with no address of its own is the provider's first-party API at the library's default.</summary>
    [Fact]
    public void ToEndpoint_WithNoAddress_LeavesTheProviderDefaultInPlace()
    {
        // Arrange
        var model = DeclaredChatModels.Model(address: string.Empty);

        // Act
        var endpoint = model.ToEndpoint();

        // Assert
        Assert.Null(endpoint.Address);
    }

    /// <summary>A header's own rules name a property of the header, so the reported key carries the header's index and not the block's alone.</summary>
    /// <remarks>
    /// Without the element's index an operator is sent to <c>Chat:Models:0:Name</c>, which nothing binds — the key they
    /// actually edit is <c>Chat:Models:0:ExtraHeaders:0:Name</c>.
    /// </remarks>
    [Fact]
    public void FindConfigurationErrors_ASecondHeaderThatIsRefused_NamesTheKeyBelowExtraHeaders()
    {
        // Arrange
        var model = WithHeader("x-tenant", "env:TENANT");
        model.ExtraHeaders.Add(new ChatModelHeaderOptions
        {
            Name = "Authorization",
            Value = new ConfiguredSecret { SecretReference = "env:OTHER" },
        });

        // Act
        var keys = ValidateKeys(model);

        // Assert
        Assert.Contains("Models:0:ExtraHeaders:1:Name", keys);
    }

    /// <summary>The whole-collection rules stay keyed to the collection, because a repetition is nothing one element got wrong.</summary>
    [Fact]
    public void FindConfigurationErrors_AHeaderDeclaredTwice_NamesTheCollectionRatherThanAnElement()
    {
        // Arrange
        var model = WithHeader("x-tenant", "env:TENANT");
        model.ExtraHeaders.Add(new ChatModelHeaderOptions
        {
            Name = "X-Tenant",
            Value = new ConfiguredSecret { SecretReference = "env:OTHER" },
        });

        // Act
        var keys = ValidateKeys(model);

        // Assert
        Assert.Contains("Models:0:ExtraHeaders", keys);
    }

    private static ChatModelDeclarationOptions WithHeader(string name, string secretReference)
    {
        var model = DeclaredChatModels.Model();

        model.ExtraHeaders.Add(new ChatModelHeaderOptions
        {
            Name = name,
            Value = new ConfiguredSecret { SecretReference = secretReference },
        });

        return model;
    }

    private static IReadOnlyList<string> Validate(ChatModelDeclarationOptions model) =>
    [
        .. model.FindConfigurationErrors(position: 0).Select(result => result.ErrorMessage ?? string.Empty),
    ];

    private static IReadOnlyList<string> ValidateKeys(ChatModelDeclarationOptions model) =>
    [
        .. model.FindConfigurationErrors(position: 0).SelectMany(result => result.MemberNames),
    ];
}
