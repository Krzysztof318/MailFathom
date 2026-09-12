// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.AI.Chat;
using MailFathom.Host.Configuration.Providers;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.Chat;

/// <summary>One model this deployment may generate text with: where it is, what it is routed to, and what one call to it may spend.</summary>
/// <remarks>
/// <para>
/// A block of the declared array rather than the section itself, which is what lets a deployment put the pass a reader
/// waits for on a small fast model while questions go to the one that answers them well. Everything a request needs is
/// here, so two models are two addresses, two credentials, and two sets of parameters rather than one endpoint with an
/// override on the model name.
/// </para>
/// <para>
/// Nothing here is a compile-time constant in code. The model, the endpoint, the output budget, and the sampling
/// parameters are all read from configuration, so changing model is an edit rather than a rebuild, and so a model
/// released after this version can be declared without one.
/// </para>
/// </remarks>
internal sealed class ChatModelDeclarationOptions : IProviderEndpointReachDeclaration
{
    /// <summary>Gets or sets the deployment's own name for this model, which every reference to it is written with.</summary>
    /// <remarks>
    /// Everything else here is an address or a credential and neither may be written down, so this is the name a log
    /// line, a metric tag, a resilience circuit, and a failure message use. It is also the key the credential is
    /// resolved by, which is why it may repeat neither another declared model's nor an embedding endpoint's.
    /// </remarks>
    public string Alias { get; set; } = string.Empty;

    /// <summary>Gets or sets the model identifier requests are routed to.</summary>
    /// <remarks>For a cloud deployment this is the name the operator gave the deployment rather than the vendor's model identifier, because that is the string the endpoint recognizes.</remarks>
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the model name a client is told answered its question, and empty to publish nothing beyond the alias.</summary>
    /// <remarks>
    /// A second name beside <see cref="Model" /> rather than a reuse of it, because that one is a routing name: for a
    /// cloud deployment it is whatever the operator called the resource, and it can carry a tenant, a project, or an
    /// environment in it. Publishing it to every signed-in client would disclose deployment topology to answer a
    /// question about model quality, so
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0022-what-an-ai-run-reports-about-cost-cancellation-and-the-model.md">ADR 0022</see>
    /// makes the disclosure a declaration: write the vendor's identifier as you wish it stated — <c>gpt-4o</c> while
    /// requests route to <c>prod-eu-4o-2</c> — or leave it empty and a run names the alias alone.
    /// </remarks>
    public string PublishedModel { get; set; } = string.Empty;

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
    /// deployment sets the resource's OpenAI-compatible address, which ends in <c>/openai/v1/</c>. A plain <c>http</c>
    /// address is refused wherever this endpoint holds a credential, because the request would publish it to anything
    /// on the path; it is accepted for an endpoint declaring <see cref="Unauthenticated" />, which is the shape of a
    /// model server the operator runs themselves.
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
    /// <para>
    /// Nullable for the reason <see cref="Temperature" /> is: a model that does not reason rejects the parameter
    /// outright. Writing <c>none</c> is not the same as leaving it out — it states an effort of none and sends it, which
    /// is what a provider refusing function tools beside an unstated effort asks for.
    /// </para>
    /// <para>
    /// The provider's own word rather than a name chosen here, and unvalidated against any list for the reason
    /// <see cref="Model" /> is: which levels exist belongs to the model, <c>xhigh</c> arrived after the levels beneath
    /// it, and a set fixed at build time would make a release the price of using the next one. What startup checks is
    /// the shape, so a value no provider could read as a level fails here rather than on the first question.
    /// </para>
    /// </remarks>
    public string? ReasoningEffort { get; set; }

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

