// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction.Attachments;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Extraction.Attachments;

/// <summary>Covers turning a passage's offset into the place inside the document it was read from.</summary>
/// <remarks>
/// This is the whole of what a citation into an attachment resolves through: the chunk carries an offset into the
/// attachment's stored text, and the boundaries turn that into the page, slide, or sheet a reader is sent to.
/// </remarks>
public sealed class AttachmentTextSegmentTests
{
    private static readonly IReadOnlyList<AttachmentTextSegment> ThreePages =
    [
        Page(1, 0),
        Page(2, 100),
        Page(3, 250),
    ];

    /// <summary>A passage beginning exactly where a page does was read from that page.</summary>
    [Fact]
    public void At_AnOffsetOnAPageBoundary_ResolvesToThatPage()
    {
        // Act
        var segment = AttachmentTextSegment.At(ThreePages, 100);

        // Assert
        Assert.Equal(2, segment?.Number);
    }

    /// <summary>
    /// A passage opening halfway down page two was read from page two. Answering with the boundary at or after it would
    /// cite page three and send a reader past the words they were looking for.
    /// </summary>
    [Fact]
    public void At_AnOffsetInsideAPage_ResolvesToThePageItStartedIn()
    {
        // Act
        var segment = AttachmentTextSegment.At(ThreePages, 180);

        // Assert
        Assert.Equal(2, segment?.Number);
    }

    /// <summary>An offset past every boundary is in the last place the document recorded.</summary>
    [Fact]
    public void At_AnOffsetPastEveryBoundary_ResolvesToTheLastPage()
    {
        // Act
        var segment = AttachmentTextSegment.At(ThreePages, 9_000);

        // Assert
        Assert.Equal(3, segment?.Number);
    }

    /// <summary>The first character of the document is the first place, which is the ordinary single-page case.</summary>
    [Fact]
    public void At_TheFirstOffset_ResolvesToTheFirstPage()
    {
        // Act
        var segment = AttachmentTextSegment.At(ThreePages, 0);

        // Assert
        Assert.Equal(1, segment?.Number);
    }

    /// <summary>
    /// A row written before boundaries existed, and one whose redaction changed the length of its text, both carry
    /// none — and a citation naming the file without a place inside it is what an honest reading of those supports.
    /// </summary>
    [Fact]
    public void At_AnAttachmentRecordingNoBoundaries_ResolvesToNoPlace()
    {
        // Act
        var segment = AttachmentTextSegment.At([], 42);

        // Assert
        Assert.Null(segment);
    }

    /// <summary>Nothing can be resolved against a list that is not there.</summary>
    [Fact]
    public void At_AMissingBoundaryList_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => AttachmentTextSegment.At(null!, 0));
    }

    private static AttachmentTextSegment Page(int number, int startOffset) =>
        new(AttachmentTextSegmentKind.Page, number, Label: null, startOffset);
}
