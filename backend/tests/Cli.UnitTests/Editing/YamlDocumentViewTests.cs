// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using MailFathom.Cli.Editing;
using Xunit;

namespace MailFathom.Cli.UnitTests.Editing;

/// <summary>Covers the YAML view of a JSON document: that the round trip keeps every value's type, and what it refuses.</summary>
public sealed class YamlDocumentViewTests
{
    /// <summary>
    /// Strings that read as another type in plain YAML are the case the view exists to get right, so the document carries
    /// every one of them beside the values they would be mistaken for.
    /// </summary>
    private const string EveryKindOfValue = """
        {
          "Text": "work",
          "TextThatReadsAsTrue": "true",
          "TextThatReadsAsNull": "null",
          "TextThatReadsAsANumber": "0x10",
          "TextThatYaml11ReadAsFalse": "no",
          "EmptyText": "",
          "MultiLineText": "first line\nsecond line\n",
          "NonAsciiText": "zażółć",
          "Integer": 42,
          "Decimal": 1.50,
          "Negative": -7,
          "True": true,
          "False": false,
          "Nothing": null,
          "EmptyObject": {},
          "EmptyArray": [],
          "MailAccounts": [{ "AccountId": "work", "Ports": [993, 465] }]
        }
        """;

    [Fact]
    public void ReadBack_TheRenderedView_DescribesTheSameDocument()
    {
        // Arrange
        var rendered = YamlDocumentView.Render(EveryKindOfValue);

        // Act
        var readBack = YamlDocumentView.ReadBack(rendered);

        // Assert
        Assert.NotNull(readBack);
        Assert.True(YamlDocumentView.DescribeTheSameDocument(EveryKindOfValue, readBack));
    }

