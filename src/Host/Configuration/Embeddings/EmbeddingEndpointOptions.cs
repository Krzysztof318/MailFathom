// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.AI.Embeddings;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.Embeddings;

/// <summary>Declares one endpoint of the embedding chain: the geometry it serves, where it is, and how it is authenticated.</summary>
/// <remarks>
/// <para>
/// The geometry is declared per endpoint rather than once for the chain, and that is the point rather than a
/// repetition to tidy away. A fallback is another route to one vector space and never a second one, so the properties
/// that decide whether two vectors are comparable have to be stated where they could disagree — which is what lets
/// startup refuse a chain that does and name the property it differs on. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>.
/// </para>
/// <para>
/// Nothing here is a compile-time constant in code. The provider, the model, the width, the metric, and the
/// preparation are all read from configuration, so changing model is an edit and an activation rather than a rebuild,
/// and so a model released after this version can be declared without one.
/// </para>
/// </remarks>
internal sealed class EmbeddingEndpointOptions
{
    /// <summary>Gets or sets the deployment's own name for this endpoint.</summary>
    /// <remarks>
    /// Everything else here is an address or a credential and neither may be written down, so this is the name a log
    /// line, a metric tag, a resilience circuit, and a failure message use. It is also the key the credential is
    /// resolved by, which is why it has to be unique within the chain.
    /// </remarks>
    public string Alias { get; set; } = string.Empty;

    /// <summary>Gets or sets the vendor whose model defines the space, which is part of what a stored vector is attributed to.</summary>
    /// <remarks>The vendor rather than the endpoint, so one model reached through a first-party API and through a cloud deployment of it is one vector space rather than two.</remarks>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Gets or sets the model identifier the vendor publishes.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the model version the vendor exposes, where it exposes one.</summary>
    /// <remarks>Empty is a vendor that versions nothing, which is the ordinary case: most replace a model rather than version it.</remarks>
    public string ModelVersion { get; set; } = string.Empty;

    /// <summary>Gets or sets what is sent as the model of a request, where that differs from the vendor's identifier.</summary>
    /// <remarks>
    /// A cloud deployment routes on the name the operator gave the deployment rather than on the vendor's model
    /// identifier, and the two are separate here because they answer separate questions: this one says which string
    /// the endpoint recognizes, while <see cref="Model" /> says which model produced a vector and is therefore part of
    /// what makes two vectors comparable. Empty means the two are the same.
    /// </remarks>
    public string RoutedModelName { get; set; } = string.Empty;

    /// <summary>Gets or sets the width of the vectors this endpoint is asked for.</summary>
    /// <remarks>
    /// A setting rather than a property of the model, because some providers take it as a request parameter and
    /// because a model narrowed to what the database can index has a stored width other than its nominal one. What is
    /// declared here is what the stored vectors have, and therefore what the profile records.
    /// </remarks>
    public int Dimension { get; set; }

    /// <summary>Gets or sets how distance is measured between two vectors of this space.</summary>
    public EmbeddingDistanceMetric DistanceMetric { get; set; } = EmbeddingDistanceMetric.Cosine;

    /// <summary>Gets or sets the number of characters a passage is cut to before it is sent.</summary>
    /// <remarks>This is what the model sees, so it changes what a vector means and is part of the profile. It is not the ceiling a deployment puts on what it spends per message, which changes how many vectors exist rather than what any of them means.</remarks>
    public int InputCharacterLimit { get; set; } = 8000;

