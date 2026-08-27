// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.Host.Api;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the body route asks the read for, and what it puts on the wire.</summary>
/// <remarks>
/// The reduction itself is covered where it happens, and so is the read behind it. What is asserted here is the
/// transport: that a pane's read never asks for markup and always asks for the tree, that an absent query means the
/// same as a reader who declined, and that both renderings reach the wire together so a refused document is drawn as
/// its words rather than as an empty pane.
/// </remarks>
public sealed class ClientMailBodyEndpointTests
{
    private static readonly Guid Message = new("11111111-1111-1111-1111-111111111111");

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void MailBodyRoute_IsThePathAClientComposes() =>
        Assert.Equal("/messages/{storedEmailId:guid}/body", ClientMailBodyEndpoint.MailBodyRoute);

    /// <summary>The pane reads the tree and never the markup, which is what keeps a sanitized document off a screen that has no parser for it.</summary>
    [Fact]
    public void RequestFor_AnyRead_AsksForTheTreeAndForNeitherMarkupNorAttachmentLinks()
    {
        // Act
        var request = ClientMailBodyEndpoint.RequestFor(Message, remoteImages: null);

        // Assert
        Assert.True(request.IncludeMailDocument);
        Assert.False(request.IncludeSanitizedHtml);
        Assert.False(request.IncludeAttachmentDownloadLinks);
        Assert.Equal([StoredEmailId.Create(Message)], request.StoredEmailIds);
    }

    /// <summary>A reader who said nothing is a reader who did not ask, so an absent query withholds exactly as a refusal does.</summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void RequestFor_TheRemoteImagesQuery_IsTheWholeOfWhatRetainsThem(bool? remoteImages, bool expected)
    {
        // Act
        var request = ClientMailBodyEndpoint.RequestFor(Message, remoteImages);

        // Assert
        Assert.Equal(expected, request.RetainRemoteImageReferences);
    }

    /// <summary>Both renderings travel together, because a pane needs the words whether or not it draws the tree.</summary>
    [Fact]
    public void For_AReadableMessage_CarriesTheDocumentAndTheWordsAtOnce()
    {
        // Arrange
        var document = MailDocument.Reduced(
            [new MailParagraphBlock([new MailInlineRun("Hello", MailTextEmphasis.None, null, null)], MailBlockAlignment.Inherited)],
            removedRemoteReferenceCount: 2,
            retainedRemoteImageCount: 0,
            inlineImageCount: 0,
            undrawnInlineImageCount: 0,
            truncated: false);

        // Act
        var response = ClientMailBodyResponse.For(MessageWith(document), remoteImagesRequested: false);

        // Assert
        Assert.Equal(Message, response.StoredEmailId);
        Assert.Equal("Readable", response.Availability);
        Assert.Same(document, response.Document);
        Assert.Equal("Just words.", response.PlainText.Text);
        Assert.Equal(11, response.PlainText.OriginalCharacterCount);
        Assert.Equal("None", response.PlainText.Truncation);
        Assert.False(response.RemoteImagesRequested);
    }

    /// <summary>The read that fetched remote pictures says so, which is how a pane knows it is showing them.</summary>
    [Fact]
    public void For_AReadTheReaderAskedRemotePicturesFor_SaysSo()
    {
        // Act
        var response = ClientMailBodyResponse.For(MessageWith(document: null), remoteImagesRequested: true);

        // Assert
        Assert.True(response.RemoteImagesRequested);
    }

    /// <summary>A body nothing could read carries its state as the state's own name, and no document to draw.</summary>
    [Fact]
    public void For_ABodyNothingCouldRead_CarriesTheStateAndNoDocument()
    {
        // Arrange
        var message = new ReadEmailContent
        {
            StoredEmailId = StoredEmailId.Create(Message),
            AccountId = MailAccountId.Create("primary"),
            FolderAlias = MailFolderAlias.Create("INBOX"),
            Headers = new EmailContentHeaders(null, null, null, [], EmailThreadReferences.None),
            Body = EmailContentBody.EncryptedNotReadableLocally,
            Attachments = [],
            RemoteFlags = RemoteEmailFlagSnapshot.NeverObserved,
            SenderVerification = SenderVerification.NotEstablished,
            SenderAuthenticationEvidence = SenderAuthenticationEvidence.None,
            MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
        };

        // Act
        var response = ClientMailBodyResponse.For(message, remoteImagesRequested: false);

        // Assert
        Assert.Equal("EncryptedNotReadableLocally", response.Availability);
        Assert.Null(response.Document);
        Assert.Equal(string.Empty, response.PlainText.Text);
    }

    /// <summary>A refused document still reaches the wire, because the reason is what a pane shows instead of the message.</summary>
    [Fact]
    public void For_ARefusedDocument_CarriesTheRefusalRatherThanNothing()
    {
        // Arrange
        var refused = MailDocument.Refused(MailDocumentRefusal.NoHtmlPart);

        // Act
        var response = ClientMailBodyResponse.For(MessageWith(refused), remoteImagesRequested: false);

        // Assert
        Assert.NotNull(response.Document);
        Assert.Equal(MailDocumentRefusal.NoHtmlPart, response.Document.Refusal);
        Assert.Empty(response.Document.Blocks);
    }

    /// <summary>A representation reaches the wire with the bound that cut it, so nothing has to derive one from two lengths.</summary>
    [Fact]
    public void For_ARepresentationABoundCut_NamesTheBoundRatherThanOnlyItsLength()
    {
        // Arrange
        var representation = new EmailBodyRepresentation(
            "Just wo",
            OriginalCharacterCount: 11,
            EmailBodyTruncation.BodyCharacterLimit);

        // Act
        var response = ClientMailBodyTextResponse.For(representation);

        // Assert
        Assert.Equal("Just wo", response.Text);
        Assert.Equal(11, response.OriginalCharacterCount);
        Assert.Equal("BodyCharacterLimit", response.Truncation);
    }

    private static ReadEmailContent MessageWith(MailDocument? document) => new()
    {
        StoredEmailId = StoredEmailId.Create(Message),
        AccountId = MailAccountId.Create("primary"),
        FolderAlias = MailFolderAlias.Create("INBOX"),
        Headers = new EmailContentHeaders(null, null, null, [], EmailThreadReferences.None),
        Body = EmailContentBody.Readable(
            new EmailBodyRepresentation("Just words.", 11, EmailBodyTruncation.None),
            sanitizedHtml: null,
            document),
        Attachments = [],
        RemoteFlags = RemoteEmailFlagSnapshot.NeverObserved,
        SenderVerification = SenderVerification.NotEstablished,
        SenderAuthenticationEvidence = SenderAuthenticationEvidence.None,
        MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
    };
}
