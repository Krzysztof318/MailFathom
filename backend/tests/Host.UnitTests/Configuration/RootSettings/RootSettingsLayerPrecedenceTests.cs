// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Provisioning;
using MailFathom.Host.Configuration.RootSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.RootSettings;

/// <summary>
/// Covers where the persisted configuration layer sits among the sources a real host composes, and therefore which
/// value an operator actually gets. The sources are the framework's own provider types rather than stand-ins, and the
/// assertions read the effective value and the composed order rather than an index the composition happens to produce.
/// </summary>
public sealed class RootSettingsLayerPrecedenceTests : IDisposable
{
    private const string SettingKey = "Layered:Setting";

    private readonly DirectoryInfo contentRoot = Directory.CreateTempSubdirectory("mailfathom-root-settings-");
    private readonly PhysicalFileProvider secretStore;

    /// <summary>Creates the temporary content root the composed sources read their files from.</summary>
    public RootSettingsLayerPrecedenceTests() =>
        this.secretStore = new PhysicalFileProvider(this.contentRoot.FullName);

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
                "json:10-provisioned.json",
                "root-settings",
                "json:secrets.json",
                "environment:",
                "command-line",
            ],
            order);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.secretStore.Dispose();
        this.contentRoot.Delete(recursive: true);
    }

    private static string DescribeSource(IConfigurationSource source) => source switch
    {
        RootSettingsConfigurationSource => "root-settings",
        CommandLineConfigurationSource => "command-line",
        MemoryConfigurationSource => "memory",
        EnvironmentVariablesConfigurationSource environment => $"environment:{environment.Prefix}",
        JsonConfigurationSource json => $"json:{Path.GetFileName(json.Path)}",
        _ => source.GetType().Name,
    };

    private ConfigurationManager ComposeHostSources(
        string persisted,
        string? provisioned = null,
        string? userSecrets = null,
        string? commandLine = null)
    {
        var configuration = new ConfigurationManager();

        // The order a host builder composes, in the framework's own provider types: the host's prefixed environment
        // settings, the application's files, the developer's secret store, the process environment, and the command
        // line. The provisioned and persisted layers are then inserted into it exactly as the host inserts them.
        configuration.AddEnvironmentVariables("DOTNET_");
        configuration.AddJsonFile(this.WriteFile(
            "appsettings.json",
            $$"""
              {
                "ConfigurationSources": { "Directory": "{{this.contentRoot.FullName}}" },
                "Layered": { "Setting": "fromApplicationFile" }
              }
              """));

        if (userSecrets is not null)
        {
            this.WriteFile("secrets.json", $$"""{ "Layered": { "Setting": "{{userSecrets}}" } }""");

            // The file name is what identifies the secret store, because that is how the framework layers it in.
            configuration.AddJsonFile(this.secretStore, "secrets.json", optional: true, reloadOnChange: false);
        }

        configuration.AddEnvironmentVariables();

        if (commandLine is not null)
        {
            configuration.AddCommandLine([$"--{SettingKey}={commandLine}"]);
        }

        if (provisioned is not null)
        {
            this.WriteFile("10-provisioned.json", provisioned);
            configuration.AddProvisionedConfiguration(
                new FakeProvisionedConfigurationFileSystem()
                    .WithDirectory(this.contentRoot.FullName, "10-provisioned.json"));
        }

        configuration.AddRootSettings(new RootSettingsDocument(persisted, Version: 1));

        return configuration;
    }

    private string WriteFile(string fileName, string content)
    {
        var path = Path.Combine(this.contentRoot.FullName, fileName);

        File.WriteAllText(path, content);

        return path;
    }
}
