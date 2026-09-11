// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Signals;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Signals;

/// <summary>
/// Covers how a deployment says its replicas carry signals to each other: that writing nothing is the supported
/// single-replica deployment, that a section describing no endpoint is refused rather than started on, and that the
/// prefix keeping two applications apart cannot be cleared by accident.
/// </summary>
public sealed class SignalBackplaneOptionsTests
{
    /// <summary>An operator who writes no section gets the deployment they had before it existed, connected to nothing.</summary>
    [Fact]
    public void ReadFrom_AbsentSection_DescribesNoBackplane()
    {
        // Act
        var settings = SignalBackplaneOptions.ReadFrom(Configuration([]));

        // Assert
        Assert.False(settings.IsConfigured);
        Assert.Empty(settings.FindConfigurationErrors());
    }

    /// <summary>The channel prefix a deployment gets by writing nothing is a name, so two MailFathom deployments sharing one endpoint have to say which is which.</summary>
    [Fact]
    public void ReadFrom_SectionNamingOnlyTheEndpoint_TakesTheProductsOwnChannelPrefix()
    {
        // Act
        var settings = SignalBackplaneOptions.ReadFrom(Configuration(EndpointDeclared));

        // Assert
        Assert.True(settings.IsConfigured);
        Assert.Equal(SignalBackplaneOptions.DefaultChannelPrefix, settings.ChannelPrefix);
        Assert.Empty(settings.FindConfigurationErrors());
    }

    /// <summary>
    /// A section carrying everything but the endpoint describes nothing. Starting on it would leave an operator reading
    /// their own file as proof of a backplane no replica ever connected to, which is the failure the whole section
    /// exists to prevent, so it is refused and the message names the setting.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_SectionNamingNoEndpoint_IsRefused()
    {
        // Arrange
        var settings = SignalBackplaneOptions.ReadFrom(Configuration(
        [
            new("SignalBackplane:ChannelPrefix", "tenant-a"),
        ]));

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains($"{SignalBackplaneOptions.SectionName}:{nameof(SignalBackplaneOptions.ConnectionString)}", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// An emptied prefix is not a second way of saying the default: it is what two applications sharing one endpoint
    /// would receive each other's statements under, which on this channel means one deployment's users being told to
    /// re-read another deployment's mail.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_EmptiedChannelPrefix_IsRefused()
    {
        // Arrange
        var settings = SignalBackplaneOptions.ReadFrom(Configuration(
        [
            .. EndpointDeclared,
            new("SignalBackplane:ChannelPrefix", "  "),
        ]));

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains($"{SignalBackplaneOptions.SectionName}:{nameof(SignalBackplaneOptions.ChannelPrefix)}", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The section is bound strictly, so a misspelled key is reported rather than ignored: a deployment that meant to
    /// scale out and wrote one would otherwise run without a backplane while its configuration said otherwise, and the
    /// symptom of that is a client quietly stopping being told things.
    /// </summary>
    [Fact]
    public void ReadFrom_MisspelledKey_IsRefused()
    {
        // Arrange
        var configuration = Configuration(
        [
            .. EndpointDeclared,
            new("SignalBackplane:ChannelPrefixes", "tenant-a"),
        ]);

        // Act and assert
        Assert.Throws<InvalidOperationException>(() => SignalBackplaneOptions.ReadFrom(configuration));
    }

    /// <summary>The declared endpoint, which is the whole of what a usable section has to carry.</summary>
    private static KeyValuePair<string, string?>[] EndpointDeclared =>
    [
        new("SignalBackplane:ConnectionString:Name", "signal-backplane"),
        new("SignalBackplane:ConnectionString:SecretReference", "file:/etc/mailfathom/secrets/signal-backplane"),
    ];

    private static IConfiguration Configuration(IReadOnlyList<KeyValuePair<string, string?>> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
