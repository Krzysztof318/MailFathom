// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails.Authorship;
using MailFathom.Host.Configuration.Mail;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>Covers what the one authorship setting decides, and what it deliberately does not.</summary>
public sealed class MachineAuthorshipConfigurationTests
{
    /// <summary>A deployment that configured nothing still reads its mail, because the assessment needs no configuring.</summary>
    [Fact]
    public void MachineAuthorshipProfile_AnUnconfiguredDeployment_ReadsUnderTheStandardWeighting()
    {
        // Arrange
        var options = new MailSynchronizationOptions();

        // Act
        var profile = options.MachineAuthorshipProfile;

        // Assert
        Assert.True(options.AssessMachineAuthorship);
        Assert.Same(MachineAuthorshipProfile.Standard, profile);
        Assert.True(profile.IsActive);
    }

    /// <summary>Turning it off hands extraction the profile that reads nothing rather than changing what is weighed.</summary>
    [Fact]
    public void MachineAuthorshipProfile_AssessmentTurnedOff_ReadsNothing()
    {
        // Arrange
        var options = new MailSynchronizationOptions { AssessMachineAuthorship = false };

        // Act
        var profile = options.MachineAuthorshipProfile;

        // Assert
        Assert.Same(MachineAuthorshipProfile.Disabled, profile);
        Assert.False(profile.IsActive);
        Assert.False(profile.Revision.NamesAProfile);
    }

    /// <summary>The two profiles are told apart by their revision, which is what a stored answer records.</summary>
    [Fact]
    public void MachineAuthorshipProfile_TheTwoProfiles_CarryDifferentRevisions()
    {
        // Arrange
        var reading = new MailSynchronizationOptions();
        var notReading = new MailSynchronizationOptions { AssessMachineAuthorship = false };

        // Act
        var readingRevision = reading.MachineAuthorshipProfile.Revision;
        var notReadingRevision = notReading.MachineAuthorshipProfile.Revision;

        // Assert
        Assert.NotEqual(readingRevision, notReadingRevision);
        Assert.True(readingRevision.NamesAProfile);
    }
}
