// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration;

/// <summary>Names the configuration files a deployment provisions outside the application's own content root.</summary>
/// <param name="DirectoryPath">A directory whose JSON files are layered in, or <see langword="null" /> when none is configured.</param>
/// <param name="FilePath">One JSON file layered in above the directory, or <see langword="null" /> when none is configured.</param>
/// <remarks>
/// <para>
/// The two shapes exist because provisioning systems produce both and neither is the fallback. A Kubernetes ConfigMap
/// mounted as a volume becomes a directory holding one file per key, while a Compose bind mount, a systemd drop-in, or
/// a ConfigMap mounted with <c>subPath</c> produces a single file. Naming them separately keeps an operator's intent
/// explicit, so an absent path is reported against the shape they configured rather than against a guess.
/// </para>
/// <para>
/// Both values are read from ordinary configuration rather than from the environment directly, which is what lets the
/// same setting arrive as an environment variable in a container, as a command-line argument under systemd, and from
/// <c>appsettings.json</c> during local development, without a second mechanism per deployment shape.
/// </para>
/// </remarks>
internal sealed record ProvisionedConfigurationPaths(string? DirectoryPath, string? FilePath)
{
    /// <summary>The configuration section both settings live in.</summary>
    public const string SectionName = "ConfigurationSources";

    /// <summary>The configuration key naming a directory of JSON configuration files.</summary>
    public const string DirectoryKey = $"{SectionName}:{DirectorySettingName}";

    /// <summary>The configuration key naming a single JSON configuration file.</summary>
    public const string FileKey = $"{SectionName}:{FileSettingName}";

    private const string DirectorySettingName = "Directory";
    private const string FileSettingName = "File";

    /// <summary>Gets a value indicating whether the deployment provisioned any configuration at all.</summary>
    public bool AreConfigured => this.DirectoryPath is not null || this.FilePath is not null;

    /// <summary>Reads both paths from the configuration the host has already bound.</summary>
    /// <param name="configuration">The configuration to read the section from.</param>
    /// <returns>The provisioned paths, each <see langword="null" /> when the deployment configured none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <exception cref="ProvisionedConfigurationSourceInvalidException">Thrown when the section carries a setting MailFathom does not define.</exception>
    /// <remarks>
    /// A blank value reads as unconfigured rather than as a path that happens to be empty. Templating a deployment
    /// manifest routinely produces an empty string for a value the operator left unset, and treating that as a path
    /// would fail startup over a setting nobody chose.
    /// </remarks>
    public static ProvisionedConfigurationPaths ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);

        RejectUnusableSettings(section);

        return new ProvisionedConfigurationPaths(
            NullWhenBlank(section[DirectorySettingName]),
            NullWhenBlank(section[FileSettingName]));
    }

    /// <summary>Fails when the section carries a setting MailFathom does not define, or a defined one that is not a path.</summary>
    /// <remarks>
    /// <para>
    /// The section is checked by hand rather than through <c>ErrorOnUnknownConfiguration</c>, which the rest of the host
    /// uses, because these two settings decide which sources exist and are therefore read before the options framework
    /// the binder belongs to. The rule is the same one, for the same reason: a misspelled <c>Directroy</c> that bound
    /// nothing would leave the host running on defaults while the operator believed their mount was in force, which is
    /// precisely the failure these settings exist to remove.
    /// </para>
    /// <para>
    /// A defined setting that carries descendants rather than a value is the same failure wearing the right name. A
    /// flattening provider can express one — <c>ConfigurationSources__Directory__Path=/etc/mailfathom/config</c> produces a
    /// child called <c>Directory</c> whose own value is absent — and reading it as a path yields nothing, so a check
    /// that looked only at the name would accept the setting and start the host without the mount.
    /// </para>
    /// </remarks>
    private static void RejectUnusableSettings(IConfigurationSection section)
    {
        string[] children = [.. section.GetChildren().Select(child => child.Key).Order(StringComparer.OrdinalIgnoreCase)];

        var unknownSettingNames = children.Where(IsUnknownSettingName).ToArray();

        if (unknownSettingNames.Length > 0)
        {
            throw new ProvisionedConfigurationSourceInvalidException(
                $"{SectionName} carries settings MailFathom does not define: {string.Join(", ", unknownSettingNames)}. "
                + $"The section defines {DirectorySettingName} and {FileSettingName}.");
        }

        var structuredSettingNames = children
            .Where(settingName => section.GetSection(settingName).GetChildren().Any())
            .ToArray();

        if (structuredSettingNames.Length > 0)
        {
            throw new ProvisionedConfigurationSourceInvalidException(
                $"{SectionName} carries settings that are not a path: {string.Join(", ", structuredSettingNames)}. "
                + $"{DirectorySettingName} and {FileSettingName} each take one path and no nested value.");
        }
    }

    private static bool IsUnknownSettingName(string settingName) =>
        !string.Equals(settingName, DirectorySettingName, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(settingName, FileSettingName, StringComparison.OrdinalIgnoreCase);

    private static string? NullWhenBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
