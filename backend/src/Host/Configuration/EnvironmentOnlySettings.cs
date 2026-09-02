// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration;

/// <summary>Refuses a value written for a setting that only the process environment can deliver.</summary>
/// <remarks>
/// <para>
/// Nearly every MailFathom setting is composed: it may arrive from <c>appsettings.json</c>, from a deployment-provisioned
/// file, from the environment, or from a command-line argument, and a later source overrides an earlier one. A few
/// settings cannot join that composition, and each for a reason that is real rather than an omission. The bootstrap
/// logging pipeline is built before <c>WebApplication.CreateBuilder</c>, because a malformed configuration file is one
/// of the failures it exists to report, so it cannot wait for configuration to exist. The OpenTelemetry exporter reads
/// its own <c>OTEL_*</c> variables from the environment directly. The .NET host settles its environment name before the
/// application's configuration is composed. OpenSSL reads <c>OPENSSL_CONF</c> while it initializes, long before any of
/// this runs.
/// </para>
/// <para>
/// What none of those readers can do is notice a value it was never shown. An operator who writes
/// <c>OTEL_SERVICE_NAME</c> into a mounted ConfigMap, or passes <c>--OPENSSL_CONF</c> on the command line, configures
/// something the pipeline accepts, stores, and reads back — while the process runs exactly as though they had set
/// nothing. This is where that becomes a startup failure naming the variable instead, which is the same treatment
/// <see cref="Endpoints.ExternalListenerConfiguration" /> gives a listener named outside the section that owns one, and
/// for the same reason: a setting that decides nothing must say so rather than be quietly dropped.
/// </para>
/// <para>
/// Equality against the environment is what is checked, not mere presence. The environment-variable provider puts these
/// names into configuration too, so a value that came from the environment is expected to appear here and is exactly
/// what a correct deployment looks like. A command-line argument outranks that provider, so it can leave configuration
/// reporting one value while the reader keeps using another — a divergence presence alone would never see.
/// </para>
/// <para>
/// The URL-shaped listener addresses are excluded below rather than covered. <c>ASPNETCORE_URLS</c> and its two
/// siblings match the <c>ASPNETCORE_</c> family by shape, but they are not environment-only: they are refused from
/// every source, the environment included, because no MailFathom surface is served from one at all.
/// <see cref="Endpoints.ExternalListenerConfiguration" /> owns them under both of their configuration keys, and
/// answering them here as well would tell an operator to move a value into the environment that is refused there too.
/// </para>
/// </remarks>
internal static class EnvironmentOnlySettings
{
    /// <summary>The families of variables read from the process environment by something that never consults configuration.</summary>
    /// <remarks>
    /// Prefixes rather than names, because two of the three readers take a family. The OpenTelemetry SDK reads the
    /// whole <c>OTEL_*</c> set — the endpoint, the protocol, the headers, the timeout, the resource attributes — and
    /// naming only the two MailFathom reads itself would leave the rest silently ignorable. The host's own
    /// <c>ASPNETCORE_*</c> and <c>DOTNET_*</c> names reach the runtime and the host builder before the application's
    /// configuration is composed, and neither is a shape any MailFathom section uses.
    /// </remarks>
    private static readonly string[] EnvironmentOnlyKeyPrefixes = ["ASPNETCORE_", "DOTNET_", "OTEL_"];

    /// <summary>The individual variables belonging to no family above.</summary>
    /// <remarks>
    /// <c>OPENSSL_CONF</c> is OpenSSL's own name rather than MailFathom's, which is why it carries no product prefix
    /// and why it stands alone here.
    /// </remarks>
    private static readonly string[] EnvironmentOnlyKeys = ["OPENSSL_CONF"];

    /// <summary>The variables matching a family above that belong to another rule entirely.</summary>
    /// <remarks>
    /// Each names a listener, which no source may name; <see cref="Endpoints.ExternalListenerConfiguration" /> refuses
    /// all three under both of their configuration keys and states where the address belongs instead.
    /// </remarks>
    private static readonly string[] KeysOwnedByAnotherRule =
        ["ASPNETCORE_URLS", "ASPNETCORE_HTTP_PORTS", "ASPNETCORE_HTTPS_PORTS"];

    /// <summary>Fails startup when a setting only the environment can deliver carries a value from anywhere else.</summary>
    /// <param name="configuration">The composed application configuration, read at the root because these names belong to the platform rather than to a MailFathom section.</param>
    /// <param name="readEnvironmentVariable">Reads one environment variable by name, returning <see langword="null" /> when it is unset.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> or <paramref name="readEnvironmentVariable" /> is <see langword="null" />.</exception>
    /// <exception cref="EnvironmentOnlySettingMisplacedException">Thrown when any such setting carries a value the environment does not.</exception>
    /// <remarks>
    /// Every misplaced setting is reported in one message, so an operator moving a deployment reads the whole list
    /// rather than discovering one variable per restart.
    /// </remarks>
    public static void RejectMisplacedValues(
        IConfiguration configuration,
        Func<string, string?> readEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(readEnvironmentVariable);

        string[] misplacedVariables =
        [
            .. configuration.GetChildren()
                .Where(setting => IsEnvironmentOnly(setting.Key))
                .Where(setting => !ArrivedFromTheEnvironment(setting, readEnvironmentVariable))
                .Select(setting => setting.Key)
                .Order(StringComparer.Ordinal),
        ];

        if (misplacedVariables.Length == 0)
        {
            return;
        }

        throw new EnvironmentOnlySettingMisplacedException(
            $"Settings only the process environment can deliver carry a value that did not come from it: "
            + $"{string.Join(", ", misplacedVariables)}. Each is read before MailFathom's configuration exists, or by a "
            + "library that never consults it, so a value written into an appsettings file, a provisioned "
            + "configuration file, the persisted configuration document, or a command-line argument reaches nobody. "
            + "Set each as an environment variable on the host process, or remove it.");
    }

    private static bool IsEnvironmentOnly(string configurationKey) =>
        !KeysOwnedByAnotherRule.Contains(configurationKey, StringComparer.OrdinalIgnoreCase)
        && (EnvironmentOnlyKeyPrefixes.Any(prefix =>
                configurationKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            || EnvironmentOnlyKeys.Contains(configurationKey, StringComparer.OrdinalIgnoreCase));

    /// <summary>Reports whether the composed value is the one the reader itself will see.</summary>
    /// <remarks>
    /// A blank value reads as unset on both sides rather than as a value that happens to be empty, for the same reason
    /// <see cref="Provisioning.ProvisionedConfigurationPaths" /> treats one that way: templating a deployment manifest
    /// routinely emits an empty string for a setting the operator left alone, and failing startup over one would refuse
    /// a deployment nobody misconfigured.
    /// </remarks>
    private static bool ArrivedFromTheEnvironment(
        IConfigurationSection setting,
        Func<string, string?> readEnvironmentVariable) =>
        string.Equals(
            NullWhenBlank(setting.Value),
            NullWhenBlank(readEnvironmentVariable(setting.Key)),
            StringComparison.Ordinal);

    private static string? NullWhenBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
