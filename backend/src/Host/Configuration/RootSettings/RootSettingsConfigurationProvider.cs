// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using System.Text.Json;
using MailFathom.Application.Configuration;
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
/// layer refuses — because it does not parse, because it carries a setting the layer itself was reached through, or
/// because it carries one the storage catalog persists elsewhere — leaves the last valid one in force.
/// </para>
/// <para>
/// The framework's parser is reached through a provider of its own, nested below, which exists only to be loaded and
/// read. That is what lets a candidate be parsed and judged without ever being published: this provider assigns its
/// own dictionary once the document has been accepted, so no reader can observe a document the layer went on to
/// refuse.
/// </para>
/// </remarks>
internal sealed class RootSettingsConfigurationProvider : ConfigurationProvider
{
    private RootSettingsDocument document;

    /// <summary>Initializes the provider with the document the host read while composing its configuration.</summary>
    /// <param name="document">The persisted configuration document and the version it was read at.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document" /> is <see langword="null" />.</exception>
    public RootSettingsConfigurationProvider(RootSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        this.document = document;
    }

    /// <summary>Gets the version of the document currently in force.</summary>
    public long Version => this.document.Version;

    /// <inheritdoc />
    /// <exception cref="FormatException">Thrown when the JSON configuration parser refuses the persisted document — a root that is not an object, or two keys differing only in case.</exception>
    /// <exception cref="JsonException">Thrown when the persisted document cannot be read as JSON at all, which for a <c>jsonb</c> column means one nested deeper than the reader's maximum.</exception>
    /// <exception cref="BootstrapOnlySettingPersistedException">Thrown when the document carries a setting read before this layer is composed.</exception>
    /// <exception cref="MisroutedSettingPersistedException">Thrown when the document carries a setting the storage catalog persists in a store of its own.</exception>
    public override void Load() => this.Parse(this.document);

    /// <summary>Replaces the published snapshot with a document read after startup, when it is newer than the one in force.</summary>
    /// <param name="candidate">The document to publish, and the version it was read at.</param>
    /// <returns><see langword="true" /> when the candidate was published, <see langword="false" /> when a version at least as new was already in force.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidate" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException">Thrown when the JSON configuration parser refuses the candidate, in which case the snapshot in force is unchanged.</exception>
    /// <exception cref="JsonException">Thrown when the candidate cannot be read as JSON at all, in which case the snapshot in force is likewise unchanged.</exception>
    /// <exception cref="BootstrapOnlySettingPersistedException">Thrown when the candidate carries a setting read before this layer is composed, in which case the snapshot in force is likewise unchanged.</exception>
    /// <exception cref="MisroutedSettingPersistedException">Thrown when the candidate carries a setting the storage catalog persists in a store of its own, in which case the snapshot in force is likewise unchanged.</exception>
    /// <remarks>
    /// <para>
    /// The change token is raised only once the candidate has been published, so a rejected candidate reloads nothing
    /// and every binding keeps reading the values the last accepted version supplied. Falling back to the sources
    /// beneath this layer is deliberately not among the outcomes: those sources never carried the persisted values, so
    /// reverting to them would silently change settings the deployment had adopted.
    /// </para>
    /// <para>
    /// Candidates arrive in whatever order the writers that produced them finish, which is not the order they
    /// committed in, so a version at least as new as the one in force is refused rather than published. That is the
    /// whole of the ordering: it holds across two processes, where no lock either writer could take exists, and it
    /// makes a superseded republish a no-op rather than a step backwards.
    /// </para>
    /// <para>
    /// It is not written for two publishers running at the same instant: the snapshot and the version it is read at
    /// are two assignments, and a second publisher running against the first would leave a reader able to see one
    /// without the other. A reader is otherwise unaffected — the dictionary is replaced by reference rather than
    /// mutated, so no read ever observes a half-applied document.
    /// </para>
    /// </remarks>
    public bool Apply(RootSettingsDocument candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        // A version already published, or one behind it, is not a candidate. Two writers interleave whenever two
        // administrators edit at once — and across two processes, where no lock either of them could take exists: the
        // one that committed first can be the one that republishes last, and publishing its document over the other's
        // would leave the process serving a version the database no longer holds, with nothing to correct it until the
        // next write. The guard is on the version rather than on the caller because that is the fact both writers share.
        if (candidate.Version <= this.document.Version)
        {
            return false;
        }

        this.Parse(candidate);
        this.document = candidate;

        this.OnReload();

        return true;
    }

    private void Parse(RootSettingsDocument settings)
    {
        var candidate = DocumentParser.Flatten(settings.Json);

        // The refusal is read off the flattened keys rather than off the raw JSON, because what a document contributes
        // is those keys and not its nesting: `{"Persistence": {"Password": {"Reference": …}}}` and
        // `{"Persistence:Password:Reference": …}` are the same setting to every reader beneath this one.
        if (BootstrapOnlySettings.FindIn(candidate.Keys) is { Count: > 0 } refused)
        {
            throw new BootstrapOnlySettingPersistedException(
                $"The persisted configuration document at version {settings.Version} carries settings MailFathom reads before that layer exists: {string.Join(", ", refused)}. Remove them from the document and configure them in a file, the environment, or a command-line argument, which is where the bootstrap read takes them from.");
        }

        // The root document is the store for every path the catalog routes nowhere else, so a path it does route is one
        // this document may not carry: the store that owns it holds it already, and composing both would publish one
        // setting from two rows with nothing able to say which of them is current.
        if (ConfigurationStorageCatalog.FindRoutedElsewhereIn(candidate.Keys) is { Count: > 0 } misrouted)
        {
            throw new MisroutedSettingPersistedException(
                $"The persisted configuration document at version {settings.Version} carries settings MailFathom persists in a store of their own: {string.Join(", ", misrouted)}. Remove them from the document, which is where every setting no store of its own claims is persisted.");
        }

        this.Data = candidate;
    }

    /// <summary>Flattens a persisted document with the framework's own JSON parser, publishing nothing.</summary>
    /// <remarks>
    /// A stream provider is the shortest path to the framework's <c>JsonConfigurationFileParser</c>, which is not
    /// public, and one instantiated here is read and discarded rather than added to any configuration. Judging a
    /// candidate needs its flattened keys, and producing them on an instance nobody is bound to is what keeps a
    /// document the layer refuses from being observable while it is being judged — a credential reference above all,
    /// which is exactly what the refusal exists to keep out of the published snapshot.
    /// </remarks>
    private sealed class DocumentParser : JsonStreamConfigurationProvider
    {
        private DocumentParser()
            : base(new JsonStreamConfigurationSource())
        {
        }

        public static IDictionary<string, string?> Flatten(string json)
        {
            using var content = new MemoryStream(Encoding.UTF8.GetBytes(json));

            var parser = new DocumentParser();

            parser.Load(content);

            return parser.Data;
        }
    }
}
