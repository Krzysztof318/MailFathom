// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Application.Configuration;

/// <summary>Declares the settings the persisted configuration layer is itself reached through, which it may not carry.</summary>
/// <remarks>
/// <para>
/// Reading the layer needs settings of its own: where the database is, how long its one statement may run, how the
/// secret reference carrying its credential is interpreted, and which configuration sources exist at all. Those are
/// read from the sources beneath the layer, because a persisted value for one of them could not be read without first
/// reading it — so the read is not circular, and the list below is exactly what makes it so.
/// </para>
/// <para>
/// What that leaves is a split rather than a circle, and the split is why a persisted value for one is refused rather
/// than ignored. The layer is composed above every file, so a persisted <c>Persistence:Password</c> would leave the
/// bootstrap read authenticating with the file's credential while the connection pool, the schema gate, and every
/// worker authenticated with the persisted one, and nothing in the running process would report the disagreement. A
/// persisted <c>Secrets:Interpretation</c> is worse than a split: it decides whether a plain-text value written where
/// a reference belongs fails startup or is accepted, for the whole secret-resolution graph, which would make the layer
/// a way to relax the terms the layer itself is trusted under. <c>Persistence:CommandTimeoutSeconds</c> is the same
/// split one turn later: it bounds the statement that fetched the document, so a persisted value would bound the
/// connection pool and the schema gate while the read that fetched it ran at the file's bound.
/// </para>
/// <para>
/// Dropping the keys from the published snapshot was the other candidate and is refused. A document that persists one
/// is a mistake an operator made, and a mistake silently discarded is one they go on believing they fixed; the layer
/// is read once at startup, so refusing costs a start that would otherwise have run on settings nobody could see.
/// </para>
/// <para>
/// The same list is what makes these settings non-writable. A write that landed on one would persist into the layer it
/// is required to open, so <see cref="ConfigurationStorageCatalog.ResolveWriteTarget" /> refuses it at the point a
/// write is validated rather than leaving it to be discovered as a deployment that no longer starts.
/// </para>
/// </remarks>
public static class BootstrapOnlySettings
{
    /// <summary>The keys read before the layer exists, each refused along with anything nested beneath it.</summary>
    /// <remarks>
    /// Paths rather than exact names, because two of them are sections rather than values: <c>Persistence:Password</c>
    /// is a secret block whose reference and store live under it, and refusing only the section's own key would admit
    /// <c>Persistence:Password:SecretReference</c>, which is the half that decides the credential. The rest are scalars
    /// and cover themselves.
    /// </remarks>
    private static readonly string[] RefusedPaths =
    [
        "ConnectionStrings:mailfathom",
        "Persistence:ConnectionString",
        "Persistence:Password",
        "Persistence:CommandTimeoutSeconds",
        "Secrets:Interpretation",
        "ConfigurationSources:Directory",
        "ConfigurationSources:File",
    ];

    /// <summary>Finds the refused settings a persisted document carries.</summary>
    /// <param name="persistedKeys">Every configuration key the document flattened to.</param>
    /// <returns>The refused settings the document carries, ordered as they are declared, empty when it carries none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="persistedKeys" /> is <see langword="null" />.</exception>
    public static IReadOnlyList<string> FindIn(IEnumerable<string> persistedKeys)
    {
        ArgumentNullException.ThrowIfNull(persistedKeys);

        return SettingPath.FindReachedIn(RefusedPaths, persistedKeys);
    }

    /// <summary>Finds the refused setting a write would carry.</summary>
    /// <param name="configurationPath">The path a write targets, which may name a refused setting, a value nested beneath one, or a section containing one.</param>
    /// <param name="refusedSetting">The refused setting the write would reach, when it reaches one; otherwise <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the path may only come from beneath the persisted layer.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="configurationPath" /> is <see langword="null" />, empty, or white space.</exception>
    /// <remarks>
    /// The match runs both ways, because a write carries the subtree it names rather than a single value. A path
    /// beneath a refused section is the credential half of it; a path *containing* one — a write addressed at
    /// <c>Persistence</c>, or at <c>Secrets</c> — would persist the refused setting as a child, and the deployment
    /// would then be locked out of its own configuration by a write that had been validated and accepted, since the
    /// next start refuses the whole document. A more specific write reaches neither and is unaffected.
    /// </remarks>
    public static bool TryFindCovering(string configurationPath, [NotNullWhen(true)] out string? refusedSetting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);

        refusedSetting = RefusedPaths.FirstOrDefault(path =>
            SettingPath.Covers(path, configurationPath) || SettingPath.Covers(configurationPath, path));

        return refusedSetting is not null;
    }
}
