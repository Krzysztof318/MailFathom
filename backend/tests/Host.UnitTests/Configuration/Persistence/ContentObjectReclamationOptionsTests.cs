// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Persistence;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Persistence;

/// <summary>Covers the two settings that bound how long mail whose record is gone can still exist as bytes.</summary>
/// <remarks>
/// The age floor is the one worth refusing a deployment over. An object is written before the unit of work that points
/// at it commits, so a floor short enough to reach a write in flight turns reclamation into a way of losing mail — and
/// a startup that accepted such a value would say nothing until the day it did.
/// </remarks>
public sealed class ContentObjectReclamationOptionsTests
{
    /// <summary>The shipped defaults are what a deployment that writes nothing here is swept under.</summary>
    [Fact]
    public void FindConfigurationErrors_TheDefaults_ReportNothing()
    {
        // Arrange
        var options = new ContentObjectReclamationOptions();

        // Act
        var errors = options.FindConfigurationErrors().ToArray();

        // Assert
        Assert.Empty(errors);
        Assert.Equal(TimeSpan.FromHours(24), options.MinimumObjectAge);
    }

    /// <summary>A schedule this system cannot read would leave the sweep never dispatched and nothing saying so.</summary>
    [Fact]
    public void FindConfigurationErrors_AScheduleThatNamesNoRecurrence_FailsStartupNamingTheKey()
    {
        // Arrange
        var options = new ContentObjectReclamationOptions { Schedule = "whenever" };

        // Act
        var errors = options.FindConfigurationErrors().ToArray();

        // Assert
        var error = Assert.Single(errors);

        Assert.Contains("ContentStorage:ObjectStorage:Reclamation:Schedule", error, StringComparison.Ordinal);
    }

    /// <summary>Below the floor a sweep could reach an object whose unit of work has not committed yet.</summary>
    [Fact]
    public void FindConfigurationErrors_AnAgeFloorShortEnoughToReachAWriteInFlight_FailsStartup()
    {
        // Arrange
        var options = new ContentObjectReclamationOptions { MinimumObjectAge = TimeSpan.FromSeconds(30) };

        // Act
        var errors = options.FindConfigurationErrors().ToArray();

        // Assert
        var error = Assert.Single(errors);

        Assert.Contains("ContentStorage:ObjectStorage:Reclamation:MinimumObjectAge", error, StringComparison.Ordinal);
        Assert.Contains("mail being lost rather than reclaimed", error, StringComparison.Ordinal);
    }

    /// <summary>The floor is also the promise about how long bytes outlive their record, so a long one is a retention decision.</summary>
    [Fact]
    public void FindConfigurationErrors_AnAgeFloorBeyondTheRetentionBound_FailsStartup()
    {
        // Arrange
        var options = new ContentObjectReclamationOptions { MinimumObjectAge = TimeSpan.FromDays(90) };

        // Act
        var errors = options.FindConfigurationErrors().ToArray();

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains(
                "ContentStorage:ObjectStorage:Reclamation:MinimumObjectAge",
                StringComparison.Ordinal));
    }

    /// <summary>A run that may examine less than one listed page would hand on after every page and reclaim nothing.</summary>
    [Fact]
    public void FindConfigurationErrors_AnObjectCeilingBelowOnePage_FailsStartup()
    {
        // Arrange
        var options = new ContentObjectReclamationOptions { MaximumObjectsPerRun = 10 };

        // Act
        var errors = options.FindConfigurationErrors().ToArray();

        // Assert
        var error = Assert.Single(errors);

        Assert.Contains(
            "ContentStorage:ObjectStorage:Reclamation:MaximumObjectsPerRun",
            error,
            StringComparison.Ordinal);
    }

    /// <summary>What the sweep is held to is exactly what the deployment declared, or the validation above bounds nothing.</summary>
    [Fact]
    public void ToBounds_AUsableDeclaration_CarriesTheDeclaredFloorAndCeiling()
    {
        // Arrange
        var options = new ContentObjectReclamationOptions
        {
            MinimumObjectAge = TimeSpan.FromHours(6),
            MaximumObjectsPerRun = 5000,
        };

        // Act
        var bounds = options.ToBounds();

        // Assert
        Assert.Equal(TimeSpan.FromHours(6), bounds.MinimumObjectAge);
        Assert.Equal(5000, bounds.MaximumObjectsPerRun);
    }

    /// <summary>The occasions the sweep is dispatched on are the ones an operator wrote.</summary>
    [Fact]
    public void ToRecurrence_AUsableDeclaration_ReadsTheDeclaredOccasions()
    {
        // Arrange
        var options = new ContentObjectReclamationOptions { Schedule = "Daily at 02:30" };

        // Act
        var recurrence = options.ToRecurrence();

        // Assert
        Assert.Equal("daily:02:30:UTC", recurrence.CanonicalForm);
    }

    /// <summary>Composition runs after validation, so reaching it with an unreadable schedule is a defect rather than a setting.</summary>
    [Fact]
    public void ToRecurrence_AScheduleThatNamesNoRecurrence_IsRefused()
    {
        // Arrange
        var options = new ContentObjectReclamationOptions { Schedule = "whenever" };

        // Act, Assert
        Assert.Throws<InvalidOperationException>(() => options.ToRecurrence());
    }
}
