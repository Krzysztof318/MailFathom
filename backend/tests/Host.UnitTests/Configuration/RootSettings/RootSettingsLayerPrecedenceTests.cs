// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration.Provisioning;
using MailFathom.Host.Configuration.RootSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.RootSettings;

/// <summary>
/// Covers where the persisted configuration layer sits among the sources a real host composes, and therefore which
/// value an operator actually gets. The sources are the framework's own provider types rather than stand-ins, and the
/// assertions read the effective value and the composed order rather than an index the composition happens to produce.
/// Every file is served from memory, so what decides a value here is the layering and nothing a directory on disk did.
/// </summary>
public sealed class RootSettingsLayerPrecedenceTests
{
    private const string SettingKey = "Layered:Setting";
    private const string ApplicationFileName = "appsettings.json";
    private const string UserSecretsFileName = "secrets.json";
    private const string ProvisionedFileName = "10-provisioned.json";

    private readonly InMemoryConfigurationFileProvider files = new();

    /// <summary>The persisted layer beats the application's own files, which is what makes a persisted setting take effect.</summary>
    [Fact]
    public void RootSettingsLayer_ApplicationFile_Overrides()
    {
        // Arrange
        using var configuration = this.ComposeHostSources(persisted: """{ "Layered": { "Setting": "fromDatabase" } }""");

        // Act
        var effective = configuration[SettingKey];

        // Assert
        Assert.Equal("fromDatabase", effective);
    }

    /// <summary>
    /// It beats the files a deployment provisioned too. Those state what the deployment configured, and a persisted
    /// value is the later decision taken against it.
    /// </summary>
    [Fact]
    public void RootSettingsLayer_ProvisionedFile_Overrides()
    {
        // Arrange
        using var configuration = this.ComposeHostSources(
            persisted: """{ "Layered": { "Setting": "fromDatabase" } }""",
            provisioned: """{ "Layered": { "Setting": "fromMountedFile" } }""");

        // Act
        var effective = configuration[SettingKey];

        // Assert
        Assert.Equal("fromDatabase", effective);
    }

    /// <summary>
    /// A provisioned file the deployment happened to name after the user-secrets store is still the deployment's, so
    /// the persisted layer stays above it. The file name is a deployment's to choose and cannot be what decides where
    /// the boundary between the two sides falls.
    /// </summary>
    [Fact]
    public void RootSettingsLayer_ProvisionedFileNamedLikeTheSecretStore_StillOverridesIt()
    {
        // Arrange
        using var configuration = this.ComposeHostSources(
            persisted: """{ "Layered": { "Setting": "fromDatabase" } }""",
            provisioned: """{ "Layered": { "Setting": "fromMountedFile" } }""",
            provisionedFileName: UserSecretsFileName);

        // Act
        var effective = configuration[SettingKey];

        // Assert
        Assert.Equal("fromDatabase", effective);
    }

    /// <summary>
    /// Every override an operator reaches for still wins, which is the property a bad persisted value is repaired
    /// through: neither User Secrets nor a command-line argument has to reach the database first.
    /// </summary>
    [Theory]
    [InlineData("fromUserSecrets", null)]
    [InlineData("fromUserSecrets", "fromCommandLine")]
    public void OperatorOverride_PersistedValue_Wins(string userSecretsValue, string? commandLineValue)
    {
        // Arrange
        using var configuration = this.ComposeHostSources(
            persisted: """{ "Layered": { "Setting": "fromDatabase" } }""",
            userSecrets: userSecretsValue,
            commandLine: commandLineValue);

        // Act
        var effective = configuration[SettingKey];

        // Assert
        Assert.Equal(commandLineValue ?? userSecretsValue, effective);
    }

    /// <summary>A key the persisted document does not carry is inherited from below rather than read as an empty value.</summary>
    [Fact]
    public void RootSettingsLayer_KeyItDoesNotCarry_InheritsFromTheSourceBeneath()
    {
        // Arrange
        using var configuration = this.ComposeHostSources(persisted: """{ "Unrelated": "value" }""");

        // Act
        var effective = configuration[SettingKey];

        // Assert
        Assert.Equal("fromApplicationFile", effective);
    }

    /// <summary>
    /// An object composes by child key and an array element overrides by its own index, which is what keeps one
    /// persisted setting from restating everything the deployment provisioned beside it.
    /// </summary>
    [Fact]
    public void RootSettingsLayer_ObjectsAndArrayElements_ComposeByKeyRatherThanReplacingWhole()
    {
        // Arrange
        using var configuration = this.ComposeHostSources(
            persisted: """{ "Layered": { "Rules": { "1": { "Name": "persisted" } } } }""",
            provisioned: """
                {
                  "Layered": {
                    "Kept": "fromMountedFile",
                    "Rules": [ { "Name": "first" }, { "Name": "second" } ]
                  }
                }
                """);

        // Act
        var kept = configuration["Layered:Kept"];
        var firstRule = configuration["Layered:Rules:0:Name"];
        var secondRule = configuration["Layered:Rules:1:Name"];

        // Assert
        Assert.Equal("fromMountedFile", kept);
        Assert.Equal("first", firstRule);
        Assert.Equal("persisted", secondRule);
    }

