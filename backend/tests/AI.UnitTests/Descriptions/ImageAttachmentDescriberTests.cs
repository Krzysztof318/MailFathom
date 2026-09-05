// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Binary;
using System.Text;
using MailFathom.AI.Descriptions;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Chat;
using MailFathom.Application.Emails.Extraction.Images;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailFathom.AI.UnitTests.Descriptions;

/// <summary>Covers the order an image attachment's refusals are taken in, which is the whole of what stands between a stranger's octets and a provider.</summary>
public sealed class ImageAttachmentDescriberTests
{
    /// <summary>Text the script keys on, which every conversation this describer composes carries in its last turn.</summary>
    private const string RequestMarker = "Describe this attached image";

    private const int OctetCeiling = 4096;

    private const long PixelCeiling = 40_000_000;

    public static TheoryData<ChatGenerationFailure, ImageDescriptionRefusal> ProviderFailures => new()
    {
        { ChatGenerationFailure.RequestTimedOut, ImageDescriptionRefusal.ProviderTimedOut },
        { ChatGenerationFailure.RateLimited, ImageDescriptionRefusal.ProviderUnavailable },
        { ChatGenerationFailure.TransportFaulted, ImageDescriptionRefusal.ProviderUnavailable },
        { ChatGenerationFailure.CredentialRejected, ImageDescriptionRefusal.ProviderRefused },
        { ChatGenerationFailure.RequestRefused, ImageDescriptionRefusal.ProviderRefused },
        { ChatGenerationFailure.AnswerEmpty, ImageDescriptionRefusal.ProviderRefused },
    };

    public static TheoryData<string, byte[], ImageDescriptionRefusal> RefusedAttachments => new()
    {
        // A file whose header declares a grid past the ceiling: forty octets on disk, ten billion pixels to allocate.
        { "image/png", Png(width: 100_000, height: 100_000), ImageDescriptionRefusal.PixelGridTooLarge },

        // One octet past what a request may carry, refused before the format is even looked at.
        { "image/png", new byte[OctetCeiling + 1], ImageDescriptionRefusal.ImageTooLarge },

        // A PNG signature with nothing behind it.
        { "image/png", Png(width: 8, height: 8)[..12], ImageDescriptionRefusal.ImageUnreadable },

        // Nothing at all, which is an unreadable image rather than an unrecognized format.
        { "image/png", [], ImageDescriptionRefusal.ImageUnreadable },

        // An SVG the part declared honestly.
        { "image/svg+xml", Utf8("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"), ImageDescriptionRefusal.FormatExcluded },

        // An SVG whose declaration says otherwise, which is why the octets decide.
        { "image/png", Utf8("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"), ImageDescriptionRefusal.FormatExcluded },

        // A raster format nothing here reads.
        { "image/bmp", [0x42, 0x4D, 0xDA, 0x08, 0x00, 0x00], ImageDescriptionRefusal.FormatNotSupported },
    };