    /// <summary>Gets or sets the greatest number of octets the images of one request may add up to, or zero for a model that is sent none.</summary>
    /// <remarks>
    /// <para>
    /// The second bound on what leaves the deployment, in the unit a picture is measured in: characters bound an image
    /// not at all, because a photograph is a turn of a few words and several megabytes. Four mebibytes admits every
    /// photograph a phone or a scanner attaches, and is set below the figure the providers this reaches publish for
    /// themselves — each of them carries an image base64-encoded, which is a third larger again than what is counted
    /// here.
    /// </para>
    /// <para>
    /// Zero says this model is sent no image, which is the right declaration for one that cannot read one, and is a
    /// refusal at the boundary rather than a turn quietly sent without its picture. It is not the setting that turns
    /// image description off — that is <c>Embeddings:ImageDescription:Enabled</c>, and this one bounds what any caller
    /// carrying a picture may send.
    /// </para>
    /// </remarks>
    [Range(0, 64 * 1024 * 1024)]
    public int MaxRequestImageOctets { get; set; } = 4 * 1024 * 1024;

    /// <summary>Gets or sets the time one request may take before it is abandoned.</summary>
    /// <remarks>Longer than an embedding request's by default, because generating an answer takes as long as the answer is and an embedding call returns a fixed block of numbers.</remarks>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>Gets or sets the reference to the provider key this endpoint is authenticated with.</summary>
    /// <remarks>Absent for an endpoint reached with Microsoft Entra or with no credential at all, and absent by default rather than an empty block, so secret discovery does not find an unresolvable reference nobody wrote.</remarks>
    public ConfiguredSecret? ApiKey { get; set; }

    /// <summary>Gets or sets the non-interactive Microsoft Entra credential this endpoint is authenticated with.</summary>
    /// <remarks>Absent for an endpoint reached with a key or with no credential at all. Exactly one of the three shapes is declared, and startup refuses none of them or more than one.</remarks>
    public ProviderEntraCredentialOptions? EntraCredential { get; set; }

    /// <summary>Gets or sets whether this endpoint asks for no credential, so a request presents none.</summary>
    /// <remarks>
    /// The shape of a model server the operator runs themselves, which admits a caller by being reachable only from the
    /// network it was put on. Written rather than inferred from the absence of the other two, because an omission is
    /// exactly what a forgotten key reference looks like and startup has to go on refusing that.
    /// </remarks>
    public bool Unauthenticated { get; set; }

    /// <summary>Gets the headers every request to this model carries beside whatever the credential writes.</summary>
    /// <remarks>
    /// Empty for a model reached directly, which is the ordinary deployment. What fills it is a gateway in front of
    /// several models that routes by a header — and each value is a secret reference, so it is resolved per request and
    /// never written into a configuration file.
    /// </remarks>
    public IList<ProviderEndpointHeaderOptions> ExtraHeaders { get; } = [];

    /// <summary>Reports everything an operator must fix before this model could be called.</summary>
    /// <param name="position">Where in the declared array this block sits, which is how a message names a block whose alias is missing.</param>
    /// <returns>One result per rule the declaration breaks, empty when it is usable.</returns>
    /// <remarks>
    /// The attribute bounds are run here rather than left to the options framework, because that framework validates
    /// the section it bound and never descends into the elements of a collection inside it. A bound stated as an
    /// annotation on a block of an array is therefore enforced by nothing at all unless the block runs it itself, and a
    /// value outside it would reach a provider on every call the deployment made.
    /// </remarks>
    public IEnumerable<ValidationResult> FindConfigurationErrors(int position) =>
        this.FindDeclaredErrors(position).Select(error => KeyedToThisBlock(error, position));

    /// <summary>Builds the endpoint this declaration describes.</summary>
    /// <returns>The endpoint.</returns>
    /// <exception cref="UriFormatException">Thrown when the declared address is not a URI.</exception>
    /// <remarks>Called only after validation has passed, so what is left here is mapping rather than checking.</remarks>
    public ChatEndpoint ToEndpoint() => new(
        this.Alias.Trim(),
        this.Address is { Length: > 0 } address ? new Uri(address, UriKind.Absolute) : null,
        this.Model.Trim(),
        this.Api,
        this.PublishedModel.Trim());