    [Fact]
    public void Render_TextThatPlainYamlWouldReadAsAnotherType_QuotesIt()
    {
        // Arrange
        const string document = """{"Enabled":"true","Limit":"10","Absent":"~"}""";

        // Act
        var rendered = YamlDocumentView.Render(document);

        // Assert
        Assert.Contains("Enabled: \"true\"", rendered, StringComparison.Ordinal);
        Assert.Contains("Limit: \"10\"", rendered, StringComparison.Ordinal);
        Assert.Contains("Absent: \"~\"", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ADocument_WritesPlainBlockYaml()
    {
        // Arrange
        const string document = """{"MailAccounts":[{"AccountId":"work","Enabled":true}]}""";

        // Act
        var rendered = YamlDocumentView.Render(document);

        // Assert
        Assert.Equal("MailAccounts:\n- AccountId: work\n  Enabled: true\n", rendered.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Render_NoDocument_LeavesTheTextAsItIs()
    {
        // Arrange
        const string document = "";

        // Act
        var rendered = YamlDocumentView.Render(document);

        // Assert
        Assert.Equal(string.Empty, rendered);
    }

    [Fact]
    public void Render_TextThatIsNotJson_FailsSayingSo()
    {
        // Arrange
        const string document = "not json";

        // Act
        var failure = Assert.Throws<CliFailure>(() => YamlDocumentView.Render(document));

        // Assert
        Assert.Contains("not JSON", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The YAML 1.2 core schema decides a plain scalar's type, and YAML 1.1's wider booleans stay text.</summary>
    [Theory]
    [InlineData("Value: work", "\"work\"")]
    [InlineData("Value: on", "\"on\"")]
    [InlineData("Value: yes", "\"yes\"")]
    [InlineData("Value: 'true'", "\"true\"")]
    [InlineData("Value: \"12\"", "\"12\"")]
    [InlineData("Value: true", "true")]
    [InlineData("Value: FALSE", "false")]
    [InlineData("Value: null", "null")]
    [InlineData("Value: ~", "null")]
    [InlineData("Value:", "null")]
    [InlineData("Value: 12", "12")]
    [InlineData("Value: +12", "12")]
    [InlineData("Value: 0o17", "15")]
    [InlineData("Value: 0x1F", "31")]
    [InlineData("Value: 1.50", "1.50")]
    [InlineData("Value: .5", "0.5")]
    [InlineData("Value: 1.", "1")]
    [InlineData("Value: -01.5e+3", "-1.5e+3")]
    [InlineData("Value: 12345678901234567890123", "12345678901234567890123")]
    public void ReadBack_APlainOrQuotedScalar_KeepsTheTypeTheCoreSchemaGivesIt(string yaml, string expectedJson)
    {
        // Arrange
        var expected = $$"""{"Value":{{expectedJson}}}""";

        // Act
        var readBack = YamlDocumentView.ReadBack(yaml);

        // Assert
        Assert.NotNull(readBack);
        Assert.Equal(expected, Compact(readBack));
    }

    [Fact]
    public void ReadBack_ALiteralBlock_ReadsItAsText()
    {
        // Arrange
        const string yaml = "Value: |\n  first\n  12\n";

        // Act
        var readBack = YamlDocumentView.ReadBack(yaml);

        // Assert
        Assert.NotNull(readBack);
        Assert.Equal("""{"Value":"first\n12\n"}""", Compact(readBack));
    }

    [Fact]
    public void ReadBack_ABufferHoldingOnlyComments_HoldsNoDocument()
    {
        // Arrange
        const string yaml = "# every setting was removed\n";

        // Act
        var readBack = YamlDocumentView.ReadBack(yaml);

        // Assert
        Assert.Null(readBack);
    }

    /// <summary>Everything YAML can say and JSON cannot is refused, naming where it was written.</summary>
    [Theory]
    [InlineData("Value: [unclosed", "not valid YAML")]
    [InlineData("First: 1\n---\nSecond: 2\n", "more than one YAML document")]
    [InlineData("Value: 1\nValue: 2\n", "more than once")]
    [InlineData("Base: &shared 1\nCopy: *shared\n", "anchor")]
    [InlineData("Copy: *shared\n", "alias")]
    [InlineData("Value: !!str 12\n", "tag")]
    [InlineData("? [a, b]\n: 1\n", "mapping key")]
    [InlineData("Value: .inf\n", "JSON has no value for")]
    [InlineData("Value: .nan\n", "JSON has no value for")]
    public void ReadBack_WhatJsonCannotSay_IsRefusedNamingThePosition(string yaml, string expectedReason)
    {
        // Arrange
        var buffer = yaml;

        // Act
        var failure = Assert.Throws<FormatException>(() => YamlDocumentView.ReadBack(buffer));

        // Assert
        Assert.Contains(expectedReason, failure.Message, StringComparison.Ordinal);
        Assert.Contains("line", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadBack_KeysDifferingOnlyByCase_AreKeptAsJsonKeepsThem()
    {
        // Arrange
        const string yaml = "Value: 1\nvalue: 2\n";

        // Act
        var readBack = YamlDocumentView.ReadBack(yaml);

        // Assert
        Assert.NotNull(readBack);
        Assert.Equal("""{"Value":1,"value":2}""", Compact(readBack));
    }

    [Fact]
    public void DescribeTheSameDocument_DocumentsLaidOutDifferently_AreTheSame()
    {
        // Arrange
        const string compact = """{"A":[1,2],"B":{"C":"d"}}""";
        const string indented = "{\n  \"A\": [ 1, 2 ],\n  \"B\": { \"C\": \"d\" }\n}";

        // Act
        var same = YamlDocumentView.DescribeTheSameDocument(compact, indented);

        // Assert
        Assert.True(same);
    }

    [Fact]
    public void DescribeTheSameDocument_AValueOfAnotherType_IsADifferentDocument()
    {
        // Arrange
        const string text = """{"Enabled":"true"}""";
        const string boolean = """{"Enabled":true}""";

        // Act
        var same = YamlDocumentView.DescribeTheSameDocument(text, boolean);

        // Assert
        Assert.False(same);
    }

    private static string Compact(string json)
    {
        using var document = JsonDocument.Parse(json);
        using MemoryStream compact = new();

        using (Utf8JsonWriter writer = new(compact, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            document.RootElement.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(compact.ToArray());
    }
}
