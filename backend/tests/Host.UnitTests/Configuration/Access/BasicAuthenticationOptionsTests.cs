// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Host.Configuration.Access;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Access;

/// <summary>Covers the one setting the block carries, which is the bound on guessing a password.</summary>
public sealed class BasicAuthenticationOptionsTests
{
    private const string SettingPath = "ClientEndpoint:Authentication:0:Basic";

    /// <summary>A block written with nothing in it is the ordinary way to select the method, so its default has to be usable.</summary>
    [Fact]
    public void FindConfigurationErrors_ABlockStatingNothing_ReportsNothing()
    {
        // Arrange
        var options = new BasicAuthenticationOptions();

        // Act, Assert
        Assert.Empty(options.FindConfigurationErrors(SettingPath));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(BasicAuthenticationOptions.MaximumAttemptsPerMinute)]
    public void FindConfigurationErrors_ABoundInsideWhatTheDeploymentWillRunUnder_ReportsNothing(int attemptsPerMinute)
    {
        // Arrange
        var options = new BasicAuthenticationOptions { AttemptsPerMinute = attemptsPerMinute };

        // Act, Assert
        Assert.Empty(options.FindConfigurationErrors(SettingPath));
    }

    /// <summary>Zero would refuse every owner and the ceiling is an offline guessing rate, so both ends are a misreading rather than a posture.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(BasicAuthenticationOptions.MaximumAttemptsPerMinute + 1)]
    public void FindConfigurationErrors_ABoundOutsideThatRange_IsRefusedNamingItsPathAndTheCeiling(int attemptsPerMinute)
    {
        // Arrange
        var options = new BasicAuthenticationOptions { AttemptsPerMinute = attemptsPerMinute };

        // Act
        var errors = options.FindConfigurationErrors(SettingPath);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains($"{SettingPath}:{nameof(BasicAuthenticationOptions.AttemptsPerMinute)}", reported, StringComparison.Ordinal);
        Assert.Contains(BasicAuthenticationOptions.MaximumAttemptsPerMinute.ToString(CultureInfo.InvariantCulture), reported, StringComparison.Ordinal);
    }
}
