// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Citations;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Host.Api;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the citation route accepts off the wire and what it publishes back.</summary>
/// <remarks>
/// Following a citation is covered where it happens. What is asserted here is the transport: that the route reads back
/// exactly the three citation targets a presentation plan publishes and nothing else, and that a resolution reaches a
/// client with the passage, the message, and the state a reader is shown — including the message behind a place that
/// is no longer there.
/// </remarks>
public sealed class ClientCitationEndpointTests
{
    private static readonly Guid Message = new("22222222-2222-2222-2222-222222222222");

    private static readonly Guid Passage = new("44444444-4444-4444-4444-444444444444");

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void CitationResolutionRoute_IsThePathAClientComposes() =>
        Assert.Equal("/citations/resolution", ClientCitationEndpoint.CitationResolutionRoute);

    /// <summary>A client posts back the citation target the plan published, so each of the three kinds reads back as itself.</summary>
    [Fact]
    public void TargetOf_EachKindThePlanPublishes_ReadsBackAsThatTarget()
    {
        // Act
        var email = ClientCitationEndpoint.TargetOf(new ClientCitationRequest("email", Message, null, null));
        var fragment = ClientCitationEndpoint.TargetOf(new ClientCitationRequest("fragment", Message, Passage, null));
        var attachment = ClientCitationEndpoint.TargetOf(new ClientCitationRequest("attachment", Message, null, 2));

        // Assert
        Assert.Equal(new EmailCitationTarget(StoredEmailId.Create(Message)), email);
        Assert.Equal(new FragmentCitationTarget(StoredEmailId.Create(Message), EmailChunkId.Create(Passage)), fragment);
        Assert.Equal(new AttachmentCitationTarget(StoredEmailId.Create(Message), 2), attachment);
    }

    /// <summary>
    /// A body naming a kind it does not carry the members of, or an identity nothing could name, is not a citation any
    /// plan declared — so it names no target rather than resolving to the message alone.
    /// </summary>
    [Theory]
    [InlineData(null, "22222222-2222-2222-2222-222222222222", null, null)]
    [InlineData("passage", "22222222-2222-2222-2222-222222222222", null, null)]
    [InlineData("email", "00000000-0000-0000-0000-000000000000", null, null)]
    [InlineData("email", "22222222-2222-2222-2222-222222222222", "44444444-4444-4444-4444-444444444444", null)]
    [InlineData("fragment", "22222222-2222-2222-2222-222222222222", null, null)]
    [InlineData("fragment", "22222222-2222-2222-2222-222222222222", "00000000-0000-0000-0000-000000000000", null)]
    [InlineData("fragment", "22222222-2222-2222-2222-222222222222", "44444444-4444-4444-4444-444444444444", 0)]
    [InlineData("attachment", "22222222-2222-2222-2222-222222222222", null, null)]
    [InlineData("attachment", "22222222-2222-2222-2222-222222222222", null, -1)]
    public void TargetOf_ABodyNoPlanCouldHavePublished_NamesNoTarget(
        string? kind,
        string email,
        string? fragment,
        int? attachmentPosition)
    {
        // Act
        var target = ClientCitationEndpoint.TargetOf(new ClientCitationRequest(
            kind,
            Guid.Parse(email),
            fragment is null ? null : Guid.Parse(fragment),
            attachmentPosition));

        // Assert
        Assert.Null(target);
    }

    /// <summary>
    /// A JSON array writes an entry as nothing at all whatever the list is declared to hold, so the citation that
    /// arrives is a document no plan published and takes the same refusal as any other.
    /// </summary>
    [Fact]
    public void TargetOf_ACitationTheBodyWroteAsNothing_NamesNoTarget() =>
        Assert.Null(ClientCitationEndpoint.TargetOf(null));

