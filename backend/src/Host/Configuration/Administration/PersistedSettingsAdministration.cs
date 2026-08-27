// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using MailFathom.Application.Configuration;
using MailFathom.Domain.Failures;
using MailFathom.Infrastructure.Persistence.Settings;

namespace MailFathom.Host.Configuration.Administration;

/// <summary>What an administrator does to the deployment's persisted configuration, in the terms they do it in.</summary>
/// <remarks>
/// <para>
/// It sits between the administrative routes and <see cref="IConfigurationWriter" /> and adds the three things a
/// keyed change needs and the port deliberately does not have. It knows which layer supplies each path, so it can
/// refuse a write the deployment would go on ignoring; it reads the effective value on both sides of a commit, so an
/// operator is told what their change did rather than that it happened; and it drops a change that would change
/// nothing, so an unchanged buffer and a removal of a setting nobody persisted cost no version.
/// </para>
/// <para>
/// None of that is a second way to write a setting. Every commit here is one <see cref="IConfigurationWriter" /> call
/// over one version, with the whole of that port's judgement in front of it — the deny-list, the route catalog, the
/// secret rule, the candidate binding, the validators, and the version guard. What this adds is refused *before* the
/// port is reached and never instead of it.
/// </para>
/// <para>
/// The shadowing refusal is here rather than in the command, and that is deliberate: a rule enforced in <c>mfctl</c>
/// would hold for the operator who typed the command and for nobody else reaching the same route. It is refused by
/// default and committed on request, because staging a value beneath an override an operator is about to remove is a
/// thing they legitimately mean.
/// </para>
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The dependency injection container materializes this service.")]
internal sealed class PersistedSettingsAdministration(
    EffectiveSettingsReader reader,
    IRootSettingsDocumentReader documents,
    IConfigurationWriter writer)
{
    /// <summary>Gets the persisted configuration version this process composed its settings over.</summary>
    internal long ComposedVersion => reader.ComposedVersion;

    /// <summary>Reads the deployment's settings at or beneath a path, as it reads them itself.</summary>
    /// <param name="prefix">The colon-delimited path to read beneath, or <see langword="null" /> for everything.</param>
    /// <returns>The reading.</returns>
    internal SettingsReading Read(string? prefix) => reader.Read(prefix);

    /// <summary>Reads what an adoption of a path would copy into the persisted layer.</summary>
    /// <param name="prefix">The colon-delimited path to read beneath, or <see langword="null" /> for everything.</param>
    /// <returns>The settings the files decide and the layer does not already carry, ordered by path.</returns>
    /// <remarks>
    /// A path the layer already carries is left out rather than offered, because adopting it would replace a value
    /// somebody persisted deliberately with the file's — which is the opposite of what taking a decision into the
    /// database means. Changing a persisted value is what a keyed write is for.
    /// </remarks>
    internal SettingsReading ReadAdoptable(string? prefix)
    {
        var beneath = reader.ReadBeneathTheLayer(prefix);

        return beneath.IsTooBroad
            ? beneath
            : SettingsReading.Of([.. beneath.Settings.Where(setting => reader.PersistedValue(setting.Path) is null)]);
    }

    /// <summary>Reads the persisted document itself, as the sparse JSON an editing session opens.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The document with every secret-bearing value replaced by the redaction marker, and the version it was read at.</returns>
    /// <exception cref="RootSettingsUnreadableException">Thrown when the persisted configuration cannot be read.</exception>
    internal async Task<PersistedSettingsDocument> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        var document = await documents.ReadAsync(cancellationToken);

        return new PersistedSettingsDocument(SettingRedaction.ApplyToDocument(document.Json), document.Version);
    }

    /// <summary>Applies keyed changes to the persisted configuration.</summary>
    /// <param name="edits">The changes, as the caller stated them.</param>
    /// <param name="expectedVersion">The version the changes were composed over.</param>
    /// <param name="evenIfShadowed">Whether to commit a change to a setting a source above the layer supplies.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns>What the write did.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="edits" /> is <see langword="null" />.</exception>
    /// <exception cref="RootSettingsUnreadableException">Thrown when the document the write would change could not be read.</exception>
    /// <exception cref="RootSettingsUnwritableException">Thrown when the commit did not complete.</exception>
    internal async Task<SettingsWriteOutcome> ApplyAsync(
        IReadOnlyList<ConfigurationEdit> edits,
        long expectedVersion,
        bool evenIfShadowed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var effective = edits.Where(this.WouldChangeTheDocument).ToList();

        if (effective.Count == 0)
        {
            return SettingsWriteOutcome.NothingToChange(
                expectedVersion,
                $"Nothing in the change would alter the persisted document — each setting either already stands as it was asked to, or names one the layer does not carry — so nothing was written and version {expectedVersion} stays in force.");
        }

        return await this.CommitAsync(effective, expectedVersion, evenIfShadowed, cancellationToken);
    }

    /// <summary>Applies a whole edited document to the persisted configuration.</summary>
    /// <param name="documentJson">The document the operator saved.</param>
    /// <param name="expectedVersion">The version the buffer was opened over.</param>
    /// <param name="evenIfShadowed">Whether to commit a change to a setting a source above the layer supplies.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns>What the write did.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="documentJson" /> is <see langword="null" />.</exception>
    /// <exception cref="RootSettingsUnreadableException">Thrown when the document the write would change could not be read.</exception>
    /// <exception cref="RootSettingsUnwritableException">Thrown when the commit did not complete.</exception>
    /// <remarks>
    /// The buffer becomes keyed changes here rather than replacing the document wholesale, so that one vocabulary
    /// reaches the writer whichever surface stated the change. It is flattened by the framework's own JSON
    /// configuration provider — the same parser that reads the row at startup — so a nested object, an array, and a
    /// number become exactly the keys the deployment would have read, rather than the keys a second flattener here
    /// happened to agree on.
    /// </remarks>
    internal async Task<SettingsWriteOutcome> ApplyDocumentAsync(
        string documentJson,
        long expectedVersion,
        bool evenIfShadowed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documentJson);

        var inForce = await documents.ReadAsync(cancellationToken);

        if (inForce.Version != expectedVersion)
        {
            return SettingsWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationVersionSuperseded,
                inForce.Version,
                [
                    $"The buffer was opened over persisted configuration version {expectedVersion}, and version {inForce.Version} is in force. Read the configuration as it now stands and edit it again against that.",
                ]);
        }

        IReadOnlyList<ConfigurationEdit> edits;

        try
        {
            edits = DifferenceBetween(Flatten(inForce.Json), Flatten(documentJson));
        }
        catch (Exception refusal)
            when (refusal is FormatException or JsonException or InvalidDataException or ArgumentException)
        {
            // The buffer is what an operator typed, so every way it can be wrong is theirs to correct rather than a
            // defect: a document that is not an object of configuration keys, a key with no name, a value carrying a
            // character PostgreSQL text cannot hold, or one past what a setting may be. The parser's own message names
            // which, and it names no value.
            return SettingsWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationCandidateInvalid,
                inForce.Version,
                [$"The saved buffer is not a document of configuration settings this deployment can persist, so nothing was written: {refusal.Message}"]);
        }

        if (edits.Count == 0)
        {
            return SettingsWriteOutcome.NothingToChange(
                expectedVersion,
                $"The saved buffer composes the settings the document already carries, so nothing was written and version {expectedVersion} stays in force.");
        }

        // A buffer is edited by hand and its difference from the document is unbounded, so the bound is stated here as
        // a refusal the operator reads. The port raises it as an argument failure instead, which is right for a caller
        // that composed the change itself and wrong for one carrying whatever somebody saved.
        if (edits.Count > IConfigurationWriter.MaximumEdits)
        {
            return SettingsWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationCandidateInvalid,
                expectedVersion,
                [
                    $"The saved buffer differs from the document in force in {edits.Count} settings, past the {IConfigurationWriter.MaximumEdits} one change carries. Save fewer changes at a time.",
                ]);
        }

        return await this.CommitAsync(edits, expectedVersion, evenIfShadowed, cancellationToken);
    }

    /// <summary>Copies what the files supply beneath a path into the persisted layer.</summary>
    /// <param name="prefix">The colon-delimited path to adopt beneath.</param>
    /// <param name="expectedVersion">The version the adoption was previewed over.</param>
    /// <param name="evenIfShadowed">Whether to commit a setting a source above the layer supplies.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns>What the write did.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="prefix" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="RootSettingsUnreadableException">Thrown when the document the write would change could not be read.</exception>
    /// <exception cref="RootSettingsUnwritableException">Thrown when the commit did not complete.</exception>
    /// <remarks>
    /// A prefix is required and there is no adoption of everything, because adopting is a deliberate act over a part of
    /// the configuration an operator has decided to take into the database. An unbounded one would move a deployment's
    /// whole file-decided configuration into a row in a single command, which is a thing nobody means and nothing
    /// undoes in one step.
    /// </remarks>
    internal async Task<SettingsWriteOutcome> AdoptAsync(
        string prefix,
        long expectedVersion,
        bool evenIfShadowed,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var adoptable = this.ReadAdoptable(prefix);

        if (adoptable.IsTooBroad)
        {
            return SettingsWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationCandidateInvalid,
                expectedVersion,
                [
                    $"The files supply {adoptable.MatchedCount} settings beneath '{prefix}', past the {EffectiveSettingsReader.MaximumSettings} one adoption carries. Adopt a narrower path.",
                ]);
        }

        var edits = adoptable.Settings
            .Select(setting => (setting.Path, Value: reader.ValueBeneathTheLayer(setting.Path)))
            .Where(supplied => supplied.Value is not null)
            .Select(supplied => ConfigurationEdit.SetTo(supplied.Path, supplied.Value!))
            .ToList();

        if (edits.Count == 0)
        {
            return SettingsWriteOutcome.NothingToChange(
                expectedVersion,
                $"The deployment's files supply nothing beneath '{prefix}' that the persisted layer does not already carry, so nothing was adopted.");
        }

        if (edits.Count > IConfigurationWriter.MaximumEdits)
        {
            return SettingsWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationCandidateInvalid,
                expectedVersion,
                [
                    $"Adopting '{prefix}' would write {edits.Count} settings in one change, past the {IConfigurationWriter.MaximumEdits} a write carries. Adopt a narrower path.",
                ]);
        }

        return await this.ApplyAsync(edits, expectedVersion, evenIfShadowed, cancellationToken);
    }

    /// <summary>Refuses what the deployment would ignore, then commits the rest and reports both readings of each setting.</summary>
    /// <remarks>
    /// Shared by every path rather than repeated, and reached only with changes the caller has already established
    /// would alter the document — which is a different question on each path and is answered against a different thing.
    /// A keyed change is judged against the layer this process composed, because that is what the same command reports
    /// the setting as reading and what the version it states was taken from; a saved buffer is judged against the row
    /// the save was differenced against, because that is what it was composed over.
    /// </remarks>
    private async Task<SettingsWriteOutcome> CommitAsync(
        IReadOnlyList<ConfigurationEdit> effective,
        long expectedVersion,
        bool evenIfShadowed,
        CancellationToken cancellationToken)
    {
        if (!evenIfShadowed && this.FindShadowed(effective) is { Count: > 0 } shadowed)
        {
            return SettingsWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationWriteShadowed,
                expectedVersion,
                shadowed);
        }

        var before = effective
            .Select(edit => edit.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(path => path, reader.ReadOne, StringComparer.OrdinalIgnoreCase);

        var result = await writer.WriteAsync(effective, expectedVersion, cancellationToken);

        return result.IsCommitted
            ? SettingsWriteOutcome.CommittedAs(
                result.Version,
                [.. before.Select(read => new SettingChange(read.Key, read.Value, reader.ReadOne(read.Key)))])
            : SettingsWriteOutcome.Refused(result.Refusal, result.Version, result.RefusalMessages);
    }

    /// <summary>Reports whether a change would leave the persisted document different from how it stands.</summary>
    /// <remarks>
    /// The layer rather than the composed configuration decides, because this is a question about the document: a value
    /// an environment variable supplies identically is still a value the document does not carry, and persisting it is
    /// exactly what an operator staging a change means to do.
    /// </remarks>
    private bool WouldChangeTheDocument(ConfigurationEdit edit) => edit.RemovesTheSetting
        ? reader.LayerCarries(edit.Path)
        : !string.Equals(reader.PersistedValue(edit.Path), edit.Value, StringComparison.Ordinal);

    /// <summary>Names every change the deployment would go on ignoring, and what supplies the value instead.</summary>
    /// <remarks>
    /// Every one of them rather than the first, because an operator correcting one at a time would learn of the next
    /// only by attempting the write again. The value the outranking source supplies is named as it is read back — so a
    /// secret-bearing setting names the source and not the credential shadowing it.
    /// </remarks>
    private IReadOnlyList<string> FindShadowed(IReadOnlyList<ConfigurationEdit> edits) =>
    [
        .. edits
            .Select(edit => (edit.Path, Shadow: reader.ShadowOver(edit.Path)))
            .Where(found => found.Shadow is not null)
            .Select(found => Describe(found.Path, found.Shadow!)),
    ];

    private static string Describe(string path, EffectiveSetting shadow) =>
        $"{path} is supplied by the {shadow.Source.Name} source{Origin(shadow)}, which outranks the persisted layer, so persisting it would change nothing this deployment reads. Change it where that source is written, or state that the write is meant to stand beneath the override.";

    private static string Origin(EffectiveSetting shadow) =>
        shadow.Origin is { Length: > 0 } origin ? $" at {origin}" : string.Empty;

    /// <summary>Reads a document as the configuration keys the deployment would compose from it.</summary>
    /// <exception cref="InvalidDataException">Thrown when the JSON configuration provider refuses the document.</exception>
    private static Dictionary<string, string> Flatten(string json)
    {
        using var buffer = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var composed = new ConfigurationBuilder().AddJsonStream(buffer).Build();

        return composed
            .AsEnumerable()
            .Where(setting => setting.Value is not null)
            .ToDictionary(setting => setting.Key, setting => setting.Value!, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Turns the difference between two documents into the changes that carry one to the other.</summary>
    /// <remarks>
    /// A value left at the redaction marker is not a change, and dropping it is what makes an editing session safe over
    /// a document carrying secrets: the buffer shows the marker where a reference stands, and saving it back leaves the
    /// reference exactly as it was rather than persisting the marker over it. A setting the operator deleted is a
    /// removal like any other, marker or not.
    /// </remarks>
    private static IReadOnlyList<ConfigurationEdit> DifferenceBetween(
        Dictionary<string, string> standing,
        Dictionary<string, string> saved) =>
    [
        .. saved
            .Where(setting => !string.Equals(setting.Value, SettingRedaction.Marker, StringComparison.Ordinal))
            .Where(setting => !standing.TryGetValue(setting.Key, out var held)
                || !string.Equals(held, setting.Value, StringComparison.Ordinal))
            .Select(setting => ConfigurationEdit.SetTo(setting.Key, setting.Value))
            .Concat(standing
                .Where(setting => !saved.ContainsKey(setting.Key))
                .Select(setting => ConfigurationEdit.Removing(setting.Key)))
            .OrderBy(edit => edit.Path, StringComparer.OrdinalIgnoreCase),
    ];
}
