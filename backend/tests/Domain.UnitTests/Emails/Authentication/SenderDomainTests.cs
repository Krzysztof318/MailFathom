// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails.Authentication;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails.Authentication;

public sealed class SenderDomainTests
{
    /// <summary>Three sources write the same domain differently, and a verdict compares them against each other.</summary>
    [Theory]
    [InlineData("example.test")]
    [InlineData("Example.Test")]
    [InlineData("EXAMPLE.TEST")]
    [InlineData("  example.test  ")]
    public void TryCreate_DomainWrittenDifferently_ProducesOneComparisonForm(string written)
    {
        // Act
        var created = SenderDomain.TryCreate(written, out var domain);

        // Assert
        Assert.True(created);
        Assert.Equal("EXAMPLE.TEST", domain.NormalizedValue);
    }

    /// <summary>The written form is kept beside the comparison form, because only one of them is meant to be compared.</summary>
    [Fact]
    public void TryCreate_MixedCaseDomain_KeepsWhatTheHeaderWrote()
    {
        // Act
        SenderDomain.TryCreate("Mail.Example.Test", out var domain);

        // Assert
        Assert.Equal("Mail.Example.Test", domain.Value);
    }

    /// <summary>Two domains differing only in case are one domain, which is what alignment depends on.</summary>
    [Fact]
    public void Equals_DomainsDifferingOnlyInCase_AreOneDomain()
    {
        // Arrange
        SenderDomain.TryCreate("Example.Test", out var first);
        SenderDomain.TryCreate("EXAMPLE.test", out var second);

        // Act
        var areEqual = first == second;

        // Assert
        Assert.True(areEqual);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    /// <summary>What cannot become a comparison key is refused rather than repaired into one.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("exa mple.test")]
    [InlineData("example\0.test")]
    [InlineData("anna@example.test")]
    [InlineData("example..test")]
    [InlineData(".example.test")]
    [InlineData("example.test.")]
    public void TryCreate_UnusableText_IsRefused(string written)
    {
        // Act
        var created = SenderDomain.TryCreate(written, out _);

        // Assert
        Assert.False(created);
    }

    /// <summary>A domain past the length a resolver accepts is refused, because a header's length is nobody's to bound.</summary>
    [Fact]
    public void TryCreate_DomainPastTheResolverLimit_IsRefused()
    {
        // Arrange
        var overLong = string.Join('.', Enumerable.Repeat(new string('a', 63), 5));

        // Act
        var created = SenderDomain.TryCreate(overLong, out _);

        // Assert
        Assert.True(overLong.Length > SenderDomain.MaximumLength);
        Assert.False(created);
    }

    /// <summary>One internationalized name written in two encodings is one name, which is what a list has to match on.</summary>
    [Theory]
    [InlineData("bücher.example")]
    [InlineData("BÜCHER.example")]
    [InlineData("xn--bcher-kva.example")]
    [InlineData("XN--BCHER-KVA.EXAMPLE")]
    public void TryCreate_InternationalizedDomainInEitherEncoding_ProducesOneComparisonForm(string written)
    {
        // Act
        var created = SenderDomain.TryCreate(written, out var domain);

        // Assert
        Assert.True(created);
        Assert.Equal("XN--BCHER-KVA.EXAMPLE", domain.NormalizedValue);
    }

    /// <summary>The written form survives the conversion, because only the comparison form is meant to be an encoding.</summary>
    [Fact]
    public void TryCreate_InternationalizedDomain_KeepsWhatTheSourceWrote()
    {
        // Act
        SenderDomain.TryCreate("bücher.example", out var domain);

        // Assert
        Assert.Equal("bücher.example", domain.Value);
    }

    /// <summary>A name within the resolver limits in its own script can leave them once encoded, and is refused there.</summary>
    /// <remarks>
    /// This is why the bounds are applied to both forms rather than only to what a header wrote: A-labels are the
    /// longer encoding, and they are what a column and a resolver actually have to hold. Every label here stays inside
    /// the label limit in both forms, so what refuses the name is the whole encoded name's length and nothing else.
    /// </remarks>
    [Fact]
    public void TryCreate_DomainWithinTheLimitsUntilItIsEncoded_IsRefused()
    {
        // Arrange
        var written = string.Join('.', Enumerable.Repeat(string.Concat(Enumerable.Repeat("ąćęłńóśźż", 3)), 8));

        // Act
        var created = SenderDomain.TryCreate(written, out _);

        // Assert
        Assert.True(written.Length <= SenderDomain.MaximumLength);
        Assert.False(created);
    }

    /// <summary>A name no encoder can put into A-labels matches nothing, so it is refused rather than compared as it arrived.</summary>
    [Fact]
    public void TryCreate_DomainNoEncoderAccepts_IsRefused()
    {
        // Act
        var created = SenderDomain.TryCreate("͸.example", out _);

        // Assert
        Assert.False(created);
    }

    /// <summary>Only a strictly lower name is beneath a domain, and a shared suffix of characters is not one.</summary>
    [Theory]
    [InlineData("mail.example.test", "example.test", true)]
    [InlineData("a.b.example.test", "example.test", true)]
    [InlineData("example.test", "example.test", false)]
    [InlineData("notexample.test", "example.test", false)]
    [InlineData("example.test", "mail.example.test", false)]
    public void IsSubdomainOf_NameAndCandidateAncestor_AnswersOnWholeLabels(
        string candidate,
        string ancestor,
        bool expected)
    {
        // Arrange
        SenderDomain.TryCreate(candidate, out var descendant);
        SenderDomain.TryCreate(ancestor, out var parent);

        // Act
        var isBeneath = descendant.IsSubdomainOf(parent);

        // Assert
        Assert.Equal(expected, isBeneath);
    }

    /// <summary>An SPF identity is written as a mailbox, and the domain is what follows the last at-sign.</summary>
    [Theory]
    [InlineData("bounce@relay.test", "RELAY.TEST")]
    [InlineData("\"a@b\"@relay.test", "RELAY.TEST")]
    [InlineData("relay.test", "RELAY.TEST")]
    public void TryCreateFromMailbox_MailboxOrBareDomain_ReadsTheDomain(string mailbox, string expected)
    {
        // Act
        var created = SenderDomain.TryCreateFromMailbox(mailbox, out var domain);

        // Assert
        Assert.True(created);
        Assert.Equal(expected, domain.NormalizedValue);
    }

    /// <summary>A mailbox with a half missing names no domain, so nothing is read from it.</summary>
    [Theory]
    [InlineData("bounce@")]
    [InlineData("@relay.test")]
    [InlineData("bounce@rel ay.test")]
    [InlineData(null)]
    public void TryCreateFromMailbox_NoUsableDomain_IsRefused(string? mailbox)
    {
        // Act
        var created = SenderDomain.TryCreateFromMailbox(mailbox, out _);

        // Assert
        Assert.False(created);
    }
}