    /// <summary>Gets or sets the instruction or prefix the model requires of a passage.</summary>
    /// <remarks>Empty is a model that asks for nothing, which is what every model this release supports does. A blank string of spaces is refused rather than stored, because it would register a second profile for a space identical to one already registered.</remarks>
    public string PassageInstruction { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the vectors of this space are of unit length.</summary>
    public bool NormalizeVectors { get; set; } = true;

    /// <summary>Gets or sets the base address requests are sent to.</summary>
    /// <remarks>
    /// Empty uses the provider library's own default, which is what a first-party OpenAI endpoint needs. A cloud
    /// deployment sets the resource's OpenAI-compatible address, which ends in <c>/openai/v1/</c>. The scheme is not a
    /// preference: the request carries a credential, so an <c>http</c> address would publish it to anyone on the path
    /// and startup refuses one.
    /// </remarks>
    public string Address { get; set; } = string.Empty;

    /// <summary>Gets or sets whether this endpoint honours a requested vector width.</summary>
    /// <remarks>
    /// Declared rather than inferred from the model name, for the reason the model is never a compile-time constant: a
    /// table of which models accept the parameter would be a list of model names in code and would be wrong the week
    /// after it was written. With it on, the narrower space is asked for, which a model trained for it answers already
    /// normalized; with it off, a wider answer is cut down only where the deployment allows trimming.
    /// </remarks>
    public bool SupportsRequestedDimension { get; set; } = true;

    /// <summary>Gets or sets the reference to the provider key this endpoint is authenticated with.</summary>
    /// <remarks>Absent for an endpoint authenticated with Microsoft Entra, and absent by default rather than an empty block, so secret discovery does not find an unresolvable reference nobody wrote.</remarks>
    public ConfiguredSecret? ApiKey { get; set; }

    /// <summary>Gets or sets the non-interactive Microsoft Entra credential this endpoint is authenticated with.</summary>
    /// <remarks>Absent for an endpoint authenticated with a key. Exactly one of the two is declared, and startup refuses both or neither.</remarks>
    public EmbeddingEntraCredentialOptions? EntraCredential { get; set; }

    /// <summary>Reports every reason this endpoint could not be used, by reading the declaration alone.</summary>
    /// <returns>One result per rule this declaration breaks.</returns>
    /// <remarks>
    /// Every bound is checked here rather than through data annotations on the members above, because the options
    /// framework validates annotations on the root object it bound and not on the elements of a collection inside it —
    /// so an annotation here would read as a rule while enforcing nothing.
    /// </remarks>
    public IEnumerable<ValidationResult> FindConfigurationErrors()
    {
        var alias = this.Alias.Trim();

        if (alias.Length == 0)
        {
            yield return new ValidationResult(
                "An embedding endpoint declares an Alias, which is what its credential, its resilience circuit, and every log line naming it are keyed by.");

            yield break;
        }

        if (this.Dimension > IndexableVectorWidth.GreatestStorable)
        {
            yield return Error(
                alias,
                $"declares a Dimension of {this.Dimension}, above the {IndexableVectorWidth.GreatestStorable} a vector column stores.");
        }

        if (this.ApiKey is null == this.EntraCredential is null)
        {
            yield return Error(
                alias,
                "declares neither a provider key nor a Microsoft Entra credential, or declares both. Exactly one authenticates an endpoint.");
        }

        if (this.Address.Length > 0 && !IsUsableAddress(this.Address))
        {
            yield return Error(alias, "declares an Address that is not an absolute HTTPS address.");
        }

        if (this.PassageInstruction.Length > 0 && this.PassageInstruction.Trim().Length == 0)
        {
            yield return Error(
                alias,
                "declares a PassageInstruction of whitespace. Leave it empty for a model that requires none, so the space is not registered twice under two spellings of the same preparation.");
        }

        foreach (var error in this.EntraCredential?.FindConfigurationErrors(alias) ?? [])
        {
            yield return error;
        }
    }

    /// <summary>Builds the endpoint this declaration describes.</summary>
    /// <returns>The endpoint.</returns>
    /// <exception cref="ArgumentException">Thrown when a declared value is not one an identity accepts.</exception>
    /// <remarks>Called only after validation has passed, so what is left here is mapping rather than checking.</remarks>
    public EmbeddingEndpoint ToEndpoint()
    {
        var model = this.Model.Trim();
        var identity = EmbeddingProfileIdentity.Create(
            this.Provider.Trim(),
            model,
            this.ModelVersion.Trim() is { Length: > 0 } version ? version : null,
            this.Dimension,
            this.DistanceMetric,
            EmbeddingInputPreparation.Create(
                this.InputCharacterLimit,
                this.PassageInstruction is { Length: > 0 } instruction ? instruction : null,
                this.NormalizeVectors));

        return new EmbeddingEndpoint(
            this.Alias.Trim(),
            identity,
            this.Address is { Length: > 0 } address ? new Uri(address, UriKind.Absolute) : null,
            this.RoutedModelName.Trim() is { Length: > 0 } routedName ? routedName : model,
            this.SupportsRequestedDimension);
    }

    private static bool IsUsableAddress(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var parsed)
        && parsed.Scheme == Uri.UriSchemeHttps;

    private static ValidationResult Error(string endpointAlias, string detail) =>
        new($"Embedding endpoint '{endpointAlias}' {detail}");
}
