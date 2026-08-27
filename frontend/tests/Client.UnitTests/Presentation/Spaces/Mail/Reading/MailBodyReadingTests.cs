// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Mail;
using MailFathom.Client.Presentation.Spaces.Mail.Reading;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation.Spaces.Mail.Reading;

/// <summary>What a pane draws for one message, decided off the visual tree so a test can read it directly.</summary>
/// <remarks>
/// Every decision the reading pane takes about a body is here rather than in the control: whether the document is
/// drawn, why it is not, and what the reader is told about what was left out. A control that decided any of them would
/// need a window to assert against.
/// </remarks>
public sealed class MailBodyReadingTests
{
    private static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
    {
        ["MailBody.Block.Unsupported"] = "A part this build cannot draw",
        ["MailBody.Image.Undrawn"] = "A picture that could not be shown",
        ["MailBody.Link.Title"] = "Follow this link?",
        ["MailBody.Link.DisplayText"] = "The message shows",
        ["MailBody.Link.Target"] = "It actually goes to",
        ["MailBody.Link.Punycode"] = "The same address in ASCII",
        ["MailBody.Link.Deception"] = "The words name a different site.",
        ["MailBody.Link.Open"] = "Open in browser",
        ["MailBody.Link.Cancel"] = "Stay here",
        ["MailBody.Refusal.NoHtmlPart"] = "Written as plain text.",
        ["MailBody.Refusal.ReductionFailed"] = "Could not be read.",
        ["MailBody.Refusal.NothingRenderable"] = "Held nothing to show.",
        ["MailBody.Availability.EncryptedNotReadableLocally"] = "Arrived encrypted.",
        ["MailBody.Availability.NotStoredExceededSizeLimit"] = "Larger than this deployment stores.",
        ["MailBody.Availability.NotStoredAwaitingStorageHeadroom"] = "No room yet.",
        ["MailBody.Availability.Unrecognized"] = "A state this version does not know.",
        ["MailBody.Notice.RemoteContent.Message"] = "References removed: {0}.",
        ["MailBody.Notice.RemoteContentShown.Message"] = "Loaded from other servers: {0}.",
        ["MailBody.Notice.UndrawnImages.Message"] = "Pictures left out: {0}.",
    };

    /// <summary>A pane nothing has been opened in is its own state rather than an empty message.</summary>
    [Fact]
    public void Nothing_APaneWithNoMessageOpen_IsNeitherDrawnNorRead()
    {
        // Act
        var reading = MailBodyReading.Nothing(Localizer());

        // Assert
        Assert.False(reading.IsOpen);
        Assert.False(reading.DrawsDocument);
        Assert.False(reading.ShowsPlainText);
        Assert.False(reading.HasReason);
        Assert.Empty(reading.Blocks);
    }

    /// <summary>A readable document is drawn, and nothing is said about why it would not be.</summary>
    [Fact]
    public void Of_AReadableDocument_IsDrawnWithNoReasonToShow()
    {
        // Arrange
        var body = BodyWith(Document() with { Blocks = [new MailBodySeparatorBlock()] });

        // Act
        var reading = MailBodyReading.Of(body, Localizer());

        // Assert
        Assert.True(reading.IsOpen);
        Assert.True(reading.DrawsDocument);
        Assert.False(reading.ShowsPlainText);
        Assert.False(reading.HasReason);
        Assert.Single(reading.Blocks);
    }

    /// <summary>A refused document is read as words with the reason above them, never as words alone.</summary>
    [Theory]
    [InlineData(MailBodyRefusal.NoHtmlPart, "Written as plain text.")]
    [InlineData(MailBodyRefusal.ReductionFailed, "Could not be read.")]
    [InlineData(MailBodyRefusal.NothingRenderable, "Held nothing to show.")]
    public void Of_ARefusedDocument_IsReadAsWordsWithTheReasonBesideThem(MailBodyRefusal refusal, string expected)
    {
        // Arrange
        var body = BodyWith(Document() with { Refusal = refusal });

        // Act
        var reading = MailBodyReading.Of(body, Localizer());

        // Assert
        Assert.False(reading.DrawsDocument);
        Assert.True(reading.ShowsPlainText);
        Assert.Equal(expected, reading.Reason);
        Assert.Equal("Just words.", reading.PlainText);
    }

    /// <summary>A document claiming nothing was refused but holding nothing is still nothing to draw.</summary>
    [Fact]
    public void Of_ADocumentRefusingNothingAndHoldingNothing_IsReadAsHavingNothingToDraw()
    {
        // Arrange
        var body = BodyWith(Document());

        // Act
        var reading = MailBodyReading.Of(body, Localizer());

        // Assert
        Assert.False(reading.DrawsDocument);
        Assert.Equal("Held nothing to show.", reading.Reason);
    }

    /// <summary>A body that could not be read at all says which of those states it was in.</summary>
    [Theory]
    [InlineData("EncryptedNotReadableLocally", "Arrived encrypted.")]
    [InlineData("NotStoredExceededSizeLimit", "Larger than this deployment stores.")]
    [InlineData("NotStoredAwaitingStorageHeadroom", "No room yet.")]
    public void Of_ABodyThatCouldNotBeRead_SaysWhichStateItWasIn(string availability, string expected)
    {
        // Arrange
        var body = BodyWith(document: null) with { Availability = availability };

        // Act
        var reading = MailBodyReading.Of(body, Localizer());

        // Assert
        Assert.Equal(expected, reading.Reason);
    }

