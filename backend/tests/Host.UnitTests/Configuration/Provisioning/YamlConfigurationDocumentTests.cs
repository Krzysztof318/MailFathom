// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Host.Configuration.Provisioning;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Provisioning;

/// <summary>Covers how a provisioned YAML file flattens into configuration keys, and what it refuses.</summary>
/// <remarks>
/// The contract is equivalence with the JSON provider, so the equivalence tests compare against what the framework's
/// own JSON provider produces for the same document rather than against keys written out here.
/// </remarks>
public sealed class YamlConfigurationDocumentTests
{
    private const string EquivalentJson = """
        {
          "MailSynchronization": {
            "Accounts": [
              { "Alias": "work", "Folders": [ "INBOX", "Archive" ] },
              { "Alias": "family", "Folders": [] }
            ],
            "Interval": "00:05:00"
          },
          "MailboxSearch": { "SnippetsPerEmail": "3", "Weights": {} },
          "Unset": null,
          "Servers": { "1": { "Host": "second.example.test" } }
        }
        """;

    private const string EquivalentYaml = """
        # the same document, as an operator writes it
        MailSynchronization:
          Accounts:
            - Alias: work
              Folders:
                - INBOX
                - Archive
            - Alias: family
              Folders: []
          Interval: "00:05:00"
        MailboxSearch:
          SnippetsPerEmail: 3
          Weights: {}
        Unset:
        Servers:
          "1":
            Host: second.example.test
        """;

    [Fact]
    public void Flatten_ADocumentEquivalentToAJsonOne_ProducesTheKeysTheJsonProviderDoes()
    {
        // Arrange
        var expected = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(EquivalentJson)))
            .Build()
            .AsEnumerable()
            .OrderBy(setting => setting.Key, StringComparer.Ordinal);

        // Act
        var flattened = Flatten(EquivalentYaml);

        // Assert
        var actual = new ConfigurationBuilder()
            .AddInMemoryCollection(flattened)
            .Build()
            .AsEnumerable()
            .OrderBy(setting => setting.Key, StringComparer.Ordinal);

        Assert.Equal(expected, actual);
    }

    /// <summary>A scalar is the text written, so nothing YAML 1.1 would have reinterpreted reaches the binder changed.</summary>
    [Theory]
    [InlineData("no")]
    [InlineData("on")]
    [InlineData("yes")]
    [InlineData("0x10")]
    [InlineData("0o17")]
    [InlineData("1.50")]
    [InlineData("1e3")]
    [InlineData("true")]
    [InlineData(".inf")]
    [InlineData("2026-09-17")]
    public void Flatten_APlainScalar_KeepsTheTextWritten(string written)
    {
        // Arrange
        var yaml = $"Value: {written}\n";

        // Act
        var flattened = Flatten(yaml);

        // Assert
        Assert.Equal(written, flattened["Value"]);
    }

    [Theory]
    [InlineData("Value:\n")]
    [InlineData("Value: ~\n")]
    [InlineData("Value: null\n")]
    [InlineData("Value: NULL\n")]
    public void Flatten_APlainNull_ReadsAsJsonNull(string yaml)
    {
        // Arrange
        var document = yaml;

        // Act
        var flattened = Flatten(document);

        // Assert
        Assert.True(flattened.ContainsKey("Value"));
        Assert.Null(flattened["Value"]);
    }

    [Theory]
    [InlineData("Value: 'null'\n", "null")]
    [InlineData("Value: \"\"\n", "")]
    [InlineData("Value: |\n  first\n  second\n", "first\nsecond\n")]
    public void Flatten_AQuotedOrBlockScalar_IsTextEvenWhereItSpellsNull(string yaml, string expected)
    {
        // Arrange
        var document = yaml;

        // Act
        var flattened = Flatten(document);

        // Assert
        Assert.Equal(expected, flattened["Value"]);
    }

    /// <summary>An operator who commented every setting out during a rollout has written an empty file, not a broken one.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("# nothing yet\n")]
    public void Flatten_AFileHoldingNoDocument_ProducesNoKeys(string yaml)
    {
        // Arrange
        var document = yaml;

        // Act
        var flattened = Flatten(document);

        // Assert
        Assert.Empty(flattened);
    }

    /// <summary>Everything that would let a file mean something other than what it appears to say fails, naming the position.</summary>
    [Theory]
    [InlineData("Value: [unclosed\n", "not valid YAML")]
    [InlineData("First: 1\n---\nSecond: 2\n", "more than one YAML document")]
    [InlineData("- first\n- second\n", "root is not a mapping")]
    [InlineData("just text\n", "root is not a mapping")]
    [InlineData("Value: 1\nValue: 2\n", "more than once")]
    [InlineData("Value: 1\nvalue: 2\n", "more than once")]
    [InlineData("A:\n  B: 1\nA:B: 2\n", "more than once")]
    [InlineData("Base: &shared 1\nCopy: 2\n", "anchor")]
    [InlineData("Base: 1\nCopy: *shared\n", "alias")]
    [InlineData("Value: !!str 12\n", "tag")]
    [InlineData("Value: !custom 12\n", "tag")]
    [InlineData("? [a, b]\n: 1\n", "mapping key")]
    public void Flatten_AFeatureTheReaderRefuses_FailsNamingThePosition(string yaml, string expectedReason)
    {
        // Arrange
        var document = yaml;

        // Act
        var failure = Assert.Throws<FormatException>(() => Flatten(document));

        // Assert
        Assert.Contains(expectedReason, failure.Message, StringComparison.Ordinal);
        Assert.Contains("line", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Flatten_NestingPastTheJsonReadersDepth_Fails()
    {
        // Arrange
        var yaml = string.Concat(Enumerable.Range(0, 70).Select(depth => new string(' ', depth * 2) + "Level:\n"));

        // Act
        var failure = Assert.Throws<FormatException>(() => Flatten(yaml));

        // Assert
        Assert.Contains("nests deeper", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A refused provisioned file stops the host the way a malformed JSON one does: the framework's own failure, which
    /// names the file's physical path, wrapping the refusal that names the position. The in-memory file here has no
    /// physical path, so what is asserted is the shape rather than the path.
    /// </summary>
    [Fact]
    public void Build_AProvisionedFileTheReaderRefuses_FailsAsTheFrameworkReportsAMalformedFile()
    {
        // Arrange
        const string fileName = "10-mail.yaml";
        var files = new InMemoryConfigurationFileProvider().WithFile(fileName, "Base: &shared 1\n");
        var builder = new ConfigurationBuilder().Add(new ProvisionedYamlConfigurationSource
        {
            Path = fileName,
            FileProvider = files,
            Optional = false,
        });

        // Act
        var failure = Assert.Throws<InvalidDataException>(() => builder.Build());

        // Assert
        Assert.StartsWith("Failed to load configuration from file", failure.Message, StringComparison.Ordinal);
        Assert.Contains("line 1", Assert.IsType<FormatException>(failure.InnerException).Message, StringComparison.Ordinal);
    }

    private static Dictionary<string, string?> Flatten(string yaml) =>
        YamlConfigurationDocument.Flatten(new MemoryStream(Encoding.UTF8.GetBytes(yaml)));
}
