// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Secrets;

namespace MailMcp.Infrastructure.UnitTests;

/// <summary>Hands back exactly the bytes a deployment provisioned, without touching the file system.</summary>
internal sealed class ProvisionedMaterialResolver : ISecretReferenceResolver
{
    private readonly Dictionary<string, (byte[] Material, SecretMaterialSource Source)> provisioned = new(StringComparer.Ordinal);
    private readonly List<ResolvedSecret> issued = [];

    public IReadOnlyList<ResolvedSecret> IssuedMaterial => this.issued;

    public void Provision(
        string secretReference,
        byte[] material,
        SecretMaterialSource source = SecretMaterialSource.SchemeAdapter) =>
        this.provisioned[secretReference] = (material, source);

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