    /// <summary>A state named by a deployment ahead of this build claims nothing rather than reading as the key.</summary>
    [Fact]
    public void Of_AStateThisBuildDoesNotKnow_ClaimsNothingAboutTheMessage()
    {
        // Arrange
        var body = BodyWith(document: null) with { Availability = "QuarantinedPendingReview" };

        // Act
        var reading = MailBodyReading.Of(body, Localizer());

        // Assert
        Assert.Equal("A state this version does not know.", reading.Reason);
    }

    /// <summary>What was withheld is counted for the reader, which is what offers them the choice.</summary>
    [Fact]
    public void Of_ADocumentThatAskedToLoadFromSomebodyElsesServer_SaysHowMuchWasWithheld()
    {
        // Arrange
        var body = BodyWith(Document() with
        {
            Blocks = [new MailBodySeparatorBlock()],
            RemovedRemoteReferenceCount = 3,
        });

        // Act
        var reading = MailBodyReading.Of(body, Localizer());

        // Assert
        Assert.True(reading.WithholdsRemoteContent);
        Assert.Equal("References removed: 3.", reading.WithheldRemoteContent);
        Assert.False(reading.ShowsRemoteContent);
    }

    /// <summary>A read the reader asked remote pictures for says what it fetched rather than leaving it to be inferred.</summary>
    [Fact]
    public void Of_AReadTheReaderAskedRemotePicturesFor_SaysWhatItFetched()
    {
        // Arrange
        var body = BodyWith(Document() with
        {
            Blocks = [new MailBodySeparatorBlock()],
            RetainedRemoteImageCount = 2,
        }) with
        {
            RemoteImagesRequested = true,
        };

        // Act
        var reading = MailBodyReading.Of(body, Localizer());

        // Assert
        Assert.True(reading.ShowsRemoteContent);
        Assert.Equal("Loaded from other servers: 2.", reading.ShownRemoteContent);
    }

    /// <summary>A retained count on a read nobody asked for says nothing, because nothing was asked for.</summary>
    [Fact]
    public void Of_ARetainedCountOnAReadNobodyAskedFor_SaysNothingAboutRemoteContent()
    {
        // Arrange
        var body = BodyWith(Document() with
        {
            Blocks = [new MailBodySeparatorBlock()],
            RetainedRemoteImageCount = 2,
        });

        // Act
        var reading = MailBodyReading.Of(body, Localizer());

        // Assert
        Assert.False(reading.ShowsRemoteContent);
    }

    /// <summary>Either rendering being cut short is one thing to say, because a reader acts the same way on both.</summary>
    [Theory]
    [InlineData("Characters", false, true)]
    [InlineData("None", true, true)]
    [InlineData("None", false, false)]
    public void Of_ARenderingCutShort_SaysSoWhicheverBoundDidIt(
        string truncation,
        bool documentTruncated,
        bool expected)
    {
        // Arrange
        var body = BodyWith(Document() with
        {
            Blocks = [new MailBodySeparatorBlock()],
            Truncated = documentTruncated,
        }) with
        {
            PlainText = new DeploymentMailBodyText("Just words.", 11, truncation),
        };

        // Act
        var reading = MailBodyReading.Of(body, Localizer());

        // Assert
        Assert.Equal(expected, reading.WasTruncated);
    }

    /// <summary>Pictures the message carried but nothing drew are counted rather than silently absent.</summary>
    [Fact]
    public void Of_PicturesLeftUndrawn_AreCountedForTheReader()
    {
        // Arrange
        var body = BodyWith(Document() with
        {
            Blocks = [new MailBodySeparatorBlock()],
            UndrawnInlineImageCount = 4,
        });

        // Act
        var reading = MailBodyReading.Of(body, Localizer());

        // Assert
        Assert.True(reading.HasUndrawnImages);
        Assert.Equal("Pictures left out: 4.", reading.UndrawnImages);
    }

    /// <summary>A newer revision of the contract is a notice rather than a refusal, so the message is still drawn.</summary>
    [Fact]
    public void Of_ADeploymentWritingANewerRevision_IsSaidWithoutRefusingTheMessage()
    {
        // Arrange
        var body = BodyWith(Document() with
        {
            SchemaVersion = MailBodyDocument.ImplementedSchemaVersion + 1,
            Blocks = [new MailBodySeparatorBlock()],
        });

        // Act
        var reading = MailBodyReading.Of(body, Localizer());

        // Assert
        Assert.True(reading.DeploymentAhead);
        Assert.True(reading.DrawsDocument);
        Assert.False(reading.HasReason);
    }

    /// <summary>The sentences the drawing composes travel with the reading, so the control resolves none itself.</summary>
    [Fact]
    public void Of_AnyBody_CarriesTheSentencesTheDrawingNeeds()
    {
        // Act
        var reading = MailBodyReading.Of(BodyWith(Document()), Localizer());

        // Assert
        Assert.Equal("A part this build cannot draw", reading.Words.UnsupportedBlock);
        Assert.Equal("Follow this link?", reading.Words.LinkTitle);
        Assert.Equal("Stay here", reading.Words.LinkCancel);
    }

    private static StubStringLocalizer Localizer() => new(Words);

    private static MailBodyDocument Document() => new(
        MailBodyDocument.ImplementedSchemaVersion,
        [],
        MailBodyRefusal.None,
        RemovedRemoteReferenceCount: 0,
        RetainedRemoteImageCount: 0,
        InlineImageCount: 0,
        UndrawnInlineImageCount: 0,
        Truncated: false);

    private static DeploymentMailBody BodyWith(MailBodyDocument? document) => new(
        Guid.Parse("8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1"),
        "Readable",
        new DeploymentMailBodyText("Just words.", 11, "None"),
        document,
        RemoteImagesRequested: false);
}
