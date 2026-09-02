// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery.Drafts;
using Xunit;

namespace MailFathom.Domain.UnitTests.Delivery.Drafts;

/// <summary>Covers what names one staged file, which is what a removal names rather than the file's own name.</summary>
public sealed class MailDraftAttachmentIdTests
{
    /// <summary>An identifier keeps the value it was created from, which is what a removal is compared against.</summary>
    [Fact]
    public void Create_ANonEmptyValue_KeepsIt()
    {
        // Arrange
        var value = Guid.CreateVersion7();

        // Act
        var identifier = MailDraftAttachmentId.Create(value);

        // Assert
        Assert.Equal(value, identifier.Value);
        Assert.Equal(value.ToString(), identifier.ToString());
    }

    /// <summary>The empty value names no file, so it is refused rather than becoming an identifier nothing matches.</summary>
    [Fact]
    public void Create_TheEmptyValue_IsRefused()
    {
        // Arrange
        // Act
        // Assert
        Assert.Throws<ArgumentException>(() => MailDraftAttachmentId.Create(Guid.Empty));
    }

    /// <summary>Two identifiers of one value are one identifier, which is what lets a removal be found by equality.</summary>
    [Fact]
    public void Equals_TwoIdentifiersOfOneValue_AreTheSameIdentifier()
    {
        // Arrange
        var value = Guid.CreateVersion7();

        // Act
        var first = MailDraftAttachmentId.Create(value);
        var second = MailDraftAttachmentId.Create(value);

        // Assert
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }
}