    /// <summary>
    /// The order stated as the provider types it produces: the application's files, then the deployment's, then the
    /// persisted layer, then everything an operator supplies.
    /// </summary>
    [Fact]
    public void ComposedSources_HostOrder_PlacesThePersistedLayerBetweenTheFilesAndTheOverrides()
    {
        // Arrange
        using var configuration = this.ComposeHostSources(
            persisted: "{}",
            provisioned: "{}",
            userSecrets: "fromUserSecrets",
            commandLine: "fromCommandLine");

        // Act
        var order = configuration.Sources.Select(DescribeSource).ToArray();

        // Assert
        Assert.Equal(
            [
                // The manager's own empty memory source, which every ConfigurationManager starts with.
                "memory",
                "environment:DOTNET_",
                "json:appsettings.json",
                "provisioned:10-provisioned.json",
                "root-settings",
                "json:secrets.json",
                "environment:",
                "command-line",
            ],
            order);
    }

    /// <summary>
    /// A persisted document the parser refuses stops startup under the code every other unreadable-layer condition
    /// carries, so an operator greps one number rather than finding that this one condition reports none.
    /// </summary>
    [Theory]
    [InlineData("[1, 2]")]
    [InlineData("\"not settings\"")]
    public void AddRootSettings_DocumentThatIsNotAConfigurationObject_FailsUnderTheLayersOwnCode(string persisted)
    {
        // Arrange
        using var configuration = new ConfigurationManager();

        // Act
        var refusal = Record.Exception(
            () => configuration.AddRootSettings(new RootSettingsDocument(persisted, Version: 12)));

        // Assert
        var unreadable = Assert.IsType<RootSettingsUnreadableException>(refusal);
        Assert.Equal(MailFathomErrorCode.RootSettingsUnreadable, unreadable.ErrorCode);
        Assert.Contains("version 12", unreadable.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A document carrying a setting the layer was itself reached through stops startup naming the key, rather than
    /// publishing a credential the bootstrap read never saw.
    /// </summary>
    [Fact]
    public void AddRootSettings_DocumentCarryingABootstrapSetting_FailsNamingTheKey()
    {
        // Arrange
        using var configuration = new ConfigurationManager();
        var document = new RootSettingsDocument(
            """{ "Persistence": { "Password": { "Reference": "file:the-operator-never-sees-this" } } }""",
            Version: 13);

        // Act
        var refusal = Record.Exception(() => configuration.AddRootSettings(document));

        // Assert
        var refused = Assert.IsType<BootstrapOnlySettingPersistedException>(refusal);
        Assert.Equal(MailFathomErrorCode.BootstrapOnlySettingPersisted, refused.ErrorCode);
        Assert.Contains("Persistence:Password", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("the-operator-never-sees-this", refused.Message, StringComparison.Ordinal);
    }

    private static string DescribeSource(IConfigurationSource source) => source switch
    {
        RootSettingsConfigurationSource => "root-settings",
        CommandLineConfigurationSource => "command-line",
        MemoryConfigurationSource => "memory",
        EnvironmentVariablesConfigurationSource environment => $"environment:{environment.Prefix}",
        ProvisionedJsonConfigurationSource provisioned => $"provisioned:{provisioned.Path}",
        JsonConfigurationSource json => $"json:{json.Path}",
        _ => source.GetType().Name,
    };

    private ConfigurationManager ComposeHostSources(
        string persisted,
        string? provisioned = null,
        string? provisionedFileName = null,
        string? userSecrets = null,
        string? commandLine = null)
    {
        var configuration = new ConfigurationManager();

        // The order a host builder composes, in the framework's own provider types: the host's prefixed environment
        // settings, the application's files, the developer's secret store, the process environment, and the command
        // line. The provisioned and persisted layers are then inserted into it exactly as the host inserts them.
        configuration.AddEnvironmentVariables("DOTNET_");
        this.AddJsonFile(configuration, ApplicationFileName, """{ "Layered": { "Setting": "fromApplicationFile" } }""");

        if (userSecrets is not null)
        {
            // The file name is what identifies the secret store, because that is how the framework layers it in.
            this.AddJsonFile(configuration, UserSecretsFileName, $$"""{ "Layered": { "Setting": "{{userSecrets}}" } }""");
        }

        configuration.AddEnvironmentVariables();

        if (commandLine is not null)
        {
            configuration.AddCommandLine([$"--{SettingKey}={commandLine}"]);
        }

        if (provisioned is not null)
        {
            this.AddProvisionedFile(configuration, provisionedFileName ?? ProvisionedFileName, provisioned);
        }

        configuration.AddRootSettings(new RootSettingsDocument(persisted, Version: 1));

        return configuration;
    }

    private void AddJsonFile(IConfigurationBuilder configuration, string fileName, string content)
    {
        this.files.WithFile(fileName, content);

        configuration.AddJsonFile(this.files, fileName, optional: false, reloadOnChange: false);
    }

    /// <summary>
    /// Inserts one provisioned file the way the provisioned layer does: the source type that layer constructs, at the
    /// index that layer computes. Which files a deployment mounted is the registration's decision and is covered by
    /// <c>ProvisionedConfigurationLayerTests</c>; what is under test here is only where the resulting source lands.
    /// </summary>
    private void AddProvisionedFile(ConfigurationManager configuration, string fileName, string content)
    {
        this.files.WithFile(fileName, content);

        var source = new ProvisionedJsonConfigurationSource
        {
            Path = fileName,
            FileProvider = this.files,
            Optional = false,
            ReloadOnChange = false,
        };

        configuration.Sources.Insert(
            ProvisionedConfigurationLayer.FindInsertionIndex([.. configuration.Sources]),
            source);
    }
}
