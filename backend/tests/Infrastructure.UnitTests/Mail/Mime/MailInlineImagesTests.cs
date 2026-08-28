// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Mail.Mime.Rendering;
using MimeKit;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime;

/// <summary>Covers the three bounds a message's own pictures are held to, and the one that sizes the answer.</summary>
/// <remarks>
/// The count and the per-picture size bound what one part costs; the total bounds what the response costs, which is the
/// number a reading pane sizes its read against. Without the last of those a message carrying the permitted count at
/// the permitted size composes an answer no client will buffer, and the reader loses the whole message — its words
/// included — rather than one photograph.
/// </remarks>
public sealed class MailInlineImagesTests
{
    /// <summary>Pictures that fit within every bound are all resolved into something nothing has to fetch.</summary>
    [Fact]
    public async Task ResolveAsync_PicturesWithinEveryBound_AreAllResolved()
    {
        // Arrange
        using var message = MessageCarrying(1000, 1000);

        // Act
        var images = await ResolvedFrom(message, maximumOctetsInTotal: 8000);

        // Assert
        Assert.Equal(2, images.ResolvedCount);
        Assert.Equal(0, images.UndrawnCount);
        Assert.StartsWith("data:image/png;base64,", images.Resolve("cid:picture1@example.test"), StringComparison.Ordinal);
    }

    /// <summary>A picture past what the document has left is reported as undrawn rather than carried anyway.</summary>
    [Fact]
    public async Task ResolveAsync_PicturePastWhatTheDocumentHasLeft_IsLeftUndrawn()
    {
        // Arrange
        using var message = MessageCarrying(1000, 1000, 1000);

        // Act
        var images = await ResolvedFrom(message, maximumOctetsInTotal: 2500);

        // Assert
        Assert.Equal(2, images.ResolvedCount);
        Assert.Equal(1, images.UndrawnCount);
        Assert.Null(images.Resolve("cid:picture2@example.test"));
    }

    /// <summary>A picture past the bound on one part is undrawn, whatever the document has left.</summary>
    [Fact]
    public async Task ResolveAsync_PicturePastThePerPictureBound_IsLeftUndrawn()
    {
        // Arrange
        using var message = MessageCarrying(4000, 500);

        // Act
        var images = await ResolvedFrom(message, maximumOctets: 1000, maximumOctetsInTotal: 100_000);

        // Assert
        Assert.Equal(1, images.ResolvedCount);
        Assert.Equal(1, images.UndrawnCount);
        Assert.Null(images.Resolve("cid:picture0@example.test"));
        Assert.NotNull(images.Resolve("cid:picture1@example.test"));
    }

    /// <summary>Once the document's total is spent, nothing further is decoded at all.</summary>
    [Fact]
    public async Task ResolveAsync_DocumentTotalAlreadySpent_DrawsNoneOfWhatFollows()
    {
        // Arrange
        using var message = MessageCarrying(1000, 100, 100);

        // Act
        var images = await ResolvedFrom(message, maximumOctetsInTotal: 1100);

        // Assert
        Assert.Equal(1, images.ResolvedCount);
        Assert.Equal(2, images.UndrawnCount);
    }

    private static Task<MailInlineImages> ResolvedFrom(
        MimeMessage message,
        int maximumImages = 64,
        int maximumOctets = 1024 * 1024,
        int maximumOctetsInTotal = 4 * 1024 * 1024) =>
        MailInlineImages.ResolveAsync(
            message,
            maximumImages,
            maximumOctets,
            maximumOctetsInTotal,
            TestContext.Current.CancellationToken);

    /// <summary>Builds a message carrying one picture per size given, each referenced by its own identifier.</summary>
    private static MimeMessage MessageCarrying(params int[] octets)
    {
        var related = new Multipart("related");

        for (var index = 0; index < octets.Length; index++)
        {
            related.Add(new MimePart("image", "png")
            {
                ContentId = $"picture{index}@example.test",
                Content = new MimeContent(new MemoryStream(new byte[octets[index]])),
            });
        }

        return new MimeMessage { Body = related };
    }
}
