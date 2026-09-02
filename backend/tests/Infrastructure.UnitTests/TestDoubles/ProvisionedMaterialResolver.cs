// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>Hands back exactly the bytes a deployment provisioned, without touching the file system.</summary>
/// <remarks>
/// Hand-written rather than substituted because every call has to hand back a fresh <see cref="ResolvedSecret" />: the
/// caller owns what it resolved and erases it when its operation ends, so one instance returned twice would have the
/// second consumer read a buffer the first already zeroed. Every instance it issued is kept, which is what lets a test
/// assert the erasure rather than infer it.
/// </remarks>
internal sealed class ProvisionedMaterialResolver : ISecretReferenceResolver
{
    private readonly Dictionary<string, (byte[] Material, SecretMaterialSource Source)> provisioned = new(StringComparer.Ordinal);
    private readonly List<ResolvedSecret> issued = [];

    /// <summary>Gets every piece of material this resolver has handed out, so a test can assert it was erased.</summary>
    public IReadOnlyList<ResolvedSecret> IssuedMaterial => this.issued;

    /// <summary>Provisions binary material behind a reference.</summary>
    public void Provision(
        string secretReference,
        byte[] material,
        SecretMaterialSource source = SecretMaterialSource.SchemeAdapter) =>
        this.provisioned[secretReference] = (material, source);

    /// <summary>Provisions text material behind a reference, which is how PEM and passwords arrive.</summary>
    public void ProvisionText(
        string secretReference,
        string material,
        SecretMaterialSource source = SecretMaterialSource.SchemeAdapter) =>
        this.Provision(secretReference, Encoding.UTF8.GetBytes(material), source);

    /// <inheritdoc />
    public Task<SecretResolutionResult> ResolveAsync(string? configuredValue, CancellationToken cancellationToken)
    {
        if (configuredValue is null || !this.provisioned.TryGetValue(configuredValue, out var entry))
        {
            return Task.FromResult(SecretResolutionResult.Failed(SecretResolutionFailure.MaterialNotFound));
        }

        var material = ResolvedSecret.FromBytes(entry.Material);
        this.issued.Add(material);

        return Task.FromResult(SecretResolutionResult.Resolved(material, entry.Source));
    }
}
