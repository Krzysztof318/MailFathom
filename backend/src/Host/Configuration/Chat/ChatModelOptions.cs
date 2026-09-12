// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Host.Configuration.Chat;

/// <summary>Declares which models this deployment generates text with, and which of them each capability runs on.</summary>
/// <remarks>
/// <para>
/// A configuration root of its own beside <c>Embeddings</c> rather than a section inside it, because the two are
/// separate choices with separate consequences. Without an embedding provider, semantic search is off and lexical
/// search continues; without a chat provider, search is unaffected and only the answering capability stops being
/// offered. An instance may reasonably have one and not the other, so a single "AI is configured" section would be
/// wrong in both directions.
/// </para>
/// <para>
/// An absent section is a valid deployment rather than a startup failure, exactly as an absent embedding section is.
/// Nothing is generated, no chat provider is called, no credential is needed, and every read path serves as it always
/// did.
/// </para>
/// <para>
/// The section is a declared array and the references into it rather than one endpoint, which is the difference from
/// every release before this one. What a model is — its address, its credential, its parameters, its bounds — is
/// written once in <see cref="Models" /> under an alias, and a capability names that alias. A deployment with one model
/// writes one block and nothing else: <see cref="MainModel" /> may be left unwritten where exactly one is declared,
/// because there is nothing for it to choose between.
/// </para>
/// <para>
/// The declaration is read again after an edit rather than once at startup, so correcting a model the provider refused
/// costs an edit rather than a restart of a process that is mid-synchronization. Two things it says are read while the
/// host composes itself and cannot follow a reload: whether the section declares a model at all, and whether each
/// capability runs, because each decides which services exist.
/// <see cref="ChatDeclarationRules.FindChangesNeedingRestart" /> refuses a candidate that moves either.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class ChatModelOptions : IValidatableObject
{
    /// <summary>The configuration section this declaration is bound from.</summary>
    public const string SectionName = "Chat";

    /// <summary>Gets the models this deployment may generate text with, each under an alias of its own.</summary>
    /// <remarks>Empty is the deployment that declared no chat provider, which is supported and is what turns every capability below off.</remarks>
    public IList<ChatModelDeclarationOptions> Models { get; } = [];

    /// <summary>Gets or sets which declared model answers questions, and which one answers where it could not.</summary>
    /// <remarks>
    /// The model every capability runs on unless it names one of its own. It may be left unwritten where exactly one
    /// model is declared — there is nothing to choose — and must be written where more than one is, because a
    /// deployment that declared two models and said nothing has not stated which one answers.
    /// </remarks>
    public ChatModelReferenceOptions MainModel { get; set; } = new();

    /// <summary>Gets or sets whether retrieval puts its candidates to a model before handing them over, and what that pass may spend.</summary>
    /// <remarks>Present rather than nullable, because every member of it has a usable default and the block's own <c>Enabled</c> is what says whether the pass runs. Off is the default and is a supported deployment.</remarks>
    public PassageRelevanceFilterOptions RelevanceFilter { get; set; } = new();

    /// <summary>Gets or sets whether arriving mail is read into the marks a list row draws.</summary>
    /// <remarks>Present rather than nullable for the reason the block above is: its own <c>Enabled</c> is what says whether the derivation runs, and off is the default and a supported deployment.</remarks>
    public EmailEnrichmentOptions Enrichment { get; set; } = new();

    /// <summary>Gets or sets whether a conversation is read into the state a client draws beside it.</summary>
    /// <remarks>Present rather than nullable for the reason the two blocks above are: its own <c>Enabled</c> is what says whether the derivation runs, and off is the default and a supported deployment.</remarks>
    public ThreadStateOptions ThreadState { get; set; } = new();

    /// <summary>Gets or sets whether a reply is drafted from the conversation it answers, and where its manner comes from.</summary>
    /// <remarks>Present rather than nullable for the reason the blocks above are: its own <c>Enabled</c> is what says whether a drafting runs, and on is the default for the reason the block beneath it is on — the block itself says why, and what turning it off leaves is the composer somebody writes in themselves.</remarks>
    public ReplyDraftingOptions ReplyDrafting { get; set; } = new();

    /// <summary>Gets or sets which model the cleaned rendering of a message body is decided by.</summary>
    /// <remarks>Present rather than nullable for the reason the blocks above are, and the one of them that carries no switch at all — the block itself says why, and what decides whether a body is cleaned is the reader's own preference.</remarks>
    public BodyCleanupOptions BodyCleanup { get; set; } = new();

    /// <summary>Gets or sets whether a sentence typed into the search field is read into the filters it describes.</summary>
    /// <remarks>Present rather than nullable for the reason the three blocks above are, and on by default as the block above it is — the block itself says why, and what turning it off leaves is the word search every deployment serves.</remarks>
    public MailSearchPhrasingOptions SearchPhrasing { get; set; } = new();

    /// <summary>Gets whether the deployment declared a chat provider at all.</summary>
    /// <remarks>Read from the declared models and the main model together: a section declaring models nobody chose between answers no question, and a section declaring none has nothing to choose from.</remarks>
    public bool IsConfigured => this.FindMainModel() is not null;

    /// <summary>Gets every alias the section declares a model under, as written.</summary>
    public IReadOnlyCollection<string> DeclaredAliases => [.. this.Models
        .Select(model => model.Alias.Trim())
        .Where(alias => alias.Length > 0)];

    /// <summary>Finds the model an alias names.</summary>
    /// <param name="alias">The alias a reference carries.</param>
    /// <returns>The declaration, or <see langword="null" /> when nothing declares that alias.</returns>
    /// <remarks>Matched trimmed and without case, because that is how the alias is matched everywhere else it is used — a credential resolved by it would otherwise reach one model while a log line named another.</remarks>
    public ChatModelDeclarationOptions? FindModel(string alias)
    {
        var wanted = alias?.Trim() ?? string.Empty;

        return wanted.Length == 0
            ? null
            : this.Models.FirstOrDefault(
                model => string.Equals(model.Alias.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Finds the model every capability runs on unless it names one of its own.</summary>
    /// <returns>The declaration, or <see langword="null" /> when the deployment declared no chat provider or stated no choice between several.</returns>
    /// <remarks>
    /// A section declaring exactly one model and naming none resolves to that one, which is what keeps the ordinary
    /// deployment to a single block with no reference written beside it. More than one and nothing named is not a
    /// default anything could pick without guessing, so it resolves to nothing and validation refuses it.
    /// </remarks>
    public ChatModelDeclarationOptions? FindMainModel()
    {
        if (this.MainModel.NamesModel)
        {
            return this.FindModel(this.MainModel.Alias);
        }

        return this.Models.Count == 1 && this.Models[0].Alias.Trim().Length > 0
            ? this.Models[0]
            : null;
    }

    /// <summary>Finds the model a reference names, falling back to the main model where it names none.</summary>
    /// <param name="reference">The capability's own reference.</param>
    /// <returns>The declaration, or <see langword="null" /> when neither the reference nor the section resolves to one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reference" /> is <see langword="null" />.</exception>
    public ChatModelDeclarationOptions? FindModelFor(ChatModelReferenceOptions reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return reference.NamesModel ? this.FindModel(reference.Alias) : this.FindMainModel();
    }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (this.Models.Count == 0)
        {
            // A section carrying capability settings but no model at all is the one shape worth naming: an operator who
            // switched a derivation on and expects it to run has to be told that nothing does. What each member
            // contributes here is whether writing it was unambiguous intent, so the two blocks that are on by default
            // are absent from the reading — their `Enabled` reads true on a section nobody wrote, and taking that as
            // intent would refuse every deployment that left the section alone.
            if (this.MainModel.NamesModel
                || this.RelevanceFilter.Enabled
                || this.Enrichment.Enabled
                || this.ThreadState.Enabled
                || this.BodyCleanup.Model.NamesModel)
            {
                yield return new ValidationResult(
                    $"The {SectionName} section declares settings but no model under {SectionName}:{nameof(this.Models)}, so no chat provider is configured and nothing in it is read. Declare a model, or remove the section.",
                    [nameof(this.Models)]);
            }

            yield break;
        }

        foreach (var error in this.Models.SelectMany((model, position) => model.FindConfigurationErrors(position)))
        {
            yield return error;
        }

        foreach (var error in this.FindRepeatedAliases())
        {
            yield return error;
        }

        foreach (var error in this.FindReferenceErrors())
        {
            yield return error;
        }

        if (this.FindMainModel() is { } mainModel)
        {
            foreach (var error in this.RelevanceFilter.FindConfigurationErrors(
                mainModel.Alias.Trim(),
                mainModel.MaxMessagesPerRequest))
            {
                yield return error;
            }
        }
    }

    private IEnumerable<ValidationResult> FindRepeatedAliases()
    {
        var repeated = this.Models
            .Select(model => model.Alias.Trim())
            .Where(alias => alias.Length > 0)
            .GroupBy(alias => alias, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (var alias in repeated)
        {
            yield return new ValidationResult(
                $"The alias '{alias}' is declared by more than one chat model. An alias names one endpoint, because it is what a credential, a resilience circuit, and a log line are keyed by.",
                [nameof(this.Models)]);
        }
    }

    private IEnumerable<ValidationResult> FindReferenceErrors()
    {
        var declared = this.DeclaredAliases;

        foreach (var error in this.MainModel.FindConfigurationErrors(
            $"{SectionName}:{nameof(this.MainModel)}",
            declared))
        {
            yield return error;
        }

        foreach (var error in this.BodyCleanup.Model.FindConfigurationErrors(
            $"{SectionName}:{nameof(this.BodyCleanup)}:{nameof(BodyCleanupOptions.Model)}",
            declared))
        {
            yield return error;
        }

        if (this.FindMainModel() is null)
        {
            yield return new ValidationResult(
                this.MainModel.NamesModel
                    ? $"{SectionName}:{nameof(this.MainModel)} names a model no block declares, so no capability has anywhere to send a request."
                    : $"{SectionName}:{nameof(this.MainModel)} names no model, and {this.Models.Count} are declared. State which one answers; it may be left unwritten only where exactly one model is declared.",
                [nameof(this.MainModel)]);
        }
    }
}
