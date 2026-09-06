// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Chunking;

/// <summary>Covers what a passage has to be able to say about the attachment it was cut out of.</summary>
public sealed class EmailChunkAttachmentSourceTests
{
    /// <summary>The walk position and the declaration reach the digest, so both survive the factory unchanged.</summary>
    [Fact]
    public void Create_AWalkPositionAndADeclaration_CarriesBothThrough()
    {
        // Act
        var source = EmailChunkAttachmentSource.Create(3, "application/pdf");

        // Assert
        Assert.Equal(3, source.Position);
        Assert.Equal("application/pdf", source.DeclaredMediaType);
    }

    /// <summary>A negative position names no attachment the download route could be addressed with.</summary>
    [Fact]
    public void Create_ANegativeWalkPosition_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EmailChunkAttachmentSource.Create(-1, "application/pdf"));
    }

    /// <summary>A missing declaration would leave the digest with a field nobody wrote.</summary>
    [Fact]
    public void Create_AMissingDeclaration_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => EmailChunkAttachmentSource.Create(0, null!));
    }
}
