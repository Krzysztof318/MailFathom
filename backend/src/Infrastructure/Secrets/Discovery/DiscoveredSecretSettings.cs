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
    /// <para>
    /// A name repeated by a declaration identical to the one that claimed it is accepted, because one provider key used
    /// by two settings is one credential and naming it twice says so. What the rule refuses is a name that would mean
    /// two credentials: two declarations agree only when they name the same reference, state the same lifetime, and
    /// name the same bundle password — which this same rule then holds to the same agreement, that password being a
    /// discovered block of its own.
    /// </para>
    /// </remarks>
    public IReadOnlyList<SecretDeclarationError> FindDeclarationErrors()
    {
        var errors = new List<SecretDeclarationError>();
        var claimedNames = new Dictionary<string, ConfiguredSecret>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in this.Blocks)
        {
            errors.AddRange(FindNameErrors(block, claimedNames));
            errors.AddRange(FindLifetimeErrors(block));
        }

        return errors;
    }

    private static IEnumerable<SecretDeclarationError> FindNameErrors(
        DiscoveredSecret block,
        Dictionary<string, ConfiguredSecret> claimedNames)
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

        if (!claimedNames.TryGetValue(name.Value!, out var claimed))
        {
            claimedNames.Add(name.Value!, block.Secret);

            yield break;
        }

        if (FindDisagreement(claimed, block.Secret) is { } failure)
        {
            yield return new SecretDeclarationError(block.ConfigurationPath, failure);
        }
    }

    /// <summary>Reports the first setting two declarations sharing a name disagree about, or nothing when they are one declaration written twice.</summary>
    private static SecretDeclarationFailure? FindDisagreement(ConfiguredSecret claimed, ConfiguredSecret repeated)
    {
        if (!string.Equals(claimed.SecretReference, repeated.SecretReference, StringComparison.Ordinal))
        {
            return SecretDeclarationFailure.NameReusedForAnotherReference;
        }

        if (!StateTheSameLifetime(claimed.Lifetime, repeated.Lifetime))
        {
            return SecretDeclarationFailure.NameReusedForAnotherLifetime;
        }

        return NameTheSamePassword(claimed.Password, repeated.Password)
            ? null
            : SecretDeclarationFailure.NameReusedForAnotherPassword;
    }

    /// <summary>Judges two configured lifetimes by the instant they name rather than by their spelling.</summary>
    /// <remarks>
    /// <c>2027-01-31T00:00:00Z</c> and <c>2027-01-31T01:00:00+01:00</c> are the same instant, so refusing the pair would
    /// make an operator re-date one declaration to match the other — the invented-difference trap this rule removes. A
    /// value neither side can parse is compared as written, because <see cref="FindLifetimeErrors" /> already refuses it
    /// against its own path and nothing is gained by guessing what it meant.
    /// </remarks>
    private static bool StateTheSameLifetime(string claimed, string repeated) =>
        SecretLifetime.TryParse(claimed, out var claimedLifetime) && SecretLifetime.TryParse(repeated, out var repeatedLifetime)
            ? claimedLifetime == repeatedLifetime
            : string.Equals(claimed, repeated, StringComparison.Ordinal);

    /// <summary>Judges two bundle passwords by the name each one claims, which is the whole of what this level decides.</summary>
    /// <remarks>
    /// A nested password is a discovered block in its own right, so two declarations naming one password are already
    /// held to this rule against each other, one level down and against their own configuration path. Comparing the
    /// names here is therefore the whole comparison rather than a shallower one: two that disagree beneath a shared
    /// name are reported where they disagree, and two that name different passwords are two credentials whatever those
    /// passwords say. It is also what keeps this free of the cycle a back-reference between options objects could
    /// otherwise build, which <see cref="ConfiguredSecretDiscovery" /> guards against because nothing forbids one.
    /// </remarks>
    private static bool NameTheSamePassword(ConfiguredSecret? claimed, ConfiguredSecret? repeated) => (claimed, repeated) switch
    {
        (null, null) => true,
        (not null, not null) => string.Equals(claimed.Name, repeated.Name, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

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
