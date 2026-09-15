// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Application.Mail.Export;
using MailFathom.Host.Configuration.Persistence;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Persistence;

/// <summary>Covers what a deployment may declare about a mailbox leaving it, and how long the copy is kept.</summary>
/// <remarks>
/// Every one of the three costs storage rather than processor time, and each has values that would either hold a second
/// copy of every exported mailbox for a week or leave a finished archive unreachable before anybody could fetch it. A
/// deployment that declared one is refused while it starts rather than discovered by an operator whose download expired.
/// </remarks>
public sealed class MailboxExportOptionsTests
{
    /// <summary>A deployment that says nothing about exports runs these, so they have to be usable unchanged.</summary>
    [Fact]
    public void Validate_TheDefaults_AreAccepted()
    {
        // Act
        var results = Validate(new MailboxExportOptions());

        // Assert
        Assert.Empty(results);
    }

    /// <summary>
    /// Below a mebibyte the limit refuses every mailbox there is, and past a tebibyte it is no longer a bound anybody's
    /// storage could honour — an archive is a second full copy of the mailbox for as long as it is kept.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData((1L * 1024 * 1024) - 1)]
    [InlineData((1024L * 1024 * 1024 * 1024) + 1)]
    public void Validate_ASizeLimitOutsideItsRange_IsRefusedNamingTheKey(long maximumArchiveByteLength)
    {
        // Arrange
        var options = new MailboxExportOptions { MaximumArchiveByteLength = maximumArchiveByteLength };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(
                nameof(MailboxExportOptions.MaximumArchiveByteLength),
                StringComparer.Ordinal));
    }

    /// <summary>Both ends of the range are declarations somebody meant — one turns the export down to nothing, the other is the ceiling.</summary>
    [Theory]
    [InlineData(1L * 1024 * 1024)]
    [InlineData(1024L * 1024 * 1024 * 1024)]
    public void Validate_ASizeLimitAtEitherEndOfTheRange_IsAccepted(long maximumArchiveByteLength)
    {
        // Arrange
        var options = new MailboxExportOptions { MaximumArchiveByteLength = maximumArchiveByteLength };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
    }

    /// <summary>
    /// Under five minutes an archive expires while the download that was waiting for it is still being arranged, and past
    /// a week the deployment is holding every exported mailbox twice for longer than anybody asked it to.
    /// </summary>
    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:04:59")]
    [InlineData("7.00:00:01")]
    public void Validate_ARetentionPeriodOutsideItsRange_IsRefusedNamingTheKey(string retention)
    {
        // Arrange
        var options = new MailboxExportOptions { Retention = TimeSpan.Parse(retention, null) };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(MailboxExportOptions.Retention), StringComparer.Ordinal));
    }

    /// <summary>
    /// A sweep that runs every minute reads a table nothing has written to, and one that runs once a day leaves an
    /// archive whose period ended just after a pass sitting in the bucket for another day.
    /// </summary>
    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:00:59")]
    [InlineData("1.00:00:01")]
    public void Validate_ASweepIntervalOutsideItsRange_IsRefusedNamingTheKey(string interval)
    {
        // Arrange
        var options = new MailboxExportOptions { ExpirySweepInterval = TimeSpan.Parse(interval, null) };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(
                nameof(MailboxExportOptions.ExpirySweepInterval),
                StringComparer.Ordinal));
    }

    /// <summary>Every faulty setting is reported, because an operator repairing one at a time repairs one per restart.</summary>
    [Fact]
    public void Validate_ABlockWrongInEveryWay_ReportsEachSettingSeparately()
    {
        // Arrange
        var options = new MailboxExportOptions
        {
            MaximumArchiveByteLength = 0,
            Retention = TimeSpan.Zero,
            ExpirySweepInterval = TimeSpan.Zero,
        };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Equal(3, results.Count);
    }

    /// <summary>The two bounds the use case refuses an export against are read from the declaration rather than restated beside it.</summary>
    [Fact]
    public void ToExportSettings_ADeclaredBlock_CarriesBothBoundsAnExportIsTakenUnder()
    {
        // Arrange
        var options = new MailboxExportOptions
        {
            MaximumArchiveByteLength = 3L * 1024 * 1024 * 1024,
            Retention = TimeSpan.FromHours(6),
        };

        // Act
        var settings = options.ToExportSettings();

        // Assert
        Assert.Equal(
            (3L * 1024 * 1024 * 1024, TimeSpan.FromHours(6)),
            (settings.MaximumArchiveByteLength, settings.Retention));
    }

    /// <summary>The sweep interval paces this deployment rather than bounding an export, so it reaches no caller's settings.</summary>
    [Fact]
    public void ToExportSettings_ADeploymentThatDeclaresNothing_CarriesTheDocumentedDefaults()
    {
        // Act
        var settings = new MailboxExportOptions().ToExportSettings();

        // Assert
        Assert.Equal(MailboxExportSettings.Default, settings);
    }

    private static List<ValidationResult> Validate(MailboxExportOptions options)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return results;
    }
}
