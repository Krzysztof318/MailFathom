// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Paging;
using Xunit;

namespace MailFathom.Application.UnitTests.Paging;

/// <summary>Covers the digest six administrative readings reduce their filters to before a cursor carries it.</summary>
/// <remarks>
/// The digest text travels inside every cursor those readings issue, so it is pinned against a recorded value rather
/// than only compared with itself: a change that quietly altered it would invalidate every cursor a client holds.
/// </remarks>
public sealed class PageFilterFingerprintTests
{
    /// <summary>The digest is a published value once a cursor carries it, so the recorded text is what proves it.</summary>
    [Fact]
    public void Of_RecordedFilters_ProduceTheDigestThisSchemeHasAlwaysProduced()
    {
        // Act
        var fingerprint = PageFilterFingerprint.Of("account", null, "third");

        // Assert
        Assert.Equal("e50fb1a79cbfce19", fingerprint);
    }

    /// <summary>A filter nobody named and one named as empty are the same request, and reduce to the same digest.</summary>
    [Fact]
    public void Of_AnUnnamedFilterAndAnEmptyOne_ReduceToOneDigest()
    {
        // Act
        var unnamed = PageFilterFingerprint.Of("account", null, "third");
        var empty = PageFilterFingerprint.Of("account", string.Empty, "third");

        // Assert
        Assert.Equal(unnamed, empty);
    }

    /// <summary>Each field holds its position, so two filter sets that differ only in which value went where differ here.</summary>
    [Fact]
    public void Of_TheSameValuesInAnotherOrder_ProduceAnotherDigest()
    {
        // Act
        var asWritten = PageFilterFingerprint.Of("account", null, "third");
        var reordered = PageFilterFingerprint.Of("third", null, "account");

        // Assert
        Assert.NotEqual(asWritten, reordered);
    }

    /// <summary>Every field is written, so a filter a later build adds cannot produce the text an earlier build produced.</summary>
    [Fact]
    public void Of_AnAddedUnnamedFilter_ProducesAnotherDigestThanTheShorterSet()
    {
        // Act
        var shorter = PageFilterFingerprint.Of("account");
        var longer = PageFilterFingerprint.Of("account", null);

        // Assert
        Assert.NotEqual(shorter, longer);
    }

    [Fact]
    public void Of_AnyFilters_ProducesLowercaseHexadecimalOfTheDeclaredLength()
    {
        // Act
        var fingerprint = PageFilterFingerprint.Of("work");

        // Assert
        Assert.Equal(16, fingerprint.Length);
        Assert.All(fingerprint, character => Assert.Contains(character, "0123456789abcdef"));
    }

    /// <summary>
    /// The separator is an assumption about filter values rather than an invariant over them, and this is where the
    /// assumption fails: a value carrying it reduces to what two fields split at it reduce to. The consequence is bounded
    /// — both sets belong to one caller, over one account, and name a boundary that caller may already read.
    /// </summary>
    [Fact]
    public void Of_AValueCarryingTheSeparator_CollidesWithTwoFieldsSplitAtIt()
    {
        // Act
        var oneField = PageFilterFingerprint.Of("a\u001fb");
        var twoFields = PageFilterFingerprint.Of("a", "b");

        // Assert
        Assert.Equal(oneField, twoFields);
    }

    [Fact]
    public void Of_NoFields_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => PageFilterFingerprint.Of((string?[])null!));
    }
}