    /// <summary>A picture inside every bound reaches the model behind the instruction, carrying the media type its octets are in.</summary>
    [Fact]
    public async Task DescribeAsync_APictureInsideEveryBound_SendsItBehindTheInstructionAndReturnsTheDescription()
    {
        // Arrange
        var provider = new ScriptedChatModelClient()
            .Answering(RequestMarker, "  A whiteboard covered in a roof plan.  ");
        var describer = Describer(provider);
        using var content = new MemoryStream(Png(width: 640, height: 480));

        // Act
        var description = await describer.DescribeAsync("image/png", content, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(description.Refusal);
        Assert.Equal("A whiteboard covered in a roof plan.", description.Text);

        var conversation = Assert.Single(provider.Conversations);

        Assert.Equal(ChatRole.System, conversation[0].Role);
        Assert.Equal(ImageDescriptionInstructions.Text, conversation[0].Text);
        Assert.Null(conversation[0].Image);

        var turn = conversation[1];

        Assert.Equal(ChatRole.User, turn.Role);
        Assert.NotNull(turn.Image);
        Assert.Equal("image/png", turn.Image.MediaType);
        Assert.Equal(Png(width: 640, height: 480), turn.Image.Content.ToArray());
    }

    /// <summary>The media type sent is read from the octets, because the one the part declared belongs to whoever composed the mail.</summary>
    [Fact]
    public async Task DescribeAsync_APartDeclaringOneFormatAndCarryingAnother_SendsWhatTheOctetsAre()
    {
        // Arrange
        var provider = new ScriptedChatModelClient().Answering(RequestMarker, "A red square.");
        var describer = Describer(provider);
        using var content = new MemoryStream(Gif(width: 16, height: 16));

        // Act
        await describer.DescribeAsync("image/jpeg", content, TestContext.Current.CancellationToken);

        // Assert
        var turn = Assert.Single(provider.Conversations)[1];

        Assert.NotNull(turn.Image);
        Assert.Equal("image/gif", turn.Image.MediaType);
    }

    /// <summary>Each way an attachment is refused produces its own reason, and none of them reaches the provider.</summary>
    [Theory]
    [MemberData(nameof(RefusedAttachments))]
    public async Task DescribeAsync_AnAttachmentRefusedBeforeTheProvider_NamesItsOwnReasonAndSendsNothing(
        string declaredMediaType,
        byte[] octets,
        ImageDescriptionRefusal expected)
    {
        // Arrange
        var provider = new ScriptedChatModelClient().AnsweringEverythingElse("nothing should reach this");
        var describer = Describer(provider);
        using var content = new MemoryStream(octets);

        // Act
        var description = await describer.DescribeAsync(
            declaredMediaType,
            content,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(description.Text);
        Assert.Equal(expected, description.Refusal);
        Assert.Equal(0, provider.CallCount);
    }

    /// <summary>A part declaring itself an SVG is refused without its octets being read at all, so the exclusion costs nothing and touches nothing.</summary>
    [Fact]
    public async Task DescribeAsync_APartDeclaringItselfAnSvg_LeavesTheOctetsUnread()
    {
        // Arrange
        var describer = Describer(new ScriptedChatModelClient());
        using var content = new MemoryStream(Png(width: 8, height: 8));

        // Act
        var description = await describer.DescribeAsync(
            "image/svg+xml; charset=utf-8",
            content,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ImageDescriptionRefusal.FormatExcluded, description.Refusal);
        Assert.Equal(0, content.Position);
    }

    /// <summary>Each way the provider ends a call becomes the reason a caller acts on, at the three granularities a caller has.</summary>
    [Theory]
    [MemberData(nameof(ProviderFailures))]
    public async Task DescribeAsync_AProviderThatDidNotAnswer_NamesWhatKindOfFailureItWas(
        ChatGenerationFailure failure,
        ImageDescriptionRefusal expected)
    {
        // Arrange
        var provider = new ScriptedChatModelClient().Failing(RequestMarker, failure);
        var describer = Describer(provider);
        using var content = new MemoryStream(Png(width: 8, height: 8));

        // Act
        var description = await describer.DescribeAsync("image/png", content, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(description.Text);
        Assert.Equal(expected, description.Refusal);
    }

    /// <summary>An answer of nothing but whitespace is a provider that produced no description rather than a description that is blank.</summary>
    [Fact]
    public async Task DescribeAsync_AProviderAnsweringNothingButWhitespace_IsRefusedRatherThanDescribed()
    {
        // Arrange
        var provider = new ScriptedChatModelClient().Answering(RequestMarker, "   ");
        var describer = Describer(provider);
        using var content = new MemoryStream(Png(width: 8, height: 8));

        // Act
        var description = await describer.DescribeAsync("image/png", content, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(description.Text);
        Assert.Equal(ImageDescriptionRefusal.ProviderRefused, description.Refusal);
    }

    /// <summary>A generation the provider's filter withheld is a refusal, because what survives it is a fragment rather than a short description.</summary>
    [Fact]
    public async Task DescribeAsync_AProviderThatWithheldTheGeneration_IsRefusedRatherThanDescribed()
    {
        // Arrange
        var provider = new ScriptedChatModelClient()
            .Answering(RequestMarker, "The document begins", ChatGenerationStop.ContentFiltered);
        var describer = Describer(provider);
        using var content = new MemoryStream(Png(width: 8, height: 8));

        // Act
        var description = await describer.DescribeAsync("image/png", content, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(description.Text);
        Assert.Equal(ImageDescriptionRefusal.ProviderRefused, description.Refusal);
    }

    /// <summary>A transcription the output budget cut short is kept, because the prefix is real text somebody can search for.</summary>
    /// <remarks>The remedy is the operator's output budget rather than anything about the picture, which is why this is not one of the refusals.</remarks>
    [Fact]
    public async Task DescribeAsync_ATranscriptionTheOutputBudgetCutShort_IsKeptRatherThanRefused()
    {
        // Arrange
        var provider = new ScriptedChatModelClient()
            .Answering(RequestMarker, "Invoice 4471, dated", ChatGenerationStop.OutputLimitReached);
        var describer = Describer(provider);
        using var content = new MemoryStream(Png(width: 8, height: 8));

        // Act
        var description = await describer.DescribeAsync("image/png", content, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Invoice 4471, dated", description.Text);
        Assert.Null(description.Refusal);
    }

    /// <summary>The caller stopping the work is not something the attachment or the provider did, so it is never recorded against either.</summary>
    [Fact]
    public async Task DescribeAsync_ACallerThatCancelled_IsNotReportedAsARefusal()
    {
        // Arrange
        var describer = Describer(new ScriptedChatModelClient().AnsweringEverythingElse("a picture"));
        using var content = new MemoryStream(Png(width: 8, height: 8));
        using var cancellation = new CancellationTokenSource();

        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => describer.DescribeAsync("image/png", content, cancellation.Token));
    }

    /// <summary>An instance whose operator has not turned description on refuses everything and reads nothing.</summary>
    [Fact]
    public async Task DescribeAsync_ADeploymentThatHasNotActivatedDescription_RefusesWithoutReadingTheOctets()
    {
        // Arrange
        using var content = new MemoryStream(Png(width: 8, height: 8));

        // Act
        var description = await InactiveImageAttachmentDescriber.Instance.DescribeAsync(
            "image/png",
            content,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(description.Text);
        Assert.Equal(ImageDescriptionRefusal.NotActivated, description.Refusal);
        Assert.Equal(0, content.Position);
    }

    private static ImageAttachmentDescriber Describer(ScriptedChatModelClient provider) =>
        new(
            provider,
            ChatDeclarations.PlanSource(ChatDeclarations.Plan(maximumRequestImageOctets: OctetCeiling)),
            PixelCeiling,
            NullLogger<ImageAttachmentDescriber>.Instance);

    private static byte[] Utf8(string document) => Encoding.UTF8.GetBytes(document);

    /// <summary>The smallest PNG carrying a readable header, which is all anything on this path ever reads.</summary>
    private static byte[] Png(int width, int height)
    {
        var file = new byte[24];

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(file);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(8), 13);
        "IHDR"u8.CopyTo(file.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(20), (uint)height);

        return file;
    }

    /// <summary>The smallest GIF carrying a readable logical screen descriptor.</summary>
    private static byte[] Gif(int width, int height)
    {
        var file = new byte[13];

        "GIF89a"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(6), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(8), (ushort)height);

        return file;
    }
}
