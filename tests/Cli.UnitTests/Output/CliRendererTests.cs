// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Output;
using Xunit;

namespace MailFathom.Cli.UnitTests.Output;

/// <summary>Covers the one place that decides how a command's output is drawn.</summary>
/// <remarks>
/// The assertions are on whole drawings rather than on the shapes handed in, because a drawing is the only thing an
/// operator sees: a listing whose columns were laid out wrongly carries exactly the cells the command asked for.
/// </remarks>
public sealed class CliRendererTests
{
    [Fact]
    public void Write_Listing_SetsEveryValueUnderItsHeading()
    {
        // Arrange
        StringWriter writer = new();
        CliRenderer renderer = new(writer, Redirected);
        CliTable listing = new("Rule", "Applies to");
        listing.AddRow("archive-newsletters", "personal");
        listing.AddRow("flag-invoices", "work");

        // Act
        renderer.Write(listing);

        // Assert
        Assert.Equal(
            [
                "Rule                 Applies to",
                "archive-newsletters  personal",
                "flag-invoices        work",
            ],
            Lines(writer));
    }

    [Fact]
    public void Write_Record_SetsEveryValueBesideItsLabelAndRepeatsNoLabel()
    {
        // Arrange
        StringWriter writer = new();
        CliRenderer renderer = new(writer, Redirected);
        CliDetails details = new();
        details.Add("Contact", "a-contact");
        details.Add("Addresses", ["one@example.test", "two@example.test"]);

        // Act
        renderer.Write(details);

        // Assert
        Assert.Equal(
            [
                "Contact:    a-contact",
                "Addresses:  one@example.test",
                "            two@example.test",
            ],
            Lines(writer));
    }

    /// <summary>Proves the promise a redirected run is given, at every emphasis rather than only the ordinary one.</summary>
    /// <remarks>
    /// Every emphasis in one test rather than a theory per value, because the enum is internal and a theory would have
    /// to carry it across this class's public signature.
    /// </remarks>
    [Fact]
    public void WriteLine_StreamThatPermitsNoColour_WritesNoEscapeSequenceAtAnyEmphasis()
    {
        // Arrange
        StringWriter writer = new();
        CliRenderer renderer = new(writer, Redirected);

        // Act
        renderer.WriteLine("Signed in.", CliEmphasis.None);
        renderer.WriteLine("This connection is unprotected.", CliEmphasis.Caution);
        renderer.WriteLine("Nothing was erased.", CliEmphasis.Failure);

        // Assert
        Assert.Equal(
            ["Signed in.", "This connection is unprotected.", "Nothing was erased."],
            Lines(writer));
    }

