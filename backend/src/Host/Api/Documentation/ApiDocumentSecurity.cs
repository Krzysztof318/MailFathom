// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Security.ApiKeys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MailFathom.Host.Api.Documentation;

/// <summary>Describes each documented operation as protected exactly when the running host protects it.</summary>
/// <remarks>
/// <para>
/// The framework derives request bodies, parameters, responses, and schemas from an endpoint, and derives nothing at
/// all about what admits a caller to it. A document left that way describes every operation as open, which is the one
/// inaccuracy a reader cannot detect by reading: the explorer would offer a call with no credential attached, the
/// deployment would refuse it, and a developer would be left deciding whether their configuration or the document was
/// wrong. So the requirement is read from the same endpoint metadata the authorization middleware reads, rather than
/// from a list of routes somebody keeps in step.
/// </para>
/// <para>
/// An operation is protected when its endpoint carries an authorization requirement and no anonymous exemption, which
/// is the framework's own rule. That makes the description follow the deployment rather than the code: a surface whose
/// credentials an operator never configured maps its routes without a requirement, and the document then says so
/// instead of describing a lock that is not on the door.
/// </para>
/// <para>
/// One scheme, named for the HTTP authentication scheme every surface actually challenges with. MailFathom accepts an
/// API key, a token from a configured authorization server, and a client assertion, and all three arrive in the same
/// <c>Authorization: Bearer</c> header — so a second entry would describe a distinction a caller has no way to act on,
/// which is the same reason a challenge names one realm.
/// </para>
/// </remarks>
internal static class ApiDocumentSecurity
{
    /// <summary>The name the security scheme is published under, and the one every requirement references.</summary>
    internal const string SchemeName = ApiKeyAuthentication.HttpAuthenticationScheme;

    /// <summary>The HTTP authentication scheme the credential is presented under, as OpenAPI spells it.</summary>
    /// <remarks>Lower case because OpenAPI compares this against the registered HTTP authentication scheme names, which are lower case; the header a client writes is unaffected.</remarks>
    private const string HttpScheme = "bearer";

    /// <summary>Publishes the credential every protected operation on this API is reached with.</summary>
    /// <param name="document">The document being generated.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Declared unconditionally rather than from the schemes the authentication framework registered, because those
    /// exist only for the surfaces this deployment enabled and are named per surface. What this describes is how a
    /// credential is presented, which is one answer for the whole API and stays the same whether or not the operator
    /// configured one.
    /// </remarks>
    internal static void DeclareCredentialScheme(OpenApiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal)
        {
            [SchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = HttpScheme,
                Description =
                    "An API key, an access token from a configured authorization server, or a client assertion. "
                    + "Credentials do not cross surfaces: one provisioned for the administrative endpoint "
                    + "authenticates nothing on the client endpoint, and the reverse.",
            },
        };
    }

    /// <summary>Marks an operation as requiring a credential when the endpoint behind it does.</summary>
    /// <param name="operation">The operation being described.</param>
    /// <param name="context">What the generator knows about the endpoint behind it.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation" /> or <paramref name="context" /> is <see langword="null" />.</exception>
    internal static void RequireCredentialWhereProtected(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        if (!IsProtected(context.Description.ActionDescriptor.EndpointMetadata))
        {
            return;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = [],
        });
    }

    /// <summary>Reports whether the authorization middleware would demand a credential for this endpoint.</summary>
    /// <remarks>An anonymous exemption wins over a requirement however the two were applied, which is the framework's own precedence and therefore the one this has to reproduce.</remarks>
    private static bool IsProtected(IList<object> endpointMetadata) =>
        !endpointMetadata.OfType<IAllowAnonymous>().Any()
        && endpointMetadata.OfType<IAuthorizeData>().Any();
}