    /// <summary>A citation nobody may read carries the identity the caller already held and nothing whatever about the mail.</summary>
    [Fact]
    public void For_APrivateSource_PublishesTheIdentityAndNothingElse()
    {
        // Act
        var response = ClientCitationResolutionResponse.For(
            [ResolvedCitation.PrivateSource(StoredEmailId.Create(Message))]);

        // Assert
        var citation = Assert.Single(response.Citations);
        Assert.Equal(Message, citation.StoredEmailId);
        Assert.Equal("PrivateSource", citation.Outcome);
        Assert.Null(citation.Message);
        Assert.Null(citation.Fragment);
        Assert.Null(citation.Attachment);
    }

    /// <summary>A resolved passage reaches the client with the text and the offsets that make the reference checkable.</summary>
    [Fact]
    public void For_AResolvedFragment_PublishesThePassageWithTheOffsetsItWasCutFrom()
    {
        // Act
        var response = ClientCitationResolutionResponse.For(
            [
                ResolvedCitation.Resolved(
                    CitedMessageOf(),
                    new CitedFragment(EmailChunkId.Create(Passage), 3, 120, 143, "the agreed rate is 4.5%")),
            ]);

        // Assert
        var citation = Assert.Single(response.Citations);
        Assert.Equal("Resolved", citation.Outcome);
        Assert.Equal(
            (Passage, 3, 120, 143, "the agreed rate is 4.5%"),
            (citation.Fragment!.FragmentId,
                citation.Fragment.Ordinal,
                citation.Fragment.StartOffset,
                citation.Fragment.EndOffset,
                citation.Fragment.Text));
        Assert.Equal("Quarterly invoice", citation.Message!.Subject);
    }

    /// <summary>
    /// A place that is gone still publishes the message it belonged to, which is what lets a client draw the source of a
    /// fact whose passage was re-cut instead of dropping the citation.
    /// </summary>
    [Fact]
    public void For_AnUnresolvablePlace_StillPublishesTheMessageItBelongedTo()
    {
        // Act
        var response = ClientCitationResolutionResponse.For([ResolvedCitation.Unresolvable(CitedMessageOf())]);

        // Assert
        var citation = Assert.Single(response.Citations);
        Assert.Equal("Unresolvable", citation.Outcome);
        Assert.Equal(Message, citation.Message!.StoredEmailId);
        Assert.Null(citation.Fragment);
    }

    /// <summary>A cited file is described at the position its own download route is asked with, and carries none of what it holds.</summary>
    [Fact]
    public void For_AResolvedAttachment_DescribesTheFileAtThePositionItsDownloadIsAskedWith()
    {
        // Act
        var response = ClientCitationResolutionResponse.For(
            [ResolvedCitation.Resolved(CitedMessageOf(), new CitedAttachment(1, "terms.pdf", "application/pdf", 8192))]);

        // Assert
        var citation = Assert.Single(response.Citations);
        Assert.Equal(
            (1, "terms.pdf", "application/pdf", 8192L),
            (citation.Attachment!.Position,
                citation.Attachment.FileName,
                citation.Attachment.MediaType,
                citation.Attachment.SizeOctets));
    }

    /// <summary>The answer carries one resolution per citation, in the order the request named them.</summary>
    [Fact]
    public void For_SeveralResolutions_PublishesThemInTheOrderTheRequestNamedThem()
    {
        // Arrange
        var other = StoredEmailId.Create(new Guid("55555555-5555-5555-5555-555555555555"));

        // Act
        var response = ClientCitationResolutionResponse.For(
            [ResolvedCitation.PrivateSource(other), ResolvedCitation.Resolved(CitedMessageOf())]);

        // Assert
        Assert.Equal(
            [other.Value, Message],
            response.Citations.Select(citation => citation.StoredEmailId));
    }

    private static CitedMessage CitedMessageOf() => new(
        StoredEmailId.Create(Message),
        MailAccountId.Create("primary"),
        MailFolderAlias.Create("INBOX"),
        "Quarterly invoice",
        new DateTimeOffset(2026, 3, 4, 9, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 3, 4, 9, 1, 0, TimeSpan.Zero));
}