    [Fact]
    public void Write_ListingOnStreamThatPermitsNoColour_WritesNoEscapeSequence()
    {
        // Arrange
        StringWriter writer = new();
        CliRenderer renderer = new(writer, Redirected);
        CliTable listing = new("Profile", "Endpoint");
        listing.AddRow("a-profile", "https://deployment.example.test");

        // Act
        renderer.Write(listing);

        // Assert
        Assert.DoesNotContain(EscapeSequence, writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WriteLine_FailureOnAStreamThatPermitsColour_MarksTheLine()
    {
        // Arrange
        StringWriter writer = new();
        CliRenderer renderer = new(writer, Coloured);

        // Act
        renderer.WriteLine("Nothing was erased.", CliEmphasis.Failure);

        // Assert
        Assert.Contains(EscapeSequence, writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Nothing was erased.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WriteLine_CautionOnAStreamThatPermitsColour_MarksTheLine()
    {
        // Arrange
        StringWriter writer = new();
        CliRenderer renderer = new(writer, Coloured);

        // Act
        renderer.WriteLine("This connection is unprotected.", CliEmphasis.Caution);

        // Assert
        Assert.Contains(EscapeSequence, writer.ToString(), StringComparison.Ordinal);
        Assert.Contains(
            "This connection is unprotected.",
            writer.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void WriteLine_OrdinaryLineOnAStreamThatPermitsColour_MarksNothing()
    {
        // Arrange
        StringWriter writer = new();
        CliRenderer renderer = new(writer, Coloured);

        // Act
        renderer.WriteLine("Signed in to https://deployment.example.test.", CliEmphasis.None);

        // Assert
        Assert.Equal(["Signed in to https://deployment.example.test."], Lines(writer));
    }

    [Fact]
    public void WriteLine_EmptyMessage_WritesOneEmptyLine()
    {
        // Arrange
        StringWriter writer = new();
        CliRenderer renderer = new(writer, Redirected);

        // Act
        renderer.WriteLine(string.Empty, CliEmphasis.None);

        // Assert
        Assert.Equal([string.Empty], Lines(writer));
    }

    /// <summary>Proves a label the deployment reported nothing under still names itself rather than vanishing.</summary>
    /// <remarks>
    /// A record whose label is dropped where its value is empty reads as a deployment that was not asked, which is a
    /// different answer from one that was asked and reported nothing.
    /// </remarks>
    [Fact]
    public void Write_LabelCarryingNoValue_StillStatesTheLabel()
    {
        // Arrange
        StringWriter writer = new();
        CliRenderer renderer = new(writer, Redirected);
        CliDetails details = new();
        details.Add("Rewound", []);
        details.Add("Scope", "every folder under work");

        // Act
        renderer.Write(details);

        // Assert
        Assert.Equal(["Rewound:", "Scope:    every folder under work"], Lines(writer));
    }

    /// <summary>Proves that a value carrying the drawing library's own syntax reaches the operator as they wrote it.</summary>
    /// <remarks>
    /// A rule name, a folder, or a failure message is operator-supplied text, and a square bracket in one would be
    /// markup to a renderer that parsed its content. Nothing here does, and this is what says so.
    /// </remarks>
    [Fact]
    public void Write_ValueHoldingMarkup_DrawsItLiterally()
    {
        // Arrange
        StringWriter writer = new();
        CliRenderer renderer = new(writer, Redirected);
        CliTable listing = new("Rule");
        listing.AddRow("[red]archive[/]");

        // Act
        renderer.Write(listing);

        // Assert
        Assert.Equal(["Rule", "[red]archive[/]"], Lines(writer));
    }

    /// <summary>Proves a value too long for any terminal reaches the operator whole rather than folded.</summary>
    /// <remarks>
    /// A refresh token and an authorization URL are both written as ordinary lines, and both can be longer than a
    /// terminal is wide. A drawing that folded one would put a newline inside a value an operator is told to copy, so
    /// what this asserts is that no width the layout could be sized to is ever reached.
    /// </remarks>
    [Fact]
    public void WriteLine_ValueLongerThanAnyTerminal_WritesItOnOneLine()
    {
        // Arrange
        StringWriter writer = new();
        CliRenderer renderer = new(writer, Redirected);
        var token = new string('t', 4096);

        // Act
        renderer.WriteLine(token, CliEmphasis.None);

        // Assert
        Assert.Equal([token], Lines(writer));
    }

    /// <summary>Proves the same of a value inside a listing, where the layout has a second reason to break one.</summary>
    [Fact]
    public void Write_CellLongerThanAnyTerminal_KeepsTheRowOnOneLine()
    {
        // Arrange
        StringWriter writer = new();
        CliRenderer renderer = new(writer, Redirected);
        var address = new string('a', 4096);
        CliTable listing = new("Endpoint");
        listing.AddRow(address);

        // Act
        renderer.Write(listing);

        // Assert
        Assert.Equal(["Endpoint", address], Lines(writer));
    }

    /// <summary>What a marked line opens with, and the whole of what a stream permitting no colour must never carry.</summary>
    private const string EscapeSequence = "\u001b";

    private static CliTerminal Redirected => new(PermitsColour: false);

    private static CliTerminal Coloured => new(PermitsColour: true);

    private static IReadOnlyList<string> Lines(StringWriter writer) =>
        [.. writer.ToString().Split('\n').SkipLast(1).Select(line => line.TrimEnd('\r'))];
}
