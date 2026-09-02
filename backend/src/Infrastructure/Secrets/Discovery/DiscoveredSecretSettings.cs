// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.Resolution;

namespace MailFathom.Infrastructure.Secrets.Discovery;

/// <summary>Everything one walk of a bound options graph found about its secret-bearing settings.</summary>
/// <param name="Blocks">Every <see cref="ConfiguredSecret" /> the walk reached, in discovery order.</param>
/// <param name="RawSecretPropertyPaths">
/// The configuration paths of <see cref="string" /> properties whose name announces a secret. Each one bypasses the
/// block shape and every rule that depends on it, so the host refuses to start rather than binding a secret it cannot
/// validate, resolve, or erase.
/// </param>
public sealed record DiscoveredSecretSettings(
    IReadOnlyList<DiscoveredSecret> Blocks,
    IReadOnlyList<string> RawSecretPropertyPaths)
{
    /// <summary>Reports every secret whose declared identity or lifetime the host cannot use.</summary>
    /// <returns>One error per faulty declaration, empty when every discovered block declares both usably.</returns>
    /// <remarks>
    /// <para>
    /// The check belongs to the walk's result rather than to a consumer, because uniqueness is a property of the whole
    /// set and no single block can answer it. The scope of that uniqueness is one walk, which is one bound
    /// configuration root: names identify secrets to an operator reading one section, and requiring them to be unique
    /// across sections would make adding a section to a working deployment a source of collisions it cannot see.
    /// </para>
    /// <para>
    /// Names are compared ignoring case. Two names differing only in case are one identity to everybody who reads
    /// them, and accepting both would leave a rotation instruction ambiguous at the moment it matters.
    /// </para>
    /// </remarks>
    public IReadOnlyList<SecretDeclarationError> FindDeclarationErrors()
    {
        var errors = new List<SecretDeclarationError>();
        var claimedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in this.Blocks)
        {
            errors.AddRange(FindNameErrors(block, claimedNames));
            errors.AddRange(FindLifetimeErrors(block));
        }

        return errors;
    }

    private static IEnumerable<SecretDeclarationError> FindNameErrors(
        DiscoveredSecret block,
        HashSet<string> claimedNames)
    {
        if (string.IsNullOrEmpty(block.Secret.Name))
        {
            yield return new SecretDeclarationError(block.ConfigurationPath, SecretDeclarationFailure.NameMissing);

            yield break;
        }

        if (!SecretName.TryCreate(block.Secret.Name, out var name))
        {
            yield return new SecretDeclarationError(block.ConfigurationPath, SecretDeclarationFailure.NameMalformed);

            yield break;
        }

        if (!claimedNames.Add(name.Value!))
        {
            yield return new SecretDeclarationError(block.ConfigurationPath, SecretDeclarationFailure.NameDuplicated);
        }
    }

    private static IEnumerable<SecretDeclarationError> FindLifetimeErrors(DiscoveredSecret block)
    {
        if (string.IsNullOrWhiteSpace(block.Secret.Lifetime))
        {
            yield return new SecretDeclarationError(block.ConfigurationPath, SecretDeclarationFailure.LifetimeMissing);
        }
        else if (!SecretLifetime.TryParse(block.Secret.Lifetime, out _))
        {
            yield return new SecretDeclarationError(block.ConfigurationPath, SecretDeclarationFailure.LifetimeMalformed);
        }
    }
}
