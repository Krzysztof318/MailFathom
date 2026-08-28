// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Mail;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Backend.Mail;

/// <summary>What the client makes of one message's body, block by block.</summary>
/// <remarks>
/// The reader is hand-written rather than declared with the polymorphic attributes, and these are the behaviours that
/// bought it: a block whose identity or revision this build does not implement costs the reader that block instead of
/// the message. A deployment and a desktop head are updated separately, so meeting one is ordinary.
/// </remarks>
public sealed class MailBodyBlockJsonConverterTests
{
    private const string EveryBlock =
        """
        {
          "storedEmailId": "8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1",
          "availability": "Readable",
          "plainText": { "text": "Words", "originalCharacterCount": 5, "truncation": "None" },
          "document": {
            "schemaVersion": 1,
            "refusal": "None",
            "removedRemoteReferenceCount": 2,
            "retainedRemoteImageCount": 0,
            "inlineImageCount": 1,
            "undrawnInlineImageCount": 0,
            "truncated": false,
            "blocks": [
              { "type": "paragraph", "version": 1, "alignment": "Center",
                "content": [ { "text": "Hello", "emphasis": "Bold, Italic", "foreground": "#112233",
                               "link": { "target": "https://example.test/a", "host": "example.test",
                                         "asciiHost": null, "deception": "None" } } ] },
              { "type": "heading", "version": 1, "level": 2, "alignment": "Inherited", "content": [] },
              { "type": "list", "version": 1, "ordered": true, "items": [ { "blocks": [] } ] },
              { "type": "table", "version": 1, "columns": [ { "widthShare": 0.5 } ],
                "rows": [ { "isHeader": true, "cells": [ { "columnSpan": 1, "rowSpan": 1,
                            "alignment": "End", "background": "#ffffff", "blocks": [] } ] } ] },
              { "type": "quote", "version": 1, "depth": 2, "blocks": [] },
              { "type": "image", "version": 1, "alignment": "Start", "link": null,
                "image": { "source": "data:image/png;base64,AAAA", "alternativeText": "A logo",
                           "width": 40, "height": 20 } },
              { "type": "separator", "version": 1 },
              { "type": "preformatted", "version": 1, "text": "  spaced" }
            ]
          },
          "remoteImagesRequested": false
        }
        """;

    /// <summary>Every block the deployment publishes is read into the type the pane draws it with.</summary>
    [Fact]
    public async Task ReadMailBodyAsync_ADocumentHoldingEveryBlock_ReadsEachOneAsItsOwnType()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(_ => StubTransport.JsonResponse(EveryBlock));

        // Act
        var body = await harness.Client.ReadMailBodyAsync(
            Guid.Parse("8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1"),
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var document = body.Document;
        Assert.NotNull(document);
        Assert.Equal(
            [
                nameof(MailBodyParagraphBlock),
                nameof(MailBodyHeadingBlock),
                nameof(MailBodyListBlock),
                nameof(MailBodyTableBlock),
                nameof(MailBodyQuoteBlock),
                nameof(MailBodyImageBlock),
                nameof(MailBodySeparatorBlock),
                nameof(MailBodyPreformattedBlock),
            ],
            document.Held.Select(block => block.GetType().Name));
    }

    /// <summary>Every member of a block is read, because the pane draws one from each of them.</summary>
    [Fact]
    public async Task ReadMailBodyAsync_AParagraph_ReadsItsRunsEmphasisColourAndLink()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(_ => StubTransport.JsonResponse(EveryBlock));

        // Act
        var body = await harness.Client.ReadMailBodyAsync(
            Guid.Parse("8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1"),
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var document = body.Document;
        Assert.NotNull(document);
        var paragraph = Assert.IsType<MailBodyParagraphBlock>(document.Held[0]);
        Assert.Equal(MailBodyAlignment.Center, paragraph.Alignment);

        var run = Assert.Single(paragraph.Content);
        Assert.Equal("Hello", run.Text);
        Assert.Equal(MailBodyEmphasis.Bold | MailBodyEmphasis.Italic, run.Emphasis);
        Assert.Equal("#112233", run.Foreground);
        Assert.NotNull(run.Link);
        Assert.Equal("https://example.test/a", run.Link.Target);
        Assert.Equal("example.test", run.Link.Place);
        Assert.False(run.Link.IsWorthWarningAbout);
    }