    /// <summary>Names a result against the block it came from, so a message carries the key an operator edits rather than a property name.</summary>
    /// <remarks>
    /// A member name reaches an operator as <c>Chat:&lt;member&gt;</c>, which was the whole key while the section
    /// declared one model and names nothing now that it declares an array. Re-keying here is what keeps every rule —
    /// this block's own and the annotations the framework produced — reported against <c>Chat:Models:&lt;position&gt;</c>.
    /// </remarks>
    private static ValidationResult KeyedToThisBlock(ValidationResult error, int position) => new(
        error.ErrorMessage,
        [.. error.MemberNames.Select(member => $"{nameof(ChatModelOptions.Models)}:{position}:{member}")]);

    private IEnumerable<ValidationResult> FindDeclaredErrors(int position)
    {
        foreach (var error in this.FindBoundErrors())
        {
            yield return error;
        }

        var alias = this.Alias.Trim();

        if (alias.Length == 0)
        {
            yield return new ValidationResult(
                $"The chat model declared at {ChatModelOptions.SectionName}:{nameof(ChatModelOptions.Models)}:{position} has no Alias, so nothing could name it. The alias is how every reference, every log line, and every credential reaches this model.",
                [nameof(this.Alias)]);

            yield break;
        }

        var description = $"Chat model '{alias}'";

        if (this.Model.Trim().Length == 0)
        {
            yield return new ValidationResult(
                $"{description} declares no Model, so no request could name what to route it to.",
                [nameof(this.Model)]);
        }

        if (this.RequestTimeout <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                $"{description} declares a RequestTimeout that is not positive, because an unbounded request would hold the work behind it open for as long as the endpoint stays silent.",
                [nameof(this.RequestTimeout)]);
        }

        // The binder accepts any number for an enum, and a value no member declares would read as a choice while naming
        // nothing — for the API, a request sent to a path this cannot reach at all.
        if (!Enum.IsDefined(this.Api))
        {
            yield return new ValidationResult(
                $"{description} declares an Api of '{(int)this.Api}', which names no API. State '{nameof(ChatProviderApi.ChatCompletions)}' or '{nameof(ChatProviderApi.Responses)}'.",
                [nameof(this.Api)]);
        }

        // The shape alone, never the vocabulary: which levels a model offers is the model's, so a list held here would
        // refuse the next one a provider adds and make a release the price of using it.
        if (this.ReasoningEffort is { } effort && !ChatGenerationPlan.IsUsableReasoningEffort(effort))
        {
            yield return new ValidationResult(
                $"{description} declares a ReasoningEffort that is not a single word a provider could read as a level. Write the level the model documents, such as 'none', 'low', or 'high', or leave it unset to send no reasoning parameter.",
                [nameof(this.ReasoningEffort)]);
        }

        foreach (var error in ProviderEndpointReachRules.FindConfigurationErrors(description, this))
        {
            yield return error;
        }

        foreach (var error in this.EntraCredential?.FindConfigurationErrors(alias) ?? [])
        {
            yield return error;
        }

        foreach (var error in ProviderEndpointHeaderOptions.FindConfigurationErrors(description, [.. this.ExtraHeaders]))
        {
            yield return error;
        }
    }

    /// <summary>Builds the plan a request to this model runs on.</summary>
    /// <returns>The plan, without the fallback the reference that selected this model may name.</returns>
    /// <exception cref="UriFormatException">Thrown when the declared address is not a URI.</exception>
    public ChatGenerationPlan ToPlan() => ChatGenerationPlan.Create(
        this.ToEndpoint(),
        this.MaxOutputTokens,
        this.Temperature,
        this.TopP,
        this.ReasoningEffort,
        this.MaxMessagesPerRequest,
        this.MaxRequestCharacters,
        this.MaxRequestImageOctets,
        this.RequestTimeout);

    private List<ValidationResult> FindBoundErrors()
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(this, new ValidationContext(this), results, validateAllProperties: true);

        return results;
    }
}
