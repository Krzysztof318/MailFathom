// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using System.Text.Json;
using MailFathom.Infrastructure.Persistence.Settings;
using Microsoft.Extensions.Configuration.Json;

namespace MailFathom.Host.Configuration.RootSettings;

/// <summary>Publishes the deployment's persisted configuration as ordinary .NET configuration keys.</summary>
/// <remarks>
/// <para>
/// The document is flattened by the framework's own JSON parser rather than by anything written here, which is the
/// point: object composition, colon-delimited keys, and the numeric child keys an array's elements take are then the
/// same rules every other JSON source in this host obeys. A persisted value at index <c>2</c> replaces index <c>2</c>
/// and nothing else, and a key the document omits is inherited from the source beneath rather than read as empty —
/// both of which follow from using one flattening and one merge rather than a second vocabulary.
/// </para>
/// <para>
/// The provider holds one snapshot and never queries per key. A reload replaces that snapshot whole; a candidate the
/// layer refuses — because it does not parse, or because it carries a setting the layer itself was reached through —
/// leaves the last valid one in force.
/// </para>
/// <para>
/// It derives from the stream provider to reach that parser, and deliberately never uses the stream on the source it
/// is constructed with: <see cref="Load()" /> is overridden to read the document this provider was given instead. The
/// base class's own <c>Load()</c> is the one that consumes <c>Source.Stream</c>, and it is never called.
/// </para>
/// </remarks>
internal sealed class RootSettingsConfigurationProvider : JsonStreamConfigurationProvider
{
    private RootSettingsDocument document;

    /// <summary>Initializes the provider with the document the host read while composing its configuration.</summary>
    /// <param name="document">The persisted configuration document and the version it was read at.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document" /> is <see langword="null" />.</exception>
    public RootSettingsConfigurationProvider(RootSettingsDocument document)
        : base(new JsonStreamConfigurationSource())
    {
        ArgumentNullException.ThrowIfNull(document);

        this.document = document;
    }

    /// <summary>Gets the version of the document currently in force.</summary>
    public long Version => this.document.Version;

    /// <inheritdoc />
    /// <exception cref="FormatException">Thrown when the persisted document is not a JSON object of configuration keys.</exception>
    /// <exception cref="JsonException">Thrown when the persisted document cannot be read as JSON at all, which for a <c>jsonb</c> column means one nested deeper than the reader's maximum.</exception>
    /// <exception cref="BootstrapOnlySettingPersistedException">Thrown when the document carries a setting read before this layer is composed.</exception>
    public override void Load() => this.Parse(this.document);

    /// <summary>Replaces the published snapshot with a document read after startup.</summary>
    /// <param name="candidate">The document to publish, and the version it was read at.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidate" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException">Thrown when the candidate is not a JSON object of configuration keys, in which case the snapshot in force is unchanged.</exception>
    /// <exception cref="JsonException">Thrown when the candidate cannot be read as JSON at all, in which case the snapshot in force is likewise unchanged.</exception>
    /// <exception cref="BootstrapOnlySettingPersistedException">Thrown when the candidate carries a setting read before this layer is composed, in which case the snapshot in force is likewise unchanged.</exception>
    /// <remarks>
    /// <para>
    /// The change token is raised only once the candidate has been published, so a rejected candidate reloads nothing
    /// and every binding keeps reading the values the last accepted version supplied. Falling back to the sources
    /// beneath this layer is deliberately not among the outcomes: those sources never carried the persisted values, so
    /// reverting to them would silently change settings the deployment had adopted.
    /// </para>
    /// <para>
    /// One caller republishes, so this is not written for concurrent publishers: the snapshot and the version it is
    /// read at are two assignments, and a second publisher running against the first would leave a reader able to see
    /// one without the other. A reader is otherwise unaffected — the dictionary is replaced by reference rather than
    /// mutated, so no read ever observes a half-applied document.
    /// </para>
    /// </remarks>
    public void Apply(RootSettingsDocument candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        this.Parse(candidate);
        this.document = candidate;

        this.OnReload();
    }

    private void Parse(RootSettingsDocument settings)
    {
        var published = this.Data;

        using var content = new MemoryStream(Encoding.UTF8.GetBytes(settings.Json));

        this.Load(content);

        // The refusal is read after the parse rather than off the raw JSON, because what a document contributes is the
        // flattened keys and not its nesting: `{"Persistence": {"Password": {"Reference": …}}}` and
        // `{"Persistence:Password:Reference": …}` are the same setting to every reader beneath this one. That means the
        // dictionary has already been replaced by the time the answer exists, so a refusal puts the published one back
        // — the parser's own refusals throw before the assignment and need no such care.
        if (BootstrapOnlySettings.FindIn(this.Data.Keys) is { Count: > 0 } refused)
        {
            this.Data = published;

            throw new BootstrapOnlySettingPersistedException(
                $"The persisted configuration document at version {settings.Version} carries settings MailFathom reads before that layer exists: {string.Join(", ", refused)}. Remove them from the document and configure them in a file, the environment, or a command-line argument, which is where the bootstrap read takes them from.");
        }
    }
}
