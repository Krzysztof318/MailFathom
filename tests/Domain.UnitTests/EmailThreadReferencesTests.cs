// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Emails;
using Xunit;

namespace MailMcp.Domain.UnitTests;

public sealed class EmailThreadReferencesTests
{
    /// <summary>Angle brackets and header folding are transport, so an ancestor written either way is one identifier.</summary>
    [Fact]
    public void Create_IdentifiersWrittenWithBracketsAndFolding_NormalizesThemToOneForm()
    {
        // Act
        var references = EmailThreadReferences.Create(
            "<root@example.test>",
            "parent@example.test",
            ["<root@example.test>", "<parent@\r\n example.test>"]);

        // Assert
        Assert.Equal("root@example.test", references.MessageId);
        Assert.Equal("parent@example.test", references.InReplyTo);
        Assert.Equal(["root@example.test", "parent@example.test"], references.References);
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
