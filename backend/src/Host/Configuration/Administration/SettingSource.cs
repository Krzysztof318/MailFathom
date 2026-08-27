// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Administration;

/// <summary>Names which layer of the composed configuration an effective value came from.</summary>
/// <remarks>
/// <para>
/// A closed enumeration rather than a C# <see langword="enum" /> because the name travels: it is what an
/// administrative reading answers with, what <c>mfctl config get</c> prints beside a value, and what a refused write
/// names as the source that would have beaten it. A member rename must not change any of the three.
/// </para>
/// <para>
/// The set is the precedence in <c>docs/operations/configuration-sources.md</c> collapsed to what an operator acts on.
/// The three above the persisted layer are named apart because each is repaired somewhere different — an argument on
/// the command line, a variable in the unit or the manifest, a store on the developer's own machine — while every file
/// beneath it is one answer, since what distinguishes two of them is the path, which travels beside the source rather
/// than inside it.
/// </para>
/// <para>
/// <see cref="Unclassified" /> is not a fallback for a source MailFathom forgot. It names a provider this build does
/// not recognize, which a deployment cannot introduce and a host composed in a test can, and reporting it honestly is
/// what keeps a reading from calling an in-memory provider a file.
/// </para>
/// </remarks>
public readonly record struct SettingSource
{
    private readonly string? name;

    private SettingSource(string name) => this.name = name;

    /// <summary>Gets the source naming a value the process was started with.</summary>
    public static SettingSource CommandLine { get; } = new("command-line");

    /// <summary>Gets the source naming a value the process's environment supplies.</summary>
    public static SettingSource EnvironmentVariable { get; } = new("environment-variable");

    /// <summary>Gets the source naming a value the developer's own User Secrets store supplies, which exists in the <c>Development</c> environment alone.</summary>
    public static SettingSource UserSecrets { get; } = new("user-secrets");

    /// <summary>Gets the source naming a value the deployment persisted, which is the layer these commands write.</summary>
    public static SettingSource PersistedLayer { get; } = new("persisted-layer");

    /// <summary>Gets the source naming a value a JSON configuration file supplies, whether the deployment provisioned it or the image carries it.</summary>
    public static SettingSource File { get; } = new("file");

    /// <summary>Gets the source naming a provider this build does not recognize.</summary>
    public static SettingSource Unclassified { get; } = new("unclassified");

    /// <summary>Gets every source a reading reports.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs, and in precedence order so a reading that lists them says which beats which.</remarks>
    public static IReadOnlyList<SettingSource> All { get; } =
        [CommandLine, EnvironmentVariable, UserSecrets, PersistedLayer, File, Unclassified];

    /// <summary>Gets whether this value names a source rather than the unusable struct default.</summary>
    public bool IsSpecified => this.name is not null;

    /// <summary>Gets the published name, which is what a reading answers with and what an operator reads.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a source.</exception>
    public string Name => this.name
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a configuration source.");

    /// <summary>Reports the source by its published name.</summary>
    /// <param name="name">The name to resolve.</param>
    /// <param name="source">The source the name publishes, or the struct default when no source publishes it.</param>
    /// <returns><see langword="true" /> when the name is one this build publishes.</returns>
    public static bool TryParse(string? name, out SettingSource source)
    {
        source = All.FirstOrDefault(candidate => string.Equals(candidate.name, name, StringComparison.Ordinal));

        return source.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}
