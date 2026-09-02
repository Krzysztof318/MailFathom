// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.GetEmailContent;

/// <summary>Covers the two refusals a content read decides before anything is read.</summary>
/// <remarks>
/// They are the request's own invariant rather than a boundary's courtesy check, so a second entrypoint cannot reach the
/// use case with a list nobody counted. Both refuse rather than repair: a truncated list and a de-duplicated one both
/// answer a question the caller did not ask.
/// </remarks>
public sealed class GetEmailContentRequestTests
{
    [Fact]
    public void Create_EmailsWithinTheBound_KeepsThemInTheOrderTheyWereNamed()
    {
        // Arrange
        var storedEmailIds = IdentitiesOf(GetEmailContentRequest.MaximumEmails);

        // Act
        var request = GetEmailContentRequest.Create(storedEmailIds);

        // Assert
        Assert.Equal(storedEmailIds, request.StoredEmailIds);
        Assert.False(request.IncludeSanitizedHtml);
        Assert.False(request.IncludeAttachmentDownloadLinks);
    }

    /// <summary>
    /// Both are off unless a caller asked, and the second of them is why that matters: retaining a message's remote
    /// references is what tells the sender's servers the reader's address and that the message was opened, so it is a
    /// choice a reader makes per message rather than a default a request inherits by saying nothing.
    /// </summary>
    [Fact]
    public void Create_ARequestAskingForNeither_BuildsNoDocumentAndKeepsNoRemoteReference()
    {
        // Act
        var request = GetEmailContentRequest.Create(IdentitiesOf(1));

        // Assert
        Assert.False(request.IncludeMailDocument);
        Assert.False(request.RetainRemoteImageReferences);
    }

    /// <summary>The consent is about one message, so the one request that may carry it names one message.</summary>
    [Fact]
    public void RetainRemoteImageReferences_ARequestNamingOneEmail_CarriesTheReadersConsent()
    {
        // Act
        var request = GetEmailContentRequest.Create(IdentitiesOf(1)) with { RetainRemoteImageReferences = true };

        // Assert
        Assert.True(request.RetainRemoteImageReferences);
    }

    /// <summary>A read naming several messages cannot carry a consent one reader gave about one of them.</summary>
    /// <remarks>
    /// A conversation read is the second case and it is the same defect: it names no email at all until the thread
    /// resolves, so the consent would be applied to whatever the conversation turned out to hold.
    /// </remarks>
    [Fact]
    public void RetainRemoteImageReferences_ARequestNamingOtherThanOneEmail_IsRefused()
    {
        // Arrange
        var several = GetEmailContentRequest.Create(IdentitiesOf(2));
        var conversation = GetEmailContentRequest.CreateForSelection(
            namedEmails: null,
            () => EmailThreadId.Create(Guid.CreateVersion7()));

        // Act
        var refusals = new[] { several, conversation }
            .Select(request => Record.Exception(() => request with { RetainRemoteImageReferences = true }));

        // Assert
        Assert.All(refusals, refusal => Assert.IsType<InvalidOperationException>(refusal));
    }

    /// <summary>A read naming nothing and a read naming too much are one finding about a count the caller chose.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(GetEmailContentRequest.MaximumEmails + 1)]
    [InlineData(GetEmailContentRequest.MaximumEmails + 90)]
    public void Create_EmailCountOutsideTheBound_IsRefusedRatherThanTruncated(int emailCount)
    {
        // Arrange
        var storedEmailIds = IdentitiesOf(emailCount);

        // Act
        var failure = Assert.Throws<EmailContentReadCountOutOfRangeException>(
            () => GetEmailContentRequest.Create(storedEmailIds));

        // Assert
        Assert.Equal(MailFathomErrorCode.EmailContentReadCountOutOfRange, failure.ErrorCode);
        Assert.Equal(GetEmailContentRequest.MaximumEmails, failure.MaximumEmails);
    }

    /// <summary>The limit is stated so a caller can act on it; the list it sent is not repeated back to it.</summary>
    [Fact]
    public void Create_TooManyEmails_StatesTheLimitAndNoIdentifier()
    {
        // Arrange
        var storedEmailIds = IdentitiesOf(GetEmailContentRequest.MaximumEmails + 1);

        // Act
        var failure = Assert.Throws<EmailContentReadCountOutOfRangeException>(
            () => GetEmailContentRequest.Create(storedEmailIds));

        // Assert
        Assert.Contains(
            GetEmailContentRequest.MaximumEmails.ToString(System.Globalization.CultureInfo.InvariantCulture),
            failure.Message,
            StringComparison.Ordinal);
        Assert.All(
            storedEmailIds,
            storedEmailId => Assert.DoesNotContain(
                storedEmailId.Value.ToString(),
                failure.Message,
                StringComparison.Ordinal));
    }

