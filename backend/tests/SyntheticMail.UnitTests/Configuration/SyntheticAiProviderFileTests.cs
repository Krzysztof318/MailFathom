// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.SyntheticMail.Configuration;

using Xunit;

namespace MailFathom.SyntheticMail.UnitTests.Configuration;

/// <summary>What the command demands of an AI provider file before it will ask a model for anything.</summary>
public sealed class SyntheticAiProviderFileTests
{
    private const string Origin = "synthetic-mail-ai.local.json";

    private const string Complete = """
        {
          "apiKey": "not-a-real-key",
          "model": "gpt-test",
          "endpoint": "https://api.example.test/v1"
        }
        """;

    [Fact]
    public void ReadFrom_ACompleteFile_ReadsEveryValue()
    {
        // Arrange, Act
        var provider = Read(Complete);

        // Assert
        Assert.Equal("not-a-real-key", provider.ApiKey);
        Assert.Equal("gpt-test", provider.Model);
        Assert.Equal(new Uri("https://api.example.test/v1"), provider.Endpoint);
    }

    [Fact]
    public void ReadFrom_AFileNamingNoEndpoint_ReachesTheProvidersOwnDefaultAddress()
    {
        // Arrange, Act
        var provider = Read("""{ "apiKey": "k", "model": "gpt-test" }""");

        // Assert
        Assert.Null(provider.Endpoint);
    }

    [Theory]
    [InlineData("apiKey", """{ "model": "gpt-test" }""")]
    [InlineData("model", """{ "apiKey": "k" }""")]
    public void ReadFrom_AFileMissingARequiredValue_IsRefusedNamingTheKey(string key, string contents)
    {
        // Arrange, Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => Read(contents));

        // Assert
        Assert.Contains($"'{key}' is not set in '{Origin}'", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://api.example.test/v1")]
    [InlineData("api.example.test/v1")]
    [InlineData("not an address")]
    public void ReadFrom_ANEndpointOutsideItsShape_IsRefusedNamingWhatToCheck(string endpoint)
    {
        // Arrange
        var contents = $$"""{ "apiKey": "k", "model": "gpt-test", "endpoint": "{{endpoint}}" }""";

        // Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => Read(contents));

        // Assert
        // The key travels in a header, so the refusal is the same the sending account makes of an unsecured
        // connection: there is no value to name that would let the run proceed, only the shape it must take.
        Assert.Contains("'endpoint'", failure.Message, StringComparison.Ordinal);
        Assert.Contains(endpoint, failure.Message, StringComparison.Ordinal);
        Assert.Contains("no unsecured option", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadFrom_ContentsThatAreNotJson_IsRefusedNamingTheFile()
    {
        // Arrange, Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => Read("this is not a file"));

        // Assert
        Assert.Contains(Origin, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_AFileNothingHasWritten_SaysWhatToWriteAndWhere()
    {
        // Arrange
        var missing = Path.Combine(AppContext.BaseDirectory, $"nothing-writes-this-{Guid.NewGuid():N}.local.json");

        // Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => SyntheticAiProviderFile.Read(missing));

        // Assert
        // The whole of the failure is what to write and where: a tool nobody has configured yet is the ordinary first
        // experience of it, so the message is the setup rather than a pointer to the setup.
        Assert.Contains(missing, failure.Message, StringComparison.Ordinal);
        Assert.Contains("apiKey", failure.Message, StringComparison.Ordinal);
        Assert.Contains("model", failure.Message, StringComparison.Ordinal);
        Assert.Contains("git-ignored", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultPath_BesideTheBuiltCommand_IsTheFileTheProjectCopiesThere()
    {
        // Arrange, Act
        var path = SyntheticAiProviderFile.DefaultPath();

        // Assert
        Assert.EndsWith("synthetic-mail-ai.local.json", path, StringComparison.Ordinal);
        Assert.True(Path.IsPathRooted(path));
    }

    [Fact]
    public void ToString_AProviderConfiguration_MasksTheKeyAndKeepsWhatAReadingOfItNeeds()
    {
        // Arrange
        var provider = Read(Complete);

        // Act
        var line = provider.ToString();

        // Assert
        Assert.DoesNotContain("not-a-real-key", line, StringComparison.Ordinal);
        Assert.Contains("gpt-test", line, StringComparison.Ordinal);
        Assert.Contains("https://api.example.test/v1", line, StringComparison.Ordinal);
    }

    private static AiProviderConfiguration Read(string contents)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(contents));

        return SyntheticAiProviderFile.ReadFrom(stream, Origin);
    }
}
