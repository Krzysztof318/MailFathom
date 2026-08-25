// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.RootSettings;

/// <summary>Refuses a persisted value for a setting the persisted layer itself was reached through.</summary>
/// <remarks>
/// <para>
/// Reading the layer needs settings of its own: where the database is, how long its one statement may run, how the
/// secret reference carrying its credential is interpreted, and which configuration sources exist at all. Those are
/// read from the sources beneath the layer, because a persisted value for one of them could not be read without first
/// reading it — so the read is not circular, and the list below is exactly what makes it so.
/// </para>
/// <para>
/// What that leaves is a split rather than a circle, and the split is why this refuses rather than ignores. The layer
/// is composed above every file, so a persisted <c>Persistence:Password</c> would leave the bootstrap read
/// authenticating with the file's credential while the connection pool, the schema gate, and every worker
/// authenticated with the persisted one, and nothing in the running process would report the disagreement. A persisted
/// <c>Secrets:Interpretation</c> is worse than a split: it decides whether a plain-text value written where a
/// reference belongs fails startup or is accepted, for the whole secret-resolution graph, which would make the layer a
/// way to relax the terms the layer itself is trusted under. <c>Persistence:CommandTimeoutSeconds</c> is the same
/// split one turn later: it bounds the statement that fetched the document, so a persisted value would bound the
/// connection pool and the schema gate while the read that fetched it ran at the file's bound.
/// </para>
/// <para>
/// Dropping the keys from the published snapshot was the other candidate and is refused. A document that persists one
/// is a mistake an operator made, and a mistake silently discarded is one they go on believing they fixed; the layer
/// is read once at startup, so refusing costs a start that would otherwise have run on settings nobody could see.
/// </para>
/// </remarks>
internal static class BootstrapOnlySettings
{
    /// <summary>The keys read before this layer exists, each refused along with anything nested beneath it.</summary>
    /// <remarks>
    /// Prefixes rather than exact names, because two of them are sections rather than values: <c>Persistence:Password</c>
    /// is a secret block whose reference and store live under it, and refusing only the section's own key would admit
    /// <c>Persistence:Password:Reference</c>, which is the half that decides the credential. The rest are scalars and
    /// match themselves.
    /// </remarks>
    private static readonly string[] RefusedKeys =
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
    /// <returns>The refused keys the document carries, ordered as they are declared, empty when it carries none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="persistedKeys" /> is <see langword="null" />.</exception>
    public static IReadOnlyList<string> FindIn(IEnumerable<string> persistedKeys)
    {
        ArgumentNullException.ThrowIfNull(persistedKeys);

        var keys = persistedKeys.ToArray();

        return [.. RefusedKeys.Where(refused => keys.Any(key => Names(key, refused)))];
    }

    /// <summary>Reports whether a persisted key is a refused setting or something nested beneath one.</summary>
    /// <remarks>
    /// Configuration keys are compared case-insensitively by every provider in the pipeline, so a document writing
    /// <c>persistence:password</c> names the same setting and is refused as one.
    /// </remarks>
    private static bool Names(string persistedKey, string refusedKey) =>
        persistedKey.Equals(refusedKey, StringComparison.OrdinalIgnoreCase)
        || persistedKey.StartsWith($"{refusedKey}:", StringComparison.OrdinalIgnoreCase);
}
