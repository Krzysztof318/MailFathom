// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;

namespace MailFathom.Host.Configuration.Providers;

/// <summary>Judges where one AI endpoint is and what a request to it presents, identically for both roles.</summary>
/// <remarks>
/// <para>
/// The rule these enforce is that a credential never crosses a network in the clear. It is stated that way rather than
/// as <em>the address is HTTPS</em>, because the scheme was only ever a proxy for it: what makes a plain address
/// dangerous is the secret travelling on it, and an endpoint with no secret to publish is a different situation rather
/// than an exception to the same one. That is what makes a model server the operator runs themselves reachable — the
/// ordinary shape of every local inference server is a plain address on a private network with no credential in front
/// of it — while leaving the protection around a vendor key exactly where it was.
/// </para>
/// <para>
/// What confidentiality a plain hop costs beyond the credential is not settled here, because nothing startup can read
/// decides it: a container network name and a public host name are the same string, so a rule reading the address would
/// refuse the deployment this exists to allow or accept everything, and neither is worth the false confidence.
/// <see cref="Hosting.Warnings.AiProviderTransportEncryptionWarning" /> reports the hop instead, which
/// is how a clear-text MCP endpoint is handled for the same reason.
/// </para>
/// <para>
/// The absent credential stays refused. An endpoint that needs none says so, so an operator who forgot to reference
/// their key is still told at startup rather than discovering it from a rejected request.
/// </para>
/// </remarks>
internal static class ProviderEndpointReachRules
{
    /// <summary>Reports every reason an endpoint could not be reached as declared, by reading the declaration alone.</summary>
    /// <param name="endpointDescription">How a message names this endpoint, already carrying its role and its alias.</param>
    /// <param name="declaration">The address and the three credential shapes, exactly one of which is declared.</param>
    /// <returns>One result per rule the declaration breaks, empty when it is usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declaration" /> is <see langword="null" />.</exception>
    public static IEnumerable<ValidationResult> FindConfigurationErrors(
        string endpointDescription,
        IProviderEndpointReachDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        var declaredShapes = (declaration.ApiKey is null ? 0 : 1)
            + (declaration.EntraCredential is null ? 0 : 1)
            + (declaration.Unauthenticated ? 1 : 0);

        if (declaredShapes == 0)
        {
            yield return new ValidationResult(
                $"{endpointDescription} declares neither a provider key, nor a Microsoft Entra credential, nor Unauthenticated. Exactly one of the three says what a request presents, and an endpoint that needs no credential declares Unauthenticated so that a forgotten reference stays distinguishable from a deliberate absence.",
                [nameof(IProviderEndpointReachDeclaration.ApiKey)]);
        }
        else if (declaredShapes > 1)
        {
            yield return new ValidationResult(
                $"{endpointDescription} declares more than one of a provider key, a Microsoft Entra credential, and Unauthenticated. Exactly one of the three says what a request presents.",
                [nameof(IProviderEndpointReachDeclaration.ApiKey)]);
        }

        if (declaration.Address.Length == 0)
        {
            yield break;
        }

        if (!Uri.TryCreate(declaration.Address, UriKind.Absolute, out var address)
            || (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp))
        {
            // Reported once and no further, because everything below reads the scheme of an address that parsed.
            yield return new ValidationResult(
                $"{endpointDescription} declares an Address that is not an absolute HTTP or HTTPS address.",
                [nameof(IProviderEndpointReachDeclaration.Address)]);

            yield break;
        }

        // Read from the credential blocks rather than from the negation of Unauthenticated, so an endpoint that declared
        // nothing at all is told once that it declared nothing, instead of being told it holds a credential it does not.
        var carriesACredential = declaration.ApiKey is not null || declaration.EntraCredential is not null;

        if (address.Scheme == Uri.UriSchemeHttp && carriesACredential)
        {
            yield return new ValidationResult(
                $"{endpointDescription} declares a credential and a plain http Address, which would publish that credential to anything on the network path. Give the endpoint an https address, or — where it is a server you run yourself that asks for no credential — declare Unauthenticated and remove the credential.",
                [nameof(IProviderEndpointReachDeclaration.Address)]);
        }
    }

    /// <summary>Reports whether a declared address names a hop nothing encrypts.</summary>
    /// <param name="address">The declared address, which may be empty.</param>
    /// <returns><see langword="true" /> when the address is an absolute plain HTTP one.</returns>
    /// <remarks>
    /// An empty address is the provider library's own default, which is HTTPS, so it is not one of these. The startup
    /// report reads this rather than the scheme directly, so what counts as a clear-text hop is decided once beside the
    /// rule that permits it.
    /// </remarks>
    public static bool IsReachedInClearText(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttp;
}
