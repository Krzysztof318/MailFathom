// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration;

/// <summary>Covers the refusal of a value written where the reader that needs it never looks.</summary>
/// <remarks>
/// The failure prevented here is silent by construction: the setting was accepted by the configuration pipeline, so
/// nothing about it is wrong, and the one reader that mattered had already read the environment and moved on. An
/// operator would see their own value in their own file while the process behaved as though they had set nothing.
/// </remarks>
public sealed class EnvironmentOnlySettingsTests
{
    [Fact]
    public void RejectMisplacedValues_ADeploymentConfiguringNoneOfThem_IsAccepted() =>
        EnvironmentOnlySettings.RejectMisplacedValues(
            Configuration(new Dictionary<string, string?> { ["MailboxSearch:SnippetsPerEmail"] = "3" }),
            NothingInTheEnvironment);

    /// <summary>The whole point: a file states the setting, the environment does not, and the reader sees nothing.</summary>
    [Theory]
    [InlineData("OTEL_SERVICE_NAME")]
    [InlineData("OTEL_EXPORTER_OTLP_ENDPOINT")]
    [InlineData("ASPNETCORE_ENVIRONMENT")]
    [InlineData("DOTNET_USE_POLLING_FILE_WATCHER")]
    [InlineData("OPENSSL_CONF")]
    public void RejectMisplacedValues_ASettingConfiguredOutsideTheEnvironment_IsRefusedAndNamed(string variable)
    {
        // Act
        var failure = Assert.Throws<EnvironmentOnlySettingMisplacedException>(() =>
            EnvironmentOnlySettings.RejectMisplacedValues(
                Configuration(new Dictionary<string, string?> { [variable] = "configured-elsewhere" }),
                NothingInTheEnvironment));

        // Assert
        Assert.Contains(variable, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The ordinary case: the environment provider supplied it, so configuration and the reader agree.</summary>
    [Fact]
    public void RejectMisplacedValues_ASettingSuppliedByTheEnvironment_IsAccepted() =>
        EnvironmentOnlySettings.RejectMisplacedValues(
            Configuration(new Dictionary<string, string?> { ["OTEL_SERVICE_NAME"] = "mailfathom" }),
            EnvironmentHolding(("OTEL_SERVICE_NAME", "mailfathom")));

    /// <summary>
    /// A command-line argument outranks the environment provider, so configuration reports the operator's override
    /// while the exporter keeps exporting under the name the environment carries. Nothing is missing here, which is
    /// why equality rather than presence is what this is checked on.
    /// </summary>
    [Fact]
    public void RejectMisplacedValues_ASettingOverriddenAboveTheEnvironment_IsRefused()
    {
        // Act
        var failure = Assert.Throws<EnvironmentOnlySettingMisplacedException>(() =>
            EnvironmentOnlySettings.RejectMisplacedValues(
                Configuration(new Dictionary<string, string?> { ["OTEL_SERVICE_NAME"] = "from-the-command-line" }),
                EnvironmentHolding(("OTEL_SERVICE_NAME", "from-the-environment"))));

        // Assert
        Assert.Contains("OTEL_SERVICE_NAME", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Templating a deployment manifest routinely emits an empty string for a setting nobody chose.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectMisplacedValues_ASettingLeftBlankAndAbsentFromTheEnvironment_IsAccepted(string configuredValue) =>
        EnvironmentOnlySettings.RejectMisplacedValues(
            Configuration(new Dictionary<string, string?> { ["OPENSSL_CONF"] = configuredValue }),
            NothingInTheEnvironment);

    /// <summary>An operator moving a deployment reads the whole list rather than one variable per restart.</summary>
    [Fact]
    public void RejectMisplacedValues_SeveralMisplacedSettings_AreAllNamed()
    {
        // Act
        var failure = Assert.Throws<EnvironmentOnlySettingMisplacedException>(() =>
            EnvironmentOnlySettings.RejectMisplacedValues(
                Configuration(new Dictionary<string, string?>
                {
                    ["OPENSSL_CONF"] = "/etc/mailfathom/openssl.cnf",
                    ["OTEL_SERVICE_NAME"] = "mailfathom",
                }),
                NothingInTheEnvironment));

        // Assert
        Assert.Contains("OPENSSL_CONF", failure.Message, StringComparison.Ordinal);
        Assert.Contains("OTEL_SERVICE_NAME", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The exporter reads a family of variables rather than the two MailFathom names itself, so the prefix is what is
    /// matched. A name that merely begins with the same letters is not one of them.
    /// </summary>
    [Theory]
    [InlineData("OTEL_EXPORTER_OTLP_HEADERS", true)]
    [InlineData("OTEL_RESOURCE_ATTRIBUTES", true)]
    [InlineData("OTELCOLLECTOR", false)]
    [InlineData("MailSynchronization:Accounts:0:Host", false)]
    public void RejectMisplacedValues_AKeyOutsideTheEnvironmentOnlyNames_IsLeftToItsOwnSection(
        string configurationKey,
        bool isRefused)
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?> { [configurationKey] = "a-value" });

        // Act
        var failure = Record.Exception(() =>
            EnvironmentOnlySettings.RejectMisplacedValues(configuration, NothingInTheEnvironment));

        // Assert
        Assert.Equal(isRefused, failure is EnvironmentOnlySettingMisplacedException);
    }

    /// <summary>
    /// A listener address matches the <c>ASPNETCORE_</c> family by shape and belongs to another rule entirely: it is
    /// refused from the environment too, so telling an operator to move it there would send them somewhere it also
    /// fails. <c>ExternalListenerConfiguration</c> answers all three under both of their configuration keys.
    /// </summary>
    [Theory]
    [InlineData("ASPNETCORE_URLS")]
    [InlineData("ASPNETCORE_HTTP_PORTS")]
    [InlineData("ASPNETCORE_HTTPS_PORTS")]
    public void RejectMisplacedValues_AUrlShapedListenerAddress_IsLeftToTheRuleThatOwnsIt(string variable) =>
        EnvironmentOnlySettings.RejectMisplacedValues(
            Configuration(new Dictionary<string, string?> { [variable] = "http://0.0.0.0:8080" }),
            NothingInTheEnvironment);

    /// <summary>The message names what to do, because an operator reading it is holding the file that has to change.</summary>
    [Fact]
    public void RejectMisplacedValues_AMisplacedSetting_IsReportedWithTheCorrection()
    {
        // Act
        var failure = Assert.Throws<EnvironmentOnlySettingMisplacedException>(() =>
            EnvironmentOnlySettings.RejectMisplacedValues(
                Configuration(new Dictionary<string, string?> { ["OPENSSL_CONF"] = "/etc/mailfathom/openssl.cnf" }),
                NothingInTheEnvironment));

        // Assert
        Assert.Contains("environment variable", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("/etc/mailfathom/openssl.cnf", failure.Message, StringComparison.Ordinal);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Func<string, string?> EnvironmentHolding(params (string Name, string Value)[] variables) =>
        name => variables
            .Where(variable => string.Equals(variable.Name, name, StringComparison.Ordinal))
            .Select(variable => variable.Value)
            .FirstOrDefault();

    private static string? NothingInTheEnvironment(string name) => null;
}
