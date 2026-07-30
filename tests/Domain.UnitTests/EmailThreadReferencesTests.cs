// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Emails;
using Xunit;

namespace MailMcp.Domain.UnitTests;

public sealed class EmailThreadReferencesTests
{
    /// <summary>Angle brackets and the whitespace around them are transport, so either writing is one identifier.</summary>
    [Fact]
    public void Create_IdentifiersWrittenWithBracketsAndSurroundingWhitespace_NormalizesThemToOneForm()
    {
        // Act
        var references = EmailThreadReferences.Create(
            "<root@example.test>",
            "parent@example.test",
            ["<root@example.test>", " <parent@example.test> "]);

        // Assert
        Assert.Equal("root@example.test", references.MessageId);
        Assert.Equal("parent@example.test", references.InReplyTo);
        Assert.Equal(["root@example.test", "parent@example.test"], references.References);
    }

    /// <summary>The identifier itself is opaque, so a space a quoted left half carries is content rather than folding.</summary>
    [Fact]
    public void Create_IdentifierWithQuotedLeftHalf_KeepsTheSpaceInsideIt()
    {
        // Act
        var references = EmailThreadReferences.Create("<\"a b\"@example.test>", inReplyTo: null, references: null);

        // Assert
        Assert.Equal("\"a b\"@example.test", references.MessageId);
    }

    /// <summary>An identifier no parser could have produced is refused rather than repaired into a thread key.</summary>
    [Fact]
    public void Create_IdentifierCarryingAControlCharacter_RefusesIt()
    {
        // Act
        var references = EmailThreadReferences.Create(
            "<root\u0007@example.test>",
            inReplyTo: null,
            references: ["<parent\u0001@example.test>", "<ancestor@example.test>"]);

        // Assert
        Assert.Null(references.MessageId);
        Assert.Equal(["ancestor@example.test"], references.References);
    }

    /// <summary>The header's order is the path from the root, so it is kept while repeated ancestors collapse.</summary>
    [Fact]
    public void Create_RepeatedAncestors_KeepsHeaderOrderAndCollapsesDuplicates()
    {
        // Act
        var references = EmailThreadReferences.Create(
            messageId: null,
            inReplyTo: null,
            ["<a@example.test>", "<b@example.test>", "a@example.test", "<c@example.test>"]);

        // Assert
        Assert.Equal(["a@example.test", "b@example.test", "c@example.test"], references.References);
    }

    /// <summary>Case is preserved, because a mail server is entitled to mint two identifiers that differ only in it.</summary>
    [Fact]
    public void Create_IdentifierCarryingUpperCase_KeepsIt()
    {
        // Act
        var references = EmailThreadReferences.Create("<AbC123@Example.Test>", inReplyTo: null, references: null);

        // Assert
        Assert.Equal("AbC123@Example.Test", references.MessageId);
    }

    /// <summary>
    /// A sender decides how long the header is and a content read publishes what a parse found, so the path is bounded
    /// where it is read. The root and the recent end survive, because one names the conversation and the other is what
    /// a reader walks.
    /// </summary>
    [Fact]
    public void Create_MoreAncestorsThanTheBoundKeeps_KeepsTheRootAndTheMostRecentOnes()
    {
        // Arrange
        var writtenReferences = Enumerable
            .Range(0, EmailThreadReferences.MaximumReferences + 10)
            .Select(position => $"<ancestor-{position}@example.test>")
            .ToArray();

        // Act
        var references = EmailThreadReferences.Create(messageId: null, inReplyTo: null, writtenReferences);

        // Assert
        Assert.Equal(EmailThreadReferences.MaximumReferences, references.References.Count);
        Assert.Equal("ancestor-0@example.test", references.References[0]);
        Assert.Equal(
            $"ancestor-{EmailThreadReferences.MaximumReferences + 9}@example.test",
            references.References[^1]);

        // The eleven ancestors after the root are what the bound gave up, so the second entry is the twelfth written.
        Assert.Equal("ancestor-11@example.test", references.References[1]);
    }

    /// <summary>A message with no threading headers is one value rather than three empty ones.</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("<>", "   ")]
    public void Create_MessageWithNoThreadingHeaders_ReportsNone(string? messageId, string? inReplyTo)
    {
        // Act
        var references = EmailThreadReferences.Create(messageId, inReplyTo, []);

        // Assert
        Assert.Same(EmailThreadReferences.None, references);
        Assert.Null(references.MessageId);
        Assert.Null(references.InReplyTo);
        Assert.Empty(references.References);
    }
}
