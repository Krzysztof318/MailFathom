// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
        Assert.False(request.IncludeAttachmentContent);
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
