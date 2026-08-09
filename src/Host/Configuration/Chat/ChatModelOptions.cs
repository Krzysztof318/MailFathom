// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.AI.Chat;
using MailFathom.Host.Configuration.Providers;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.Chat;

/// <summary>Declares what this deployment generates text with, and what one call to it may spend.</summary>
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
/// Nothing here is a compile-time constant in code. The model, the endpoint, the output budget, and the sampling
/// parameters are all read from configuration, so changing model is an edit rather than a rebuild, and so a model
/// released after this version can be declared without one.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class ChatModelOptions : IValidatableObject
{
    /// <summary>Names every reasoning effort a declaration may state, for the message that reports one it may not.</summary>
    /// <remarks>Read from the enumeration rather than written out, so a member added later reaches the message without a second edit.</remarks>
    private static readonly string DeclaredReasoningEfforts = string.Join(", ", Enum.GetNames<ChatReasoningEffort>());

    /// <summary>Gets or sets the deployment's own name for the chat endpoint.</summary>
    /// <remarks>
    /// Everything else here is an address or a credential and neither may be written down, so this is the name a log
    /// line, a metric tag, a resilience circuit, and a failure message use. It is also the key the credential is
    /// resolved by, which is why it may not repeat an alias an embedding endpoint already declared.
    /// </remarks>
    public string Alias { get; set; } = string.Empty;

    /// <summary>Gets or sets the model identifier requests are routed to.</summary>
    /// <remarks>For a cloud deployment this is the name the operator gave the deployment rather than the vendor's model identifier, because that is the string the endpoint recognizes.</remarks>
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets which of the provider's two request APIs a call is conducted through.</summary>
    /// <remarks>
    /// Declared rather than derived from the model, because the routed model name is not a model identity: for a cloud
    /// deployment it is whatever the operator called the deployment, so deriving would mean guessing from a string they
    /// invented and a wrong guess is one nothing here could correct. Chat completions is the default because every
    /// OpenAI-compatible server offers it; the responses API is what a current reasoning model requires before it will
    /// take function tools beside a stated reasoning effort.
    /// </remarks>
    public ChatProviderApi Api { get; set; } = ChatProviderApi.ChatCompletions;

    /// <summary>Gets or sets the base address requests are sent to.</summary>
    /// <remarks>
    /// Empty uses the provider library's own default, which is what a first-party OpenAI endpoint needs. A cloud
    /// deployment sets the resource's OpenAI-compatible address, which ends in <c>/openai/v1/</c>. The scheme is not a
    /// preference: the request carries a credential, so an <c>http</c> address would publish it to anyone on the path
    /// and startup refuses one.
    /// </remarks>
    public string Address { get; set; } = string.Empty;

    /// <summary>Gets or sets the greatest number of tokens one answer may occupy.</summary>
    /// <remarks>The bound on what one call costs. Reaching it is not a failure: the answer arrives marked as cut short, and the text before the cut is real.</remarks>
    [Range(1, 200_000)]
    public int MaxOutputTokens { get; set; } = 1024;

    /// <summary>Gets or sets the sampling temperature, left unset to keep the model's own default.</summary>
    /// <remarks>
    /// Nullable rather than defaulted to a number, because several current models reject the parameter outright and
    /// sending a value one of them refuses turns every call this deployment makes into a rejected request. Writing
    /// nothing therefore has to mean sending nothing.
    /// </remarks>
    [Range(0d, 2d)]
    public float? Temperature { get; set; }

    /// <summary>Gets or sets the nucleus-sampling threshold, left unset to keep the model's own default.</summary>
    /// <remarks>Nullable for the reason <see cref="Temperature" /> is, and declared beside it rather than instead of it because a provider that accepts both documents setting only one.</remarks>
    [Range(0d, 1d)]
    public float? TopP { get; set; }

    /// <summary>Gets or sets the reasoning effort every call states, left unset to send no reasoning parameter at all.</summary>
    /// <remarks>
    /// Nullable for the reason <see cref="Temperature" /> is: a model that does not reason rejects the parameter
    /// outright. <c>None</c> is not the same as leaving it out — it states an effort of none and sends it, which is what
    /// a provider refusing function tools beside an unstated effort asks for.
    /// </remarks>
    public ChatReasoningEffort? ReasoningEffort { get; set; }

    /// <summary>Gets or sets the greatest number of turns one request carries.</summary>
    [Range(1, 512)]
    public int MaxMessagesPerRequest { get; set; } = 64;

    /// <summary>Gets or sets the greatest number of characters those turns may add up to.</summary>
    /// <remarks>
    /// A bound on what leaves the deployment, stated in the unit this side can measure. Tokens are what the provider
    /// bills and refuses by, but counting them would mean carrying the model's own tokenizer, so the ceiling is in
    /// characters and is set below what the context window allows.
    /// </remarks>
    [Range(1, 4_000_000)]
    public int MaxRequestCharacters { get; set; } = 120_000;

    /// <summary>Gets or sets the time one request may take before it is abandoned.</summary>
    /// <remarks>Longer than an embedding request's by default, because generating an answer takes as long as the answer is and an embedding call returns a fixed block of numbers.</remarks>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>Gets or sets the reference to the provider key this endpoint is authenticated with.</summary>
    /// <remarks>Absent for an endpoint authenticated with Microsoft Entra, and absent by default rather than an empty block, so secret discovery does not find an unresolvable reference nobody wrote.</remarks>
    public ConfiguredSecret? ApiKey { get; set; }

    /// <summary>Gets or sets the non-interactive Microsoft Entra credential this endpoint is authenticated with.</summary>
    /// <remarks>Absent for an endpoint authenticated with a key. Exactly one of the two is declared, and startup refuses both or neither.</remarks>
    public ProviderEntraCredentialOptions? EntraCredential { get; set; }

    /// <summary>Gets or sets whether retrieval puts its candidates to this endpoint before handing them over, and what that pass may spend.</summary>
    /// <remarks>Present rather than nullable, because every member of it has a usable default and the block's own <c>Enabled</c> is what says whether the pass runs. Off is the default and is a supported deployment.</remarks>
    public PassageRelevanceFilterOptions RelevanceFilter { get; set; } = new();

    /// <summary>Gets whether the deployment declared a chat provider at all.</summary>
    /// <remarks>Read from the alias because it is the one member with no usable default: a section an operator began writing but left without a name is not a declaration, and a section they never wrote has none either.</remarks>
    public bool IsConfigured => this.Alias.Trim().Length > 0;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!this.IsConfigured)
        {
            // A section carrying settings but no alias is the one shape worth naming: an operator who wrote a model, an
            // address, or a credential and expects the provider to be in use has to be told that nothing reads any of
            // it. What each member contributes here is whether writing it was unambiguous intent. Most have no useful
            // default, so any value at all is; the API has one, so what counts is a value other than it, which nobody
            // writes by accident. The bounds and the timeout contribute nothing either way, because a deployment that
            // accepted their defaults is indistinguishable from one that wrote them out.
            if (this.Model.Trim().Length > 0
                || this.Address.Trim().Length > 0
                || this.Api != ChatProviderApi.ChatCompletions
                || this.ReasoningEffort is not null
                || this.ApiKey is not null
                || this.EntraCredential is not null
                || this.RelevanceFilter.Enabled)
            {
                yield return new ValidationResult(
                    "The Chat section declares settings but no Alias, so no chat provider is configured and nothing in it is read. Give the endpoint an alias, or remove the section.",
                    [nameof(this.Alias)]);
            }

            yield break;
        }

        var alias = this.Alias.Trim();

        if (this.Model.Trim().Length == 0)
        {
            yield return new ValidationResult(
                $"Chat endpoint '{alias}' declares no Model, so no request could name what to route it to.",
                [nameof(this.Model)]);
        }

        if (this.RequestTimeout <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                $"Chat endpoint '{alias}' declares a RequestTimeout that is not positive, because an unbounded request would hold the work behind it open for as long as the endpoint stays silent.",
                [nameof(this.RequestTimeout)]);
        }

        // The binder accepts any number for an enum, and a value no member declares would read as a choice while naming
        // nothing — for the API, a request sent to a path this cannot reach at all.
        if (!Enum.IsDefined(this.Api))
        {
            yield return new ValidationResult(
                $"Chat endpoint '{alias}' declares an Api of '{(int)this.Api}', which names no API. State '{nameof(ChatProviderApi.ChatCompletions)}' or '{nameof(ChatProviderApi.Responses)}'.",
                [nameof(this.Api)]);
        }

        if (this.ReasoningEffort is { } effort && !Enum.IsDefined(effort))
        {
            yield return new ValidationResult(
                $"Chat endpoint '{alias}' declares a ReasoningEffort of '{(int)effort}', which names no effort. State one of {DeclaredReasoningEfforts}, or leave it unset to send no reasoning parameter.",
                [nameof(this.ReasoningEffort)]);
        }

        if (this.Address.Length > 0 && !IsUsableAddress(this.Address))
        {
            yield return new ValidationResult(
                $"Chat endpoint '{alias}' declares an Address that is not an absolute HTTPS address.",
                [nameof(this.Address)]);
        }

        if (this.ApiKey is null == this.EntraCredential is null)
        {
            yield return new ValidationResult(
                $"Chat endpoint '{alias}' declares neither a provider key nor a Microsoft Entra credential, or declares both. Exactly one authenticates an endpoint.",
                [nameof(this.ApiKey)]);
        }

        foreach (var error in this.EntraCredential?.FindConfigurationErrors(alias) ?? [])
        {
            yield return error;
        }

        foreach (var error in this.RelevanceFilter.FindConfigurationErrors(alias, this.MaxMessagesPerRequest))
        {
            yield return error;
        }
    }

    /// <summary>Builds the endpoint this declaration describes.</summary>
    /// <returns>The endpoint.</returns>
    /// <exception cref="UriFormatException">Thrown when the declared address is not a URI.</exception>
    /// <remarks>Called only after validation has passed, so what is left here is mapping rather than checking.</remarks>
    public ChatEndpoint ToEndpoint() => new(
        this.Alias.Trim(),
        this.Address is { Length: > 0 } address ? new Uri(address, UriKind.Absolute) : null,
        this.Model.Trim(),
        this.Api);

    private static bool IsUsableAddress(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var parsed)
        && parsed.Scheme == Uri.UriSchemeHttps;
}