    /// <summary>A block from a deployment ahead of this build costs the reader that block and nothing more.</summary>
    [Theory]
    [InlineData("{ \"type\": \"timeline\", \"version\": 1 }")]
    [InlineData("{ \"type\": \"paragraph\", \"version\": 2, \"content\": [], \"alignment\": \"Start\" }")]
    [InlineData("{ \"version\": 1 }")]
    [InlineData("{ \"type\": \"heading\", \"level\": \"deep\", \"version\": 1 }")]
    public async Task ReadMailBodyAsync_ABlockThisBuildCannotRead_CostsThatBlockRatherThanTheMessage(string block)
    {
        // Arrange
        var document = BodyHolding(
            block,
            "{ \"type\": \"paragraph\", \"version\": 1, \"alignment\": \"Start\", "
            + "\"content\": [ { \"text\": \"Still readable\", \"emphasis\": \"None\", "
            + "\"foreground\": null, \"link\": null } ] }");

        using var harness = await DeploymentHarness.CreateAsync(_ => StubTransport.JsonResponse(document));

        // Act
        var body = await harness.Client.ReadMailBodyAsync(
            Guid.NewGuid(),
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var read = body.Document;
        Assert.NotNull(read);
        Assert.Equal(2, read.Held.Count);
        Assert.IsType<MailBodyUnsupportedBlock>(read.Held[0]);
        Assert.Equal(
            "Still readable",
            Assert.Single(Assert.IsType<MailBodyParagraphBlock>(read.Held[1]).Content).Text);
    }

    /// <summary>The unsupported block carries what the deployment claimed, so a pane can say what it met.</summary>
    [Fact]
    public async Task ReadMailBodyAsync_ABlockOfAnIdentityThisBuildDoesNotKnow_CarriesTheIdentityAndRevision()
    {
        // Arrange
        var document = BodyHolding("{ \"type\": \"timeline\", \"version\": 3 }");
        using var harness = await DeploymentHarness.CreateAsync(_ => StubTransport.JsonResponse(document));

        // Act
        var body = await harness.Client.ReadMailBodyAsync(
            Guid.NewGuid(),
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var read = body.Document;
        Assert.NotNull(read);
        var unsupported = Assert.IsType<MailBodyUnsupportedBlock>(Assert.Single(read.Held));
        Assert.Equal("timeline", unsupported.Identity);
        Assert.Equal(3, unsupported.Version);
    }

    /// <summary>A refused document reads as its reason rather than as a document holding nothing.</summary>
    [Fact]
    public async Task ReadMailBodyAsync_ARefusedDocument_ReadsAsTheReasonAndIsNotDrawn()
    {
        // Arrange
        var document =
            """
            {
              "storedEmailId": "8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1",
              "availability": "Readable",
              "plainText": { "text": "Just words.", "originalCharacterCount": 11, "truncation": "None" },
              "document": {
                "schemaVersion": 1, "refusal": "NoHtmlPart", "removedRemoteReferenceCount": 0,
                "retainedRemoteImageCount": 0, "inlineImageCount": 0, "undrawnInlineImageCount": 0,
                "truncated": false, "blocks": []
              },
              "remoteImagesRequested": false
            }
            """;

        using var harness = await DeploymentHarness.CreateAsync(_ => StubTransport.JsonResponse(document));

        // Act
        var body = await harness.Client.ReadMailBodyAsync(
            Guid.NewGuid(),
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var read = body.Document;
        Assert.NotNull(read);
        Assert.Equal(MailBodyRefusal.NoHtmlPart, read.Refusal);
        Assert.False(read.IsDrawn);
        Assert.Equal("Just words.", body.PlainText.Text);
    }

    /// <summary>The override is a query on the request rather than anything either end keeps.</summary>
    [Theory]
    [InlineData(false, "https://mail.example/api/client/messages/8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1/body")]
    [InlineData(true, "https://mail.example/api/client/messages/8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1/body?remoteImages=true")]
    public async Task ReadMailBodyAsync_TheReadersAnswerAboutRemoteContent_TravelsOnTheRequestAlone(
        bool remoteImages,
        string expected)
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(_ => StubTransport.JsonResponse(EveryBlock));

        // Act
        await harness.Client.ReadMailBodyAsync(
            Guid.Parse("8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1"),
            remoteImages,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new Uri(expected), Assert.Single(harness.Deployment.Requests).RequestUri);
    }

    /// <summary>A message this owner does not hold is a refusal the pane reports rather than an empty body.</summary>
    [Fact]
    public async Task ReadMailBodyAsync_ADeploymentAnsweringNotFound_Fails()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        // Act, Assert
        await Assert.ThrowsAsync<DeploymentFailure>(
            async () => await harness.Client.ReadMailBodyAsync(
                Guid.NewGuid(),
                cancellationToken: TestContext.Current.CancellationToken));
    }

    private static string BodyHolding(params string[] blocks) =>
        $$"""
        {
          "storedEmailId": "8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1",
          "availability": "Readable",
          "plainText": { "text": "Words", "originalCharacterCount": 5, "truncation": "None" },
          "document": {
            "schemaVersion": 1, "refusal": "None", "removedRemoteReferenceCount": 0,
            "retainedRemoteImageCount": 0, "inlineImageCount": 0, "undrawnInlineImageCount": 0,
            "truncated": false,
            "blocks": [ {{string.Join(",\n", blocks)}} ]
          },
          "remoteImagesRequested": false
        }
        """;
}
