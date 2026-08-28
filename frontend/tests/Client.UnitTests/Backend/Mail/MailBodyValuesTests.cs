// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Mail;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Backend.Mail;

/// <summary>What the client makes of a word in the body contract that this build has not met.</summary>
/// <remarks>
/// <para>
/// A deployment and a head are updated separately, so a value the deployment writes and this build does not know is
/// ordinary rather than exceptional. The framework's own enum reader throws on one, which would cost the reader the
/// whole body — the plain text beside it included — over one word, so each of these four values is read by hand.
/// </para>
/// <para>
/// Which value the unknown word falls to is the whole decision, and it differs per enum: two of them have a default
/// that already means "the deployment said nothing", and the two that carry a judgement have a member of their own for
/// a verdict this build cannot read, because reading one as clean is the mistake that matters.
/// </para>
/// </remarks>
public sealed class MailBodyValuesTests
{
    /// <summary>An alignment this build does not know places the block the way saying nothing would.</summary>
    [Theory]
    [InlineData("\"Start\"", MailBodyAlignment.Start)]
    [InlineData("\"Justify\"", MailBodyAlignment.Justify)]
    [InlineData("\"Diagonal\"", MailBodyAlignment.Inherited)]
    [InlineData("7", MailBodyAlignment.Inherited)]
    [InlineData("null", MailBodyAlignment.Inherited)]
    public async Task ReadMailBodyAsync_AnAlignmentWord_IsReadOrFallsBackToTheReadingDirection(
        string written,
        MailBodyAlignment expected)
    {
        // Act
        var document = await DocumentOf(
            $$"""{ "type": "paragraph", "version": 1, "alignment": {{written}}, "content": [] }""");

        // Assert
        var paragraph = Assert.IsType<MailBodyParagraphBlock>(Assert.Single(document.Blocks));
        Assert.Equal(expected, paragraph.Alignment);
    }

    /// <summary>A decoration this build does not know costs the run that decoration and nothing else.</summary>
    [Theory]
    [InlineData("\"Bold, Italic\"", MailBodyEmphasis.Bold | MailBodyEmphasis.Italic)]
    [InlineData("\"Bold, Wobbly\"", MailBodyEmphasis.Bold)]
    [InlineData("\"Wobbly\"", MailBodyEmphasis.None)]
    [InlineData("\"\"", MailBodyEmphasis.None)]
    [InlineData("3", MailBodyEmphasis.None)]
    public async Task ReadMailBodyAsync_EmphasisNames_KeepsTheOnesThisBuildKnows(
        string written,
        MailBodyEmphasis expected)
    {
        // Act
        var document = await DocumentOf(
            $$"""
            { "type": "paragraph", "version": 1, "alignment": "Inherited",
              "content": [ { "text": "Hello", "emphasis": {{written}}, "foreground": null, "link": null } ] }
            """);

        // Assert
        var paragraph = Assert.IsType<MailBodyParagraphBlock>(Assert.Single(document.Blocks));
        Assert.Equal(expected, Assert.Single(paragraph.Content).Emphasis);
    }

    /// <summary>A verdict this build cannot read is not reported as one that found nothing.</summary>
    [Theory]
    [InlineData("\"None\"", MailBodyLinkDeception.None)]
    [InlineData("\"NotApplicable\"", MailBodyLinkDeception.NotApplicable)]
    [InlineData("\"DisplayedHostDiffers\"", MailBodyLinkDeception.DisplayedHostDiffers)]
    [InlineData("\"PunycodeMismatch\"", MailBodyLinkDeception.Unrecognized)]
    [InlineData("2", MailBodyLinkDeception.Unrecognized)]
    public async Task ReadMailBodyAsync_ALinkVerdict_IsReadOrTakenAsUnvouchedFor(
        string written,
        MailBodyLinkDeception expected)
    {
        // Act
        var document = await DocumentOf(
            $$"""
            { "type": "paragraph", "version": 1, "alignment": "Inherited",
              "content": [ { "text": "Hello", "emphasis": "None", "foreground": null,
                             "link": { "target": "https://example.test/a", "host": "example.test",
                                       "asciiHost": null, "deception": {{written}} } } ] }
            """);

        // Assert
        var paragraph = Assert.IsType<MailBodyParagraphBlock>(Assert.Single(document.Blocks));
        var link = Assert.Single(paragraph.Content).Link;
        Assert.NotNull(link);
        Assert.Equal(expected, link.Deception);
    }

    /// <summary>A reason this build does not know is still a reason there is no document to draw.</summary>
    [Theory]
    [InlineData("\"None\"", MailBodyRefusal.None)]
    [InlineData("\"NoHtmlPart\"", MailBodyRefusal.NoHtmlPart)]
    [InlineData("\"ReductionFailed\"", MailBodyRefusal.ReductionFailed)]
    [InlineData("\"NothingRenderable\"", MailBodyRefusal.NothingRenderable)]
    [InlineData("\"BodyTooLarge\"", MailBodyRefusal.Unrecognized)]
    [InlineData("1", MailBodyRefusal.Unrecognized)]
    public async Task ReadMailBodyAsync_ARefusalReason_IsReadOrTakenAsStillBeingOne(
        string written,
        MailBodyRefusal expected)
    {
        // Act
        var document = await DocumentOf(block: null, refusal: written);

        // Assert
        Assert.Equal(expected, document.Refusal);
    }

    /// <summary>A number is not an ordinal into a set the deployment may have reordered.</summary>
    /// <remarks>
    /// The contract publishes these as names for that reason, so reading a number as a position would be the one
    /// mistake the naming exists to rule out — a member inserted into the middle of a set would silently redraw every
    /// value after it.
    /// </remarks>
    [Fact]
    public async Task ReadMailBodyAsync_AValueWrittenAsANumber_NamesNothingRatherThanAPosition()
    {
        // Act
        var document = await DocumentOf(
            """{ "type": "paragraph", "version": 1, "alignment": 2, "content": [] }""");

        // Assert
        var paragraph = Assert.IsType<MailBodyParagraphBlock>(Assert.Single(document.Blocks));
        Assert.Equal(MailBodyAlignment.Inherited, paragraph.Alignment);
        Assert.NotEqual(MailBodyAlignment.Center, paragraph.Alignment);
    }

    private static async Task<MailBodyDocument> DocumentOf(string? block, string refusal = "\"None\"")
    {
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(BodyHolding(block, refusal)),
            cancellationToken: TestContext.Current.CancellationToken);

        var body = await harness.Client.ReadMailBodyAsync(
            Guid.Parse("8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(body.Document);

        return body.Document;
    }

    private static string BodyHolding(string? block, string refusal) =>
        $$"""
        {
          "storedEmailId": "8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1",
          "availability": "Readable",
          "plainText": { "text": "Words", "originalCharacterCount": 5, "truncation": "None" },
          "document": {
            "schemaVersion": 1, "refusal": {{refusal}}, "removedRemoteReferenceCount": 0,
            "retainedRemoteImageCount": 0, "inlineImageCount": 0, "undrawnInlineImageCount": 0,
            "truncated": false,
            "blocks": [ {{block ?? string.Empty}} ]
          },
          "remoteImagesRequested": false
        }
        """;
}
