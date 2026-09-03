// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering;
using Xunit;

namespace MailFathom.Application.UnitTests.EmailContent.Rendering;

/// <summary>Covers reading back what the self-contained markup inlined, which is what the read's picture budget is spent in.</summary>
public sealed class SelfContainedHtmlImagesTests
{
    /// <summary>A picture is counted as the octets behind it rather than as the characters that encode it.</summary>
    [Fact]
    public void OctetsIn_MarkupCarryingAPicture_CountsWhatThePictureItselfComesTo()
    {
        // Arrange
        var markup = $"""<p>Body</p><img src="data:image/png;base64,{Convert.ToBase64String(new byte[300])}">""";

        // Act
        var octets = SelfContainedHtmlImages.OctetsIn(markup);

        // Assert
        Assert.Equal(300, octets);
    }

    /// <summary>Every position markup can inline a picture in is read, not only a quoted attribute.</summary>
    [Theory]
    [InlineData("""<img src="data:image/png;base64,PAYLOAD">""")]
    [InlineData("<img src='data:image/png;base64,PAYLOAD'>")]
    [InlineData("""<div style="background:url(data:image/png;base64,PAYLOAD)">x</div>""")]
    [InlineData("<style>div{background:url(data:image/png;base64,PAYLOAD)}</style>")]
    public void OctetsIn_PictureWrittenInAnyPosition_IsCounted(string form)
    {
        // Arrange
        var markup = form.Replace("PAYLOAD", Convert.ToBase64String(new byte[120]), StringComparison.Ordinal);

        // Act
        var octets = SelfContainedHtmlImages.OctetsIn(markup);

        // Assert
        Assert.Equal(120, octets);
    }

    /// <summary>Several pictures come to what they come to together, which is what a budget across a read is spent in.</summary>
    [Fact]
    public void OctetsIn_MarkupCarryingSeveralPictures_CountsThemTogether()
    {
        // Arrange
        var first = Convert.ToBase64String(new byte[90]);
        var second = Convert.ToBase64String(new byte[210]);
        var markup = $"""<img src="data:image/png;base64,{first}"><img src="data:image/gif;base64,{second}">""";

        // Act
        var octets = SelfContainedHtmlImages.OctetsIn(markup);

        // Assert
        Assert.Equal(300, octets);
    }

    /// <summary>Markup carrying no picture of its own spends none of the budget, however much of it there is.</summary>
    [Theory]
    [InlineData("<p>Body</p>")]
    [InlineData("""<img src="https://cdn.example.test/logo.png"><p>Body</p>""")]
    [InlineData("")]
    public void OctetsIn_MarkupInliningNothing_CountsNothing(string markup)
    {
        // Act
        var octets = SelfContainedHtmlImages.OctetsIn(markup);

        // Assert
        Assert.Equal(0, octets);
    }

    /// <summary>Nothing is read out of an absent representation, because the caller has none to charge for.</summary>
    [Fact]
    public void OctetsIn_NullMarkup_IsRefused()
    {
        // Act and assert
        Assert.Throws<ArgumentNullException>(() => SelfContainedHtmlImages.OctetsIn(null!));
    }
}