    /// <summary>Serving a repeat twice spends the budget on content the caller holds; dropping it returns fewer entries than were named.</summary>
    [Fact]
    public void Create_TheSameEmailNamedTwice_IsRefusedRatherThanServedOrCollapsed()
    {
        // Arrange
        var repeated = StoredEmailId.Create(Guid.CreateVersion7());

        // Act
        var failure = Assert.Throws<EmailContentReadDuplicateEmailException>(
            () => GetEmailContentRequest.Create([repeated, StoredEmailId.Create(Guid.CreateVersion7()), repeated]));

        // Assert
        Assert.Equal(MailFathomErrorCode.EmailContentReadDuplicateEmail, failure.ErrorCode);
        Assert.DoesNotContain(repeated.Value.ToString(), failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Either reading of a call carrying both returns mail the caller did not ask for, so neither is chosen for it.</summary>
    [Fact]
    public void CreateForSelection_BothEmailsAndAConversation_IsRefusedRatherThanResolvedByPrecedence()
    {
        // Arrange
        var storedEmailIds = IdentitiesOf(1);
        var threadId = EmailThreadId.Create(Guid.CreateVersion7());

        // Act
        var failure = Assert.Throws<EmailContentReadSelectionInvalidException>(
            () => GetEmailContentRequest.CreateForSelection(() => storedEmailIds, () => threadId));

        // Assert
        Assert.Equal(MailFathomErrorCode.EmailContentReadSelectionInvalid, failure.ErrorCode);
    }

    /// <summary>
    /// An empty list is a list the caller sent, so the selection is settled before anything is counted: the list stays
    /// unresolved until the refusal has been decided, and a count would otherwise answer a question nobody asked.
    /// </summary>
    [Fact]
    public void CreateForSelection_AnEmptyEmailListBesideAConversation_IsRefusedAsBothRatherThanAsTooFewEmails()
    {
        // Arrange
        var threadId = EmailThreadId.Create(Guid.CreateVersion7());

        // Act
        var failure = Assert.Throws<EmailContentReadSelectionInvalidException>(
            () => GetEmailContentRequest.CreateForSelection(() => [], () => threadId));

        // Assert
        Assert.Equal(MailFathomErrorCode.EmailContentReadSelectionInvalid, failure.ErrorCode);
    }

    /// <summary>
    /// The conversation is resolved as late as the list is, so a call that named both is refused for naming both rather
    /// than for how it spelled the argument it will not be read by.
    /// </summary>
    [Fact]
    public void CreateForSelection_AnUnreadableConversationBesideAList_IsRefusedBeforeTheConversationIsResolved()
    {
        // Act
        var failure = Assert.Throws<EmailContentReadSelectionInvalidException>(
            () => GetEmailContentRequest.CreateForSelection(
                () => IdentitiesOf(1),
                () => throw new InvalidOperationException("The conversation was expected to stay unresolved.")));

        // Assert
        Assert.Equal(MailFathomErrorCode.EmailContentReadSelectionInvalid, failure.ErrorCode);
    }

    [Fact]
    public void CreateForSelection_NeitherEmailsNorAConversation_IsRefusedRatherThanServedEmpty()
    {
        // Act
        var failure = Assert.Throws<EmailContentReadSelectionInvalidException>(
            () => GetEmailContentRequest.CreateForSelection(namedEmails: null, namedThread: null));

        // Assert
        Assert.Equal(MailFathomErrorCode.EmailContentReadSelectionInvalid, failure.ErrorCode);
    }

    [Fact]
    public void CreateForSelection_AConversationAlone_NamesItAndCountsNoEmail()
    {
        // Arrange
        var threadId = EmailThreadId.Create(Guid.CreateVersion7());

        // Act
        var request = GetEmailContentRequest.CreateForSelection(namedEmails: null, () => threadId);

        // Assert
        Assert.Equal(threadId, request.ThreadId);
        Assert.Empty(request.StoredEmailIds);
    }

    [Fact]
    public void CreateForSelection_EmailsAlone_NamesThemAndNoConversation()
    {
        // Arrange
        var storedEmailIds = IdentitiesOf(2);

        // Act
        var request = GetEmailContentRequest.CreateForSelection(() => storedEmailIds, namedThread: null);

        // Assert
        Assert.Equal(storedEmailIds, request.StoredEmailIds);
        Assert.Null(request.ThreadId);
    }

    /// <summary>A conversation read is bounded where it resolves, so the list a caller names is still counted as one.</summary>
    [Fact]
    public void CreateForSelection_MoreEmailsThanOneReadServes_IsRefusedByTheSameCountAsANamedList()
    {
        // Arrange
        var storedEmailIds = IdentitiesOf(GetEmailContentRequest.MaximumEmails + 1);

        // Act
        var failure = Assert.Throws<EmailContentReadCountOutOfRangeException>(
            () => GetEmailContentRequest.CreateForSelection(() => storedEmailIds, namedThread: null));

        // Assert
        Assert.Equal(GetEmailContentRequest.MaximumEmails, failure.MaximumEmails);
    }

    /// <summary>The bound is the count half of the control on how much mail one call draws out, so it is pinned rather than inferred.</summary>
    [Fact]
    public void MaximumEmails_IsTenEmailsPerRead()
    {
        // Assert
        Assert.Equal(10, GetEmailContentRequest.MaximumEmails);
    }

    private static StoredEmailId[] IdentitiesOf(int count) =>
        [.. Enumerable.Range(0, count).Select(_ => StoredEmailId.Create(Guid.CreateVersion7()))];
}
