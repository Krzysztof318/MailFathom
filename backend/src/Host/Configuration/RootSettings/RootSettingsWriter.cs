// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MailFathom.Application.Configuration;
using MailFathom.Domain.Failures;
using MailFathom.Infrastructure.Persistence.Settings;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.References;

namespace MailFathom.Host.Configuration.RootSettings;

/// <summary>Writes the deployment's persisted configuration, having proved the configuration the write would produce.</summary>
/// <remarks>
/// <para>
/// The order of the stages is the contract rather than an implementation detail, because each one exists to keep the
/// next from being reached with something it could not undo. A path MailFathom persists nowhere and a value that is
/// secret material are refused before anything is read, since neither depends on what the document says. The version
/// is checked next, so an edit authored against a document somebody else has replaced is refused rather than composed
/// over theirs. Only then is a candidate built, composed with every source that outranks the layer, and put through
/// the binding and the validators a start uses — and only a candidate that survives that is committed.
/// </para>
/// <para>
/// Every refusal leaves the row exactly as it was, which is what makes the whole of it atomic without a transaction
/// spanning it: nothing before the commit writes anything, and the commit is one statement guarded by the version the
/// candidate was composed over. A writer that lost the race between validating and committing is refused there rather
/// than overwriting, so the two administrators case has the same answer wherever the second one arrives.
/// </para>
/// <para>
/// The reload comes last, after the commit is durable, and that ordering is the whole reason it is a separate step. A
/// token raised over a candidate would publish settings a failed commit was about to take back, and every options
/// snapshot bound to the layer would have observed a version the deployment never had.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this writer.")]
internal sealed partial class RootSettingsWriter(
    IRootSettingsDocumentReader reader,
    IRootSettingsDocumentWriter store,
    CandidateConfigurationComposer composer,
    CandidateSettingsValidator validator,
    RootSettingsReloader reloader,
    IEnumerable<ISecretSchemeResolver> secretSchemeResolvers,
    ILogger<RootSettingsWriter> logger) : IConfigurationWriter
{
    /// <summary>The schemes that name where material is kept rather than carrying it.</summary>
    /// <remarks>
    /// Read from what this deployment registered rather than from what the reference syntax admits, because those are
    /// not the same set and the difference is the whole of the check. A scheme is minted for any name before the first
    /// colon, so material carrying one — <c>Pa55:word</c>, a token with a colon in it — parses as a well-formed
    /// reference to a scheme nothing serves. Matching the parse alone would admit it into the column; matching what a
    /// resolver actually answers refuses it, and refuses in the direction that keeps a credential out.
    /// </remarks>
    private readonly HashSet<SecretReferenceScheme> schemesNamingWhereMaterialIsKept =
    [
        .. secretSchemeResolvers
            .Select(resolver => resolver.Scheme)
            .Where(scheme => scheme != SecretReferenceScheme.Plaintext),
    ];

    /// <inheritdoc />
    /// <exception cref="RootSettingsUnreadableException">Thrown when the document the write would change could not be read.</exception>
    /// <exception cref="RootSettingsUnwritableException">Thrown when the database refused the commit, in which case the deployment's configuration is unchanged.</exception>
    public async Task<ConfigurationWriteResult> WriteAsync(
        IReadOnlyList<ConfigurationEdit> edits,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedVersion);

        if (edits.Count == 0)
        {
            throw new ArgumentException("A configuration write states at least one change.", nameof(edits));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(edits.Count, IConfigurationWriter.MaximumEdits, nameof(edits));

        if (FindPathsPersistedNowhere(edits) is { Count: > 0 } unwritable)
        {
            return this.Refuse(MailFathomErrorCode.ConfigurationPathNotWritable, expectedVersion, unwritable);
        }

        if (this.FindMaterialWrittenWhereAReferenceBelongs(edits) is { Count: > 0 } material)
        {
            return this.Refuse(MailFathomErrorCode.ConfigurationSecretMaterialRefused, expectedVersion, material);
        }

        var inForce = await reader.ReadAsync(cancellationToken);

        if (inForce.Version != expectedVersion)
        {
            return this.RefuseAsSuperseded(expectedVersion, inForce.Version);
        }

        var judged = this.Judge(inForce, edits);

        if (judged.Refusal is { } refusal)
        {
            return refusal;
        }

        return await this.CommitAsync(judged.CandidateJson, inForce.Version, cancellationToken);
    }

    /// <summary>Builds the candidate document and puts the configuration it would produce through the rules a start applies.</summary>
    /// <remarks>
    /// The candidate is composed and judged inside one method so the composed configuration is disposed whichever way
    /// the judgement goes. It holds a provider per source and a reload subscription on each, so a candidate abandoned
    /// undisposed would leave a file watcher per refused write.
    /// </remarks>
    private CandidateJudgement Judge(RootSettingsDocument inForce, IReadOnlyList<ConfigurationEdit> edits)
    {
        try
        {
            var candidate = new RootSettingsDocument(
                RootSettingsDocumentPatch.Apply(inForce.Json, edits),
                inForce.Version + 1);

            // Before the candidate is composed rather than after, because a document past the ceiling is refused
            // whatever it would have bound to, and measured as the database stores it rather than as it was written.
            if (!RootSettingsCommitRules.FitsWhatIsComposedFrom(candidate.Json))
            {
                return new CandidateJudgement(this.Refuse(
                    MailFathomErrorCode.ConfigurationDocumentTooLarge,
                    inForce.Version,
                    [
                        $"The changes compose a configuration document of {RootSettingsCommitRules.PersistedOctetsOf(candidate.Json)} octets, past the {RootSettingsDocument.MaximumOctets} MailFathom composes its settings from. Persist fewer settings, or remove the ones this deployment no longer configures.",
                    ]));
            }

            using var composed = composer.Compose(candidate);

            return validator.FindErrors(composed) is { Count: > 0 } errors
                ? new CandidateJudgement(
                    this.Refuse(MailFathomErrorCode.ConfigurationCandidateInvalid, inForce.Version, errors))
                : new CandidateJudgement(candidate.Json);
        }
        catch (Exception refusal) when (refusal is FormatException or JsonException)
        {
            return new CandidateJudgement(
                this.Refuse(MailFathomErrorCode.ConfigurationCandidateInvalid, inForce.Version, [refusal.Message]));
        }
        catch (Exception refusal)
            when (refusal is BootstrapOnlySettingPersistedException or MisroutedSettingPersistedException)
        {
            return new CandidateJudgement(
                this.Refuse(MailFathomErrorCode.ConfigurationPathNotWritable, inForce.Version, [refusal.Message]));
        }
    }

    private async Task<ConfigurationWriteResult> CommitAsync(
        string candidateJson,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        if (await store.CommitAsync(candidateJson, expectedVersion, cancellationToken) is not { } committedVersion)
        {
            // The version now in force is read rather than assumed to be the next one, because the writer that won the
            // race may have committed more than once while this candidate was being judged.
            var inForce = await reader.ReadAsync(cancellationToken);

            return this.RefuseAsSuperseded(expectedVersion, inForce.Version);
        }

        this.LogVersionCommitted(committedVersion);

        // Deliberately not the caller's token. Republishing is what makes the committed version the one this process
        // reads, so a caller who gave up between the commit and the reload would otherwise leave the deployment bound
        // to a version the database no longer holds, with nothing scheduled to correct it.
        await reloader.ReloadAsync(CancellationToken.None);

        return ConfigurationWriteResult.Committed(committedVersion);
    }

    /// <summary>Finds the changes naming a setting the deployment persists in no store this build writes.</summary>
    /// <remarks>
    /// Every path is judged rather than the first refused one reported, because an operator correcting one at a time
    /// would learn about the next only by attempting the write again.
    /// </remarks>
    private static IReadOnlyList<string> FindPathsPersistedNowhere(IReadOnlyList<ConfigurationEdit> edits) =>
        [.. edits.Select(edit => WhyNothingPersists(edit.Path)).OfType<string>()];

    /// <summary>Says why a path is persisted nowhere this build writes, or nothing when it is persisted in the root document.</summary>
    /// <remarks>
    /// The catalog answers both halves of the question at once — the bootstrap deny-list and the route — so a path that
    /// resolves a store has already been judged against the settings the layer is itself read through. What is left is
    /// a store the catalog names and this build has no writer for, which is a refusal rather than a silent write into
    /// the wrong row.
    /// </remarks>
    private static string? WhyNothingPersists(string path)
    {
        var target = ConfigurationStorageCatalog.ResolveWriteTarget(path);

        if (!target.IsWritable)
        {
            return target.RefusalMessage;
        }

        return target.Route == ConfigurationStorageRoute.RootDocument
            ? null
            : $"MailFathom persists {path} in the {target.Route.Name} store, which this build does not write. Configure it where that store is provisioned from.";
    }

    /// <summary>Finds the changes writing a secret's material where the document may only carry a reference to it.</summary>
    /// <remarks>
    /// <para>
    /// A setting announces that it holds a secret through its own name, which is the rule
    /// <see cref="SecretPropertyNaming" /> already states for the bound options graph, so nothing here decides a second
    /// time what counts as a secret. What it decides is the value: a reference to a scheme this deployment actually
    /// resolves names where the material is kept, and anything else — a bare password, the one scheme whose target is
    /// the literal itself, or a value that merely happens to carry a colon — is the material.
    /// </para>
    /// <para>
    /// Refused whatever the deployment's secret interpretation says, unlike a value in a file. Under an inline
    /// interpretation a configured value <em>is</em> the material and a file carrying one is the operator's own choice
    /// about their own file; a write is MailFathom putting it into an unsealed column of its own database, which is a
    /// choice this port does not make on their behalf.
    /// </para>
    /// <para>
    /// The message names the setting and says what belongs there. It repeats neither the value nor its length, because
    /// a length is what turns a guess about a credential into a shorter list of guesses.
    /// </para>
    /// </remarks>
    private IReadOnlyList<string> FindMaterialWrittenWhereAReferenceBelongs(IReadOnlyList<ConfigurationEdit> edits) =>
    [
        .. edits
            .Where(edit => !edit.RemovesTheSetting)
            .Where(edit => SecretPropertyNaming.NamesASecret(edit.Path.Split(':')[^1]))
            .Where(edit => !this.NamesWhereTheMaterialIsKept(edit.Value!))
            .Select(edit =>
                $"MailFathom does not persist secret material: {edit.Path} carries the value itself rather than a <scheme>:<target> reference this deployment resolves. Provision the secret and persist the reference to it."),
    ];

    private bool NamesWhereTheMaterialIsKept(string value) =>
        SecretReference.TryParse(value, out var reference, out _)
        && this.schemesNamingWhereMaterialIsKept.Contains(reference.Scheme);

    private ConfigurationWriteResult RefuseAsSuperseded(long expectedVersion, long versionInForce) => this.Refuse(
        MailFathomErrorCode.ConfigurationVersionSuperseded,
        versionInForce,
        [
            $"The write was composed over persisted configuration version {expectedVersion}, and version {versionInForce} is in force. Read the configuration as it now stands and decide again against it.",
        ]);

    private ConfigurationWriteResult Refuse(
        MailFathomErrorCode refusal,
        long versionInForce,
        IReadOnlyList<string> messages)
    {
        // The count rather than the messages. A validator quotes what the caller wrote — a rule's condition among it,
        // which can carry an address they typed — and the caller is the one person who already has both.
        this.LogWriteRefused(refusal.ToString(), versionInForce, messages.Count);

        return ConfigurationWriteResult.Refused(refusal, versionInForce, messages);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Persisted configuration version {CommittedVersion} was committed.")]
    private partial void LogVersionCommitted(long committedVersion);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A configuration write was refused as {Refusal} and changed nothing; version {ActiveVersion} stays in force. It named {RefusedSettingCount} settings to correct.")]
    private partial void LogWriteRefused(string refusal, long activeVersion, int refusedSettingCount);

    /// <summary>The candidate document a write would commit, or the refusal that stopped it becoming one.</summary>
    /// <param name="CandidateJson">The candidate document, meaningful only when <paramref name="Refusal" /> is <see langword="null" />.</param>
    /// <param name="Refusal">The refusal, or <see langword="null" /> when a candidate survived judgement.</param>
    private readonly record struct CandidateJudgement(string CandidateJson, ConfigurationWriteResult? Refusal)
    {
        public CandidateJudgement(string candidateJson)
            : this(candidateJson, Refusal: null)
        {
        }

        public CandidateJudgement(ConfigurationWriteResult refusal)
            : this(string.Empty, refusal)
        {
        }
    }
}
