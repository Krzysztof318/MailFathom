// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Actions;
using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam.Actions;

public sealed class SpamActionSettingsTests
{
    [Fact]
    public void None_ADeploymentThatConfiguredNothing_AsksForNoChangeToAnyMailbox()
    {
        // Act
        var settings = SpamActionSettings.None;

        // Assert
        Assert.False(settings.FilesJunk);
        Assert.False(settings.MarksJunkRead);
        Assert.False(settings.IsAnyActionEnabled);
        Assert.Null(settings.Threshold);
    }

    [Fact]
    public void Create_NoDestinationNamed_FilesIntoWhicheverFolderPlaysTheJunkRole()
    {
        // Act
        var settings = SpamActionSettings.Create(filesJunk: true, marksJunkRead: false);

        // Assert
        Assert.Equal(MailFolderSpecialUse.Junk, settings.JunkFolder.Role);
        Assert.Null(settings.JunkFolder.Alias);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Create_EitherSwitchOn_ReportsThatSomethingIsActedOn(bool filesJunk, bool marksJunkRead)
    {
        // Act
        var settings = SpamActionSettings.Create(filesJunk, marksJunkRead);

        // Assert
        Assert.True(settings.IsAnyActionEnabled);
    }

    [Fact]
    public void Create_TheUnspecifiedFolderReference_IsRefused()
    {
        // Act
        var refusal = () => SpamActionSettings.Create(
            filesJunk: true,
            marksJunkRead: false,
            default(MailFolderReference));

        // Assert
        Assert.Throws<ArgumentException>(refusal);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Create_AThresholdThatIsNotFinite_IsRefused(double threshold)
    {
        // Act
        var refusal = () => SpamActionSettings.Create(filesJunk: true, marksJunkRead: false, threshold: threshold);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(refusal);
    }

    /// <summary>The default destination is written the way an operator would have written it themselves.</summary>
    [Fact]
    public void DefaultJunkFolder_WrittenOut_ReadsBackAsTheJunkRole()
    {
        // Act
        var written = SpamActionSettings.DefaultJunkFolder.ToString();

        // Assert
        Assert.Equal($"{MailFolderReference.RoleScheme}{MailFolderSpecialUse.Junk}", written);
    }
}
