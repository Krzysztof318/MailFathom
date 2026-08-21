// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Text;
using MailFathom.Application.Contacts;
using MailFathom.Domain.Contacts;
using Xunit;

namespace MailFathom.Application.UnitTests.Contacts;

/// <summary>Covers the opaque form a caller presents to continue a walk of the contact book.</summary>
/// <remarks>
/// The cursor carries a name's comparison form, which is the one field here that is not a fixed shape: it may be any
/// length up to a contact name's bound and may contain the character the fields are separated by. Both are what these
/// tests are about, because either read wrongly would serve a contact twice or skip one, and a walk that skips somebody
/// is a listing that is quietly incomplete.
/// </remarks>
public sealed class ContactCursorTests
{
    private static readonly ContactId Identity = ContactId.Create(new Guid("11111111-2222-3333-4444-555555555555"));

    [Fact]
    public void Encode_ACursor_ProducesTextThatDecodesBackToTheSameBoundary()
    {
        // Arrange
        var cursor = ContactCursor.After(ContactDisplayName.Create("Anna Kowalska"), Identity);

        // Act
        var decoded = ContactCursor.TryDecode(cursor.Encode(), out var read);

        // Assert
        Assert.True(decoded);
        Assert.Equal(cursor, read);
    }

    /// <summary>
    /// The regression the field order exists for. A name may itself contain the separator, so a decoding that split
    /// greedily would read part of somebody's name as a field of its own and refuse a cursor this deployment issued —
    /// which reads to a caller as a walk that cannot be continued.
    /// </summary>
    [Fact]
    public void Decode_ACursorForANameContainingTheFieldSeparator_ReadsTheWholeName()
    {
        // Arrange
        var displayName = ContactDisplayName.Create("Anna J. Kowalska-Nowak.");
        var cursor = ContactCursor.After(displayName, Identity);

        // Act
        var decoded = ContactCursor.TryDecode(cursor.Encode(), out var read);

        // Assert
        Assert.True(decoded);
        Assert.Equal(displayName.SortKey, read?.DisplayNameSortKey);
        Assert.Equal(Identity, read?.ContactId);
    }

    /// <summary>A name at the bound still encodes to a cursor this refuses nothing about, in every character width.</summary>
    [Fact]
    public void Encode_ACursorForTheLongestNameABookAdmits_StaysWithinWhatDecodingAccepts()
    {
        // Arrange
        var displayName = ContactDisplayName.Create(new string('Å', ContactDisplayName.MaximumLength));

        // Act
        var encoded = ContactCursor.After(displayName, Identity).Encode();

        // Assert
        Assert.True(encoded.Length <= ContactCursor.MaximumEncodedLength);
        Assert.True(ContactCursor.TryDecode(encoded, out var read));
        Assert.Equal(displayName.SortKey, read?.DisplayNameSortKey);
    }

    /// <summary>
    /// Anything a caller presents that this did not issue is refused rather than read, because a cursor decides which
    /// contacts a page skips: one built by hand would silently start a walk in the middle of the book.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not base64url at all!")]
    public void TryDecode_TextThisDidNotIssue_RefusesIt(string presented)
    {
        // Act
        var decoded = ContactCursor.TryDecode(presented, out var read);

        // Assert
        Assert.False(decoded);
        Assert.Null(read);
    }

    /// <summary>
    /// The shape a hand-built cursor actually takes: every field is present and only the identity is spelled the way a
    /// person writes a UUID rather than the way this encodes one. Refusing it is what keeps a walk to the cursors this
    /// deployment issued.
    /// </summary>
    [Fact]
    public void TryDecode_ACursorAssembledByHandWithADashedIdentity_RefusesIt()
    {
        // Arrange
        var presented = Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"1.{Identity.Value:D}.ANNA"));

        // Act
        var decoded = ContactCursor.TryDecode(presented, out var read);

        // Assert
        Assert.False(decoded);
        Assert.Null(read);
    }

    /// <summary>Text longer than any cursor this issues is refused before it is decoded at all.</summary>
    [Fact]
    public void TryDecode_TextLongerThanAnyCursorThisIssues_RefusesItUnread()
    {
        // Act
        var decoded = ContactCursor.TryDecode(new string('a', ContactCursor.MaximumEncodedLength + 1), out var read);

        // Assert
        Assert.False(decoded);
        Assert.Null(read);
    }
}
