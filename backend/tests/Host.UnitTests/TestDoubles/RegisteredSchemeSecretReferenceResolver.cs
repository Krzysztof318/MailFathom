// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Resolves a reference under any scheme a deployment registers, and refuses every other one.</summary>
/// <remarks>
/// <para>
/// It answers the question a start asks of material that is not the deployment's own: whether the reference an owner
/// carries reaches something. A persisted document may not carry a literal, so a test about one cannot state its
/// secret as <c>plaintext:</c>, and <see cref="PlaintextOnlySecretReferenceResolver" /> is therefore the wrong double
/// for it — this one resolves the schemes <see cref="DeclaredSecretScheme.Registered" /> names and nothing else, which
/// keeps a reference under an unserved scheme refused.
/// </para>
/// <para>The reference's target is handed back as the material, because what the material is never decides anything here.</para>
/// <para>
/// A target beginning with <see cref="UnreadableTarget" /> is the exception, and it is what a test states when the
/// question is the resolution rather than the scheme: a record may only carry a reference under a scheme this
/// deployment registers — the binder refuses anything else before the walk runs — so an unregistered scheme cannot
/// reach it, and a well-formed reference to material that is not there is the one shape that can.
/// </para>
/// </remarks>
internal sealed class RegisteredSchemeSecretReferenceResolver : ISecretReferenceResolver
{
    /// <summary>The target prefix a test writes to state a reference that reaches nothing.</summary>
    internal const string UnreadableTarget = "reaches-nothing";

    public Task<SecretResolutionResult> ResolveAsync(string? configuredValue, CancellationToken cancellationToken)
    {
        if (!SecretReference.TryParse(configuredValue, out var reference, out var grammarFailure))
        {
            return Task.FromResult(SecretResolutionResult.Failed(grammarFailure));
        }

        if (!DeclaredSecretScheme.Registered.Any(adapter => adapter.Scheme == reference.Scheme))
        {
            return Task.FromResult(SecretResolutionResult.Failed(SecretResolutionFailure.SchemeNotSupported));
        }

        return Task.FromResult(reference.Target.StartsWith(UnreadableTarget, StringComparison.Ordinal)
            ? SecretResolutionResult.Failed(SecretResolutionFailure.MaterialNotFound)
            : SecretResolutionResult.Resolved(ResolvedSecret.FromText(reference.Target), SecretMaterialSource.SchemeAdapter));
    }
}
