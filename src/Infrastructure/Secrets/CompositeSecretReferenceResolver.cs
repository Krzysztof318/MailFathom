// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Infrastructure.Secrets;

/// <summary>Interprets a configured value and dispatches it to the adapter that serves its scheme.</summary>
/// <remarks>
/// Dispatch is a lookup over the registered <see cref="ISecretSchemeResolver" /> set rather than a switch the core owns,
/// which is what makes adding a managed-store provider a registration instead of an edit here. Neither the result nor
/// any diagnostic derived from it may carry the reference target or the material.
/// </remarks>
internal sealed class CompositeSecretReferenceResolver : ISecretReferenceResolver
{
    private readonly Dictionary<SecretReferenceScheme, ISecretSchemeResolver> schemeResolvers;
    private readonly SecretValueInterpretation interpretation;

    /// <summary>Creates the dispatch over the registered scheme adapters.</summary>
    /// <param name="schemeResolvers">The registered adapters, at most one per scheme.</param>
    /// <param name="resolutionOptions">The deployment's interpretation mode.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when two adapters declare the same scheme.</exception>
    public CompositeSecretReferenceResolver(
        IEnumerable<ISecretSchemeResolver> schemeResolvers,
        SecretResolutionOptions resolutionOptions)
    {
        ArgumentNullException.ThrowIfNull(schemeResolvers);
        ArgumentNullException.ThrowIfNull(resolutionOptions);

        this.schemeResolvers = schemeResolvers.ToDictionary(schemeResolver => schemeResolver.Scheme);
        this.interpretation = resolutionOptions.Interpretation;
    }

    /// <inheritdoc />
    public async Task<SecretResolutionResult> ResolveAsync(string? configuredValue, CancellationToken cancellationToken)
    {
        // The inline-only branch comes first on purpose: parsing and then ignoring the result would leave a path on
        // which a scheme-shaped password is silently treated as a reference to somewhere else.
        if (this.interpretation == SecretValueInterpretation.InlineOnly)
        {
            return AcceptInline(configuredValue, whenAbsent: SecretResolutionFailure.ReferenceMissing);
        }

        if (!SecretReference.TryParse(configuredValue, out var reference, out var grammarFailure))
        {
            // An absent value is missing rather than inline, and a value that names a scheme but nothing after it is a
            // malformed reference rather than a plausible secret. Accepting either inline would turn an operator's
            // mistake into a credential. A literal that genuinely ends in a colon is spelled with plaintext:.
            return grammarFailure is SecretResolutionFailure.ReferenceMissing or SecretResolutionFailure.TargetMissing
                ? SecretResolutionResult.Failed(grammarFailure)
                : this.AcceptInlineWhenPermitted(configuredValue, grammarFailure);
        }

        if (!this.schemeResolvers.TryGetValue(reference.Scheme, out var schemeResolver))
        {
            return this.AcceptInlineWhenPermitted(configuredValue, SecretResolutionFailure.SchemeNotSupported);
        }

        return await schemeResolver.ResolveAsync(reference, cancellationToken);
    }

    private SecretResolutionResult AcceptInlineWhenPermitted(string? configuredValue, SecretResolutionFailure failure) =>
        this.interpretation == SecretValueInterpretation.ReferenceOrInline
            ? AcceptInline(configuredValue, failure)
            : SecretResolutionResult.Failed(failure);

    private static SecretResolutionResult AcceptInline(string? configuredValue, SecretResolutionFailure whenAbsent) =>
        string.IsNullOrEmpty(configuredValue)
            ? SecretResolutionResult.Failed(whenAbsent)
            : SecretResolutionResult.Resolved(ResolvedSecret.FromText(configuredValue), SecretMaterialSource.InlineValue);
}
