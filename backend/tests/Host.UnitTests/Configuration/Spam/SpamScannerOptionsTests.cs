// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Spam;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Spam;

/// <summary>Covers what the scanner block refuses at startup rather than on somebody's mail.</summary>
public sealed class SpamScannerOptionsTests
{
    /// <summary>A deployment that never asked for a scanner is not required to describe one.</summary>
    /// <remarks>
    /// This is what makes the sidecar deployable only when the switch is on: the block binds to its defaults, nothing
    /// validates an address that will never be dialled, and no daemon conversation is constructed for it.
    /// </remarks>
    [Fact]
    public void FindErrors_ABlockSettingNothingWhileTheScannerIsOff_ReportsNoError()
    {
        // Arrange
        var options = new SpamScannerOptions();

        // Act
        var errors = options.FindErrors(useScanner: false).ToArray();

        // Assert
        Assert.Empty(errors);
        Assert.Null(options.Host);
    }

    /// <summary>Every bound a deployment did not state is the documented default.</summary>
    [Fact]
    public void Port_ABlockStatingOnlyAHost_CarriesTheDocumentedDefaults()
    {
        // Arrange, Act
        var options = new SpamScannerOptions { Host = "mailfathom-spamassassin" };

        // Assert
        Assert.Equal(783, options.Port);
        Assert.Equal(SpamScannerOptions.DefaultScanTimeoutSeconds, options.ScanTimeoutSeconds);
        Assert.Equal(SpamScannerOptions.DefaultMaximumMessageBytes, options.MaximumMessageBytes);
        Assert.Equal(SpamScannerOptions.DefaultMaximumConcurrentScans, options.MaximumConcurrentScans);
        Assert.Empty(options.FindErrors(useScanner: true));
    }

    /// <summary>A scanner switched on with nowhere to ask is refused, because the quiet answer would be wrong twice.</summary>
    /// <remarks>
    /// Nothing would fail: every message would keep the verdict its headers reached while the configuration said a
    /// corpus had read it. The refusal names the key and the shape of a good value, and no address.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FindErrors_AScannerSwitchedOnWithNoHost_IsRefusedNamingTheKey(string? host)
    {
        // Arrange
        var options = new SpamScannerOptions { Host = host };

        // Act
        var error = Assert.Single(options.FindErrors(useScanner: true));

        // Assert
        Assert.Equal([nameof(SpamScannerOptions.Host)], error.MemberNames);
        Assert.Contains(nameof(SpamScannerOptions.Host), error.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>Every bound is refused outside its stated range, whether or not a scanner was asked for.</summary>
    /// <remarks>
    /// The bounds are checked even with the scanner off, unlike the host: a value written wrong stays written, and an
    /// operator finding out about it in the run that first switches scanning on learns it at the worst moment.
    /// </remarks>
    [Theory]
    [InlineData(0, 30, 512_000, 5, nameof(SpamScannerOptions.Port))]
    [InlineData(65_536, 30, 512_000, 5, nameof(SpamScannerOptions.Port))]
    [InlineData(783, 0, 512_000, 5, nameof(SpamScannerOptions.ScanTimeoutSeconds))]
    [InlineData(783, 121, 512_000, 5, nameof(SpamScannerOptions.ScanTimeoutSeconds))]
    [InlineData(783, 30, 31_999, 5, nameof(SpamScannerOptions.MaximumMessageBytes))]
    [InlineData(783, 30, (32 * 1024 * 1024) + 1, 5, nameof(SpamScannerOptions.MaximumMessageBytes))]
    [InlineData(783, 30, 512_000, 0, nameof(SpamScannerOptions.MaximumConcurrentScans))]
    [InlineData(783, 30, 512_000, 65, nameof(SpamScannerOptions.MaximumConcurrentScans))]
    public void FindErrors_ABoundOutsideItsRange_IsRefusedNamingThatKey(
        int port,
        int scanTimeoutSeconds,
        int maximumMessageBytes,
        int maximumConcurrentScans,
        string expectedKey)
    {
        // Arrange
        var options = new SpamScannerOptions
        {
            Host = "mailfathom-spamassassin",
            Port = port,
            ScanTimeoutSeconds = scanTimeoutSeconds,
            MaximumMessageBytes = maximumMessageBytes,
            MaximumConcurrentScans = maximumConcurrentScans,
        };

        // Act
        var errorWithTheScannerOn = Assert.Single(options.FindErrors(useScanner: true));
        var errorWithTheScannerOff = Assert.Single(options.FindErrors(useScanner: false));

        // Assert
        Assert.Equal([expectedKey], errorWithTheScannerOn.MemberNames);
        Assert.Equal([expectedKey], errorWithTheScannerOff.MemberNames);
    }

    /// <summary>Each end of every range is accepted, so a documented limit is a value an operator may actually write.</summary>
    [Fact]
    public void FindErrors_EveryBoundAtTheEdgeOfItsRange_IsAccepted()
    {
        // Arrange
        var smallest = new SpamScannerOptions
        {
            Host = "mailfathom-spamassassin",
            Port = 1,
            ScanTimeoutSeconds = SpamScannerOptions.SmallestScanTimeoutSeconds,
            MaximumMessageBytes = SpamScannerOptions.SmallestMaximumMessageBytes,
            MaximumConcurrentScans = 1,
        };
        var largest = new SpamScannerOptions
        {
            Host = "mailfathom-spamassassin",
            Port = 65_535,
            ScanTimeoutSeconds = SpamScannerOptions.LargestScanTimeoutSeconds,
            MaximumMessageBytes = SpamScannerOptions.LargestMaximumMessageBytes,
            MaximumConcurrentScans = SpamScannerOptions.LargestMaximumConcurrentScans,
        };

        // Act
        var errors = smallest.FindErrors(useScanner: true).Concat(largest.FindErrors(useScanner: true)).ToArray();

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A block that validated becomes the profile the adapter dials, seconds turned into an interval.</summary>
    [Fact]
    public void ToProfile_AValidatedBlock_BecomesTheProfileTheAdapterDials()
    {
        // Arrange
        var options = new SpamScannerOptions
        {
            Host = "spamassassin.mailfathom.svc.cluster.local",
            Port = 1783,
            ScanTimeoutSeconds = 45,
            MaximumMessageBytes = 256_000,
            MaximumConcurrentScans = 8,
        };

        // Act
        var profile = options.ToProfile();

        // Assert
        Assert.Equal("spamassassin.mailfathom.svc.cluster.local", profile.Host);
        Assert.Equal(1783, profile.Port);
        Assert.Equal(TimeSpan.FromSeconds(45), profile.ScanTimeout);
        Assert.Equal(256_000, profile.MaximumMessageBytes);
        Assert.Equal(8, profile.MaximumConcurrentScans);
    }

    /// <summary>Composing a profile from a block that named no host is a wiring mistake rather than a configuration one.</summary>
    /// <remarks>
    /// Validation refuses that combination before anything resolves the profile, so reaching here means the registration
    /// stopped agreeing with the validation. It says so instead of dialling nowhere.
    /// </remarks>
    [Fact]
    public void ToProfile_ABlockThatNamedNoHost_SaysSoRatherThanComposingAnAddress()
    {
        // Arrange
        var options = new SpamScannerOptions();

        // Act, Assert
        _ = Assert.Throws<InvalidOperationException>(options.ToProfile);
    }
}
