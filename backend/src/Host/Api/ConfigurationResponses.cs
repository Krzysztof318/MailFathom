// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Configuration;
using MailFathom.Host.Configuration.Administration;

namespace MailFathom.Host.Api;

/// <summary>What the deployment reports when asked what its settings say and where each one is decided.</summary>
/// <param name="Version">The persisted configuration version this process composed its settings over.</param>
/// <param name="Settings">One entry per setting the prefix matched, ordered by path.</param>
/// <remarks>
/// The version travels with the reading because it is what the caller's next write is composed over. Reading and then
/// writing against a version fetched separately is the lost-update shape the version guard exists to refuse, so the
/// number arrives with the answer it describes.
/// </remarks>
internal sealed record ConfigurationReadingResponse(long Version, IReadOnlyList<EffectiveSettingResponse> Settings)
{
    /// <summary>Describes a reading that answered.</summary>
    /// <param name="version">The version the process composed its settings over.</param>
    /// <param name="reading">The settings the prefix matched.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reading" /> is <see langword="null" />.</exception>
    internal static ConfigurationReadingResponse For(long version, SettingsReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return new ConfigurationReadingResponse(version, [.. reading.Settings.Select(EffectiveSettingResponse.For)]);
    }
}

/// <summary>One setting as the deployment reads it.</summary>
/// <param name="Path">The colon-delimited configuration path.</param>
/// <param name="Value">The value the deployment reads, or the redaction marker where the setting bears a secret.</param>
/// <param name="Source">The published name of the layer that supplied the value.</param>
/// <param name="Origin">What identifies that source within its own kind — a file's path — and nothing for a layer that has one instance.</param>
/// <param name="Redacted">Whether <see cref="Value" /> is the marker rather than what the deployment reads.</param>
internal sealed record EffectiveSettingResponse(
    string Path,
    string Value,
    string Source,
    string? Origin,
    bool Redacted)
{
    /// <summary>Describes one setting.</summary>
    /// <param name="setting">The setting as it was read.</param>
    /// <returns>The response entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="setting" /> is <see langword="null" />.</exception>
    internal static EffectiveSettingResponse For(EffectiveSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);

        return new EffectiveSettingResponse(
            setting.Path,
            setting.Value,
            setting.Source.Name,
            setting.Origin,
            setting.IsRedacted);
    }
}

/// <summary>What the deployment reports when asked for the persisted document itself.</summary>
/// <param name="Version">The version the document was read at, which the commit that follows is accepted against.</param>
/// <param name="Document">The sparse document, with every secret-bearing value replaced by the redaction marker.</param>
internal sealed record ConfigurationDocumentResponse(long Version, string Document);

/// <summary>The changes one administrative write asks for.</summary>
/// <param name="Version">The version the changes were composed over.</param>
/// <param name="Changes">The changes, applied together or not at all.</param>
/// <param name="EvenIfShadowed">Whether to commit a change to a setting a source above the persisted layer supplies, which is refused unless it is stated.</param>
internal sealed record ConfigurationWriteRequest(
    long Version,
    IReadOnlyList<ConfigurationChangeRequest>? Changes,
    bool EvenIfShadowed);

/// <summary>One change a write asks for: a setting given a value, or one the document stops carrying.</summary>
/// <param name="Path">The colon-delimited configuration path.</param>
/// <param name="Value">The value the setting takes, or <see langword="null" /> to stop the document carrying it so the source beneath supplies it again.</param>
/// <remarks>
/// An absent value is a removal rather than a blank, which is the distinction the persisted layer's sparseness rests
/// on: a setting the document does not carry is inherited, and one carrying an empty value shadows the source beneath
/// it with nothing.
/// </remarks>
internal sealed record ConfigurationChangeRequest(string? Path, string? Value);

/// <summary>The whole document an editing session saved.</summary>
/// <param name="Version">The version the buffer was opened over.</param>
/// <param name="Document">The document as the operator saved it.</param>
/// <param name="EvenIfShadowed">Whether to commit a change to a setting a source above the persisted layer supplies.</param>
internal sealed record ConfigurationDocumentRequest(long Version, string? Document, bool EvenIfShadowed);

/// <summary>The path an adoption takes into the persisted layer.</summary>
/// <param name="Version">The version the adoption was previewed over.</param>
/// <param name="Prefix">The colon-delimited path to adopt beneath.</param>
/// <param name="EvenIfShadowed">Whether to commit a setting a source above the persisted layer supplies.</param>
internal sealed record ConfigurationAdoptionRequest(long Version, string? Prefix, bool EvenIfShadowed);

/// <summary>What one administrative configuration write did.</summary>
/// <param name="Committed">Whether the deployment's persisted configuration moved to a new version.</param>
/// <param name="Version">The version now in force, whether the write committed, was refused, or changed nothing.</param>
/// <param name="Code">The five-digit code naming why the write was refused, and <see langword="null" /> where nothing refused it.</param>
/// <param name="Messages">One sentence per reason the write was refused or changed nothing, and empty on a commit.</param>
/// <param name="Changes">What each named setting read as before the write and reads as now, and empty unless it committed.</param>
/// <remarks>
/// A refusal arrives as an outcome with a success status rather than as an error, for the reason
/// <see cref="ConfigurationWriteResult" /> gives: every one of them is something the administrator who asked for the
/// write acts on and continues from — a path persisted nowhere, a value that will not bind, a document somebody else
/// moved on, a setting an override already supplies — and each carries the version they compose the next attempt over.
/// </remarks>
internal sealed record ConfigurationWriteResponse(
    bool Committed,
    long Version,
    int? Code,
    IReadOnlyList<string> Messages,
    IReadOnlyList<SettingChangeResponse> Changes)
{
    /// <summary>Describes what a write did.</summary>
    /// <param name="outcome">The outcome the administration reported.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="outcome" /> is <see langword="null" />.</exception>
    internal static ConfigurationWriteResponse For(SettingsWriteOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new ConfigurationWriteResponse(
            outcome.Committed,
            outcome.Version,
            outcome.Refusal.IsSpecified ? outcome.Refusal.Value : null,
            outcome.Messages,
            [.. outcome.Changes.Select(SettingChangeResponse.For)]);
    }
}

/// <summary>What one setting read as before a committed write and what it reads as after it.</summary>
/// <param name="Path">The colon-delimited configuration path the write named.</param>
/// <param name="Before">What the deployment read at the path before the commit, or <see langword="null" /> where no source supplied it.</param>
/// <param name="After">What the deployment reads at the path now, or <see langword="null" /> where nothing supplies it.</param>
internal sealed record SettingChangeResponse(
    string Path,
    EffectiveSettingResponse? Before,
    EffectiveSettingResponse? After)
{
    /// <summary>Describes one setting's two readings.</summary>
    /// <param name="change">The change as the administration reported it.</param>
    /// <returns>The response entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="change" /> is <see langword="null" />.</exception>
    internal static SettingChangeResponse For(SettingChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new SettingChangeResponse(
            change.Path,
            change.Before is { } before ? EffectiveSettingResponse.For(before) : null,
            change.After is { } after ? EffectiveSettingResponse.For(after) : null);
    }
}
