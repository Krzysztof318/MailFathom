// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Infrastructure.Mail.Mime;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests;

/// <summary>Covers the sanitization policy message HTML is reduced by before a reader is handed it.</summary>
public sealed class EmailHtmlSanitizerTests
{
    /// <summary>A script element goes with its contents, so the code never reappears as the message's own words.</summary>
    [Fact]
    public void Sanitize_BodyCarryingAScriptElement_RemovesTheElementAndItsContents()
    {
        // Arrange
        var sanitizer = new EmailHtmlSanitizer();

        // Act
        var sanitized = sanitizer.Sanitize("<p>Before</p><script>alert('xss')</script><p>After</p>");

        // Assert
        Assert.DoesNotContain("script", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Before", sanitized, StringComparison.Ordinal);
        Assert.Contains("After", sanitized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""<p onclick="alert('xss')">Text</p>""")]
    [InlineData("""<p onmouseover="alert('xss')">Text</p>""")]
    [InlineData("""<img alt="Logo" onerror="alert('xss')">""")]
    public void Sanitize_BodyCarryingAnEventHandlerAttribute_RemovesTheAttribute(string html)
    {
        // Arrange
        var sanitizer = new EmailHtmlSanitizer();

        // Act
        var sanitized = sanitizer.Sanitize(html);

        // Assert
        Assert.DoesNotContain("on", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Nothing a renderer could resolve survives, so no image is fetched and no read receipt is delivered.</summary>
    [Theory]
    [InlineData("""<img src="https://tracker.example.test/pixel.gif?read=1" alt="Logo">""")]
    [InlineData("""<a href="https://tracker.example.test/open?id=42">Open</a>""")]
    [InlineData("""<p style="background-image: url(https://tracker.example.test/pixel.gif)">Text</p>""")]
    public void Sanitize_BodyCarryingAnExternalReference_RemovesEveryReferenceARendererCouldFollow(string html)
    {
        // Arrange
        var sanitizer = new EmailHtmlSanitizer();

        // Act
        var sanitized = sanitizer.Sanitize(html);

        // Assert
        Assert.DoesNotContain("tracker.example.test", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A reference to a part of the same message is unresolvable here, so it never leaves in the markup.</summary>
    [Fact]
    public void Sanitize_BodyReferencingAnInlineResource_RemovesTheContentIdReference()
    {
        // Arrange
        var sanitizer = new EmailHtmlSanitizer();

        // Act
        var sanitized = sanitizer.Sanitize("""<p>Chart:</p><img src="cid:chart@example.test" alt="Chart">""");

        // Assert
        Assert.DoesNotContain("cid:", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Chart:", sanitized, StringComparison.Ordinal);
    }

    /// <summary>What a stripped image was is worth keeping, because it is the only thing a reader is left with.</summary>
    [Fact]
    public void Sanitize_BodyCarryingAnImage_KeepsTheAlternativeTextThatDescribesIt()
    {
        // Arrange
        var sanitizer = new EmailHtmlSanitizer();

        // Act
        var sanitized = sanitizer.Sanitize("""<img src="cid:chart@example.test" alt="Revenue by quarter">""");

        // Assert
        Assert.Contains("Revenue by quarter", sanitized, StringComparison.Ordinal);
    }

    /// <summary>Markup a browser would repair is repaired here too, rather than letting a stray tag drop the rest.</summary>
    [Fact]
    public void Sanitize_UnbalancedAndDeeplyNestedMarkup_KeepsEveryWordAndClosesWhatTheSenderLeftOpen()
    {
        // Arrange
        var sanitizer = new EmailHtmlSanitizer();

        // Act
        var sanitized = sanitizer.Sanitize("<div><p>First<div><span>Second</div></p><b>Third");

        // Assert
        Assert.Contains("First", sanitized, StringComparison.Ordinal);
        Assert.Contains("Second", sanitized, StringComparison.Ordinal);
        Assert.Contains("Third", sanitized, StringComparison.Ordinal);
        Assert.EndsWith(">", sanitized, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unclosed disallowed container takes what the parser nested inside it, which is the same text a browser would
    /// not display either. The alternative — unwrapping a removed element onto its children — would keep the source of
    /// a <c>&lt;script&gt;</c> as though the sender had written it as words.
    /// </summary>
    [Fact]
    public void Sanitize_UnclosedDisallowedContainer_RemovesItWithWhatTheParserNestedInside()
    {
        // Arrange
        var sanitizer = new EmailHtmlSanitizer();

        // Act
        var sanitized = sanitizer.Sanitize(
            """<p>Before</p><iframe src="https://tracker.example.test"><p>Swallowed</p>""");

        // Assert
        Assert.DoesNotContain("iframe", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tracker.example.test", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Swallowed", sanitized, StringComparison.Ordinal);
        Assert.Contains("Before", sanitized, StringComparison.Ordinal);
    }

    /// <summary>A form is machinery for sending data somewhere, which a mail body has no business carrying.</summary>
    [Fact]
    public void Sanitize_BodyCarryingAForm_RemovesIt()
    {
        // Arrange
        var sanitizer = new EmailHtmlSanitizer();

        // Act
        var sanitized = sanitizer.Sanitize(
            """<form action="https://tracker.example.test"><input name="password"><button>Send</button></form>""");

        // Assert
        Assert.DoesNotContain("form", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("input", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tracker.example.test", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Markup that spells a tag as character references stays text. The sanitizer parses what it is given, so an
    /// escaped sequence is content rather than a tag, and re-serializing it must not decode it back into one.
    /// </summary>
    [Theory]
    [InlineData("&lt;script&gt;alert('xss')&lt;/script&gt;")]
    [InlineData("&#60;script&#62;alert('xss')&#60;/script&#62;")]
    [InlineData("&#x3c;script&#x3e;alert('xss')&#x3c;/script&#x3e;")]
    public void Sanitize_BodyEncodingATagAsCharacterReferences_LeavesItAsTextRatherThanMarkup(string html)
    {
        // Arrange
        var sanitizer = new EmailHtmlSanitizer();

        // Act
        var sanitized = sanitizer.Sanitize(html);

        // Assert
        Assert.DoesNotContain("<script", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A style sheet can hide a reference behind a url(), so no declaration survives at all.</summary>
    [Fact]
    public void Sanitize_BodyCarryingAStyleElementAndStyleAttributes_RemovesBoth()
    {
        // Arrange
        var sanitizer = new EmailHtmlSanitizer();

        // Act
        var sanitized = sanitizer.Sanitize(
            """<style>@import url(https://tracker.example.test/a.css);</style><p style="color: red">Text</p>""");

        // Assert
        Assert.DoesNotContain("style", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tracker.example.test", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Text", sanitized, StringComparison.Ordinal);
    }

    /// <summary>The presentational elements real mail is written with keep the words inside them.</summary>
    [Fact]
    public void Sanitize_BodyUsingPresentationalElements_KeepsTheirTextAndDropsTheirAttributes()
    {
        // Arrange
        var sanitizer = new EmailHtmlSanitizer();

        // Act
        var sanitized = sanitizer.Sanitize("""<font color="#ff0000" face="Arial">Regards,</font><center>Anna</center>""");

        // Assert
        Assert.Contains("Regards,", sanitized, StringComparison.Ordinal);
        Assert.Contains("Anna", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("color", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("face", sanitized, StringComparison.OrdinalIgnoreCase);
    }
}
