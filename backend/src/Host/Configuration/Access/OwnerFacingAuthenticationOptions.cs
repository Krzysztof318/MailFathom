// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.Access;

/// <summary>One method an owner-facing endpoint accepts, and the conditions it requires of it.</summary>
/// <remarks>
/// <para>
/// An entry here states a method and nothing that could authenticate anybody. That is the whole difference between the
/// two mail-serving surfaces and the administrative one: a credential that reaches somebody's mail names the owner
/// whose mail it reaches, and an owner is a record in this deployment's database rather than a value in an operator's
/// file — so the deployment states which methods it accepts and the database states who may use them. An entry
/// therefore carries no key, no public key, no subject, and no grant, and there is nothing in an endpoint section for a
/// backup, a deployment tool, or a support transcript to leak.
/// </para>
/// <para>
/// The method is named rather than selected by the presence of its block, which is the second difference. Two of the
/// four methods require nothing of the operator at all, and a block stating nothing is not something configuration can
/// express: an empty JSON object binds to no instance and reaches no section, so an entry written that way would state
/// no method while looking like it stated one. Naming it is also what lets a method's own block be optional — an entry
/// naming Basic and writing no block takes the bound's default rather than having to write it out to exist.
/// </para>
/// <para>
/// A method appears at most once. A second entry naming one would be a second grant, a second bound, or a second set of
/// authorization servers for one credential vocabulary, and which of them applied would come down to configuration
/// order — where the configured surface repeats an entry per credential, this repeats nothing, because the credentials
/// are rows. The one exception is OAuth, which may be written several times: each entry carries its own authorization
/// servers and its own required scopes, and a token is judged by the entry that trusts its issuer.
/// </para>
/// <para>
/// The published names are the ones <see cref="OwnerCredentialMethod" /> publishes, which are the same names
/// <c>mfctl</c> takes when a credential is provisioned. One vocabulary, so an operator who turned a method on writes
/// the same word to give somebody a credential for it.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class OwnerFacingAuthenticationOptions
{
    /// <summary>Gets or sets which method this entry accepts, as one of the published method names.</summary>
    /// <remarks><see langword="null" /> when the entry states none, which is refused: an entry that names no method is an entry an operator meant to write something in.</remarks>
    public string? Method { get; set; }

    /// <summary>Gets or sets how often an owner's password may be tried, where this entry accepts one.</summary>
    /// <remarks>
    /// Optional even on the entry that accepts passwords, because the bound has a default this repository decided and
    /// most deployments have no reason to move it. It is refused on an entry naming any other method, since none of the
    /// other three is guessable and a bound written there would be a setting that does nothing.
    /// </remarks>
    public BasicAuthenticationOptions? Basic { get; set; }

    /// <summary>Gets or sets what this deployment is called in OAuth terms and which authorization servers may speak for it.</summary>
    /// <remarks>
    /// Required on an entry naming OAuth and refused on every other, because it is the one method whose acceptance the
    /// deployment genuinely configures: which servers are trusted, what this resource is called, and which scopes a
    /// token must carry are decisions about the deployment rather than about a person. What it no longer carries is
    /// which subjects are served — that is one credential record per person, which is what makes a token resolve an
    /// owner instead of admitting whoever the server signed in.
    /// </remarks>
    public OAuthValidationOptions? OAuth { get; set; }

    /// <summary>Gets or sets whether a token's own scopes narrow the grant its credential record carries.</summary>
    /// <remarks>
    /// With it, a token holds the published names its scopes carry <em>and</em> the record grants, so the authorization
    /// server decides per subject within a bound the deployment provisioned. Available only to an entry naming OAuth,
    /// because none of the other three credentials can carry a claim about what it may do — a key, a public key, and an
    /// owner's password are each judged against something this deployment holds — so startup refuses it elsewhere
    /// rather than asking a credential a question it cannot answer.
    /// </remarks>
    public bool PermissionsFromTokenScopes { get; set; }

    /// <summary>Gets the configuration key this entry was written under, or <see langword="null" /> where no read established one.</summary>
    /// <remarks>A source may number its entries with a gap, so the key an operator wrote and the position the binder appended the entry at are different numbers. A refusal names this one wherever it is known, because a path nobody wrote is a path nobody can go and correct.</remarks>
    internal string? ConfigurationKey { get; private set; }

    /// <summary>Gets the method this entry accepts, or the unspecified default where it names none this repository publishes.</summary>
    public OwnerCredentialMethod AcceptedMethod =>
        OwnerCredentialMethod.TryParse(this.Method, out var method) ? method : default;

    /// <summary>Records the configuration key this entry was written under, which every refusal against it is named by.</summary>
    /// <param name="key">The key of the configuration child the entry was bound from.</param>
    internal void RecordConfigurationKey(string key) => this.ConfigurationKey = key;

    /// <summary>Finds everything an operator must fix before this entry can guard an endpoint.</summary>
    /// <param name="settingPath">The configuration path this entry was bound from, which every message is written against.</param>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the settings are usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settingPath" /> is <see langword="null" />.</exception>
    public IReadOnlyList<string> FindConfigurationErrors(string settingPath)
    {
        ArgumentNullException.ThrowIfNull(settingPath);

        if (!this.AcceptedMethod.IsSpecified)
        {
            return
            [
                $"{settingPath}:{nameof(this.Method)} — {Describe(this.Method)} names no method this endpoint accepts; "
                + $"write one of {PublishedMethodNames()}. The credentials themselves are provisioned through the "
                + "administrative endpoint, so an entry states only which method is accepted.",
            ];
        }

        return [.. this.FindBlockErrors(settingPath), .. this.FindGrantErrors(settingPath)];
    }

    private IEnumerable<string> FindBlockErrors(string settingPath)
    {
        var method = this.AcceptedMethod;

        if (this.Basic is { } basic)
        {
            if (method != OwnerCredentialMethod.Password)
            {
                yield return $"{settingPath}:{nameof(this.Basic)} — this entry accepts '{method.Name}', and the block bounds how often a password may be tried. Move it to an entry naming '{OwnerCredentialMethod.Password.Name}', or remove it.";
            }
            else
            {
                foreach (var error in basic.FindConfigurationErrors($"{settingPath}:{nameof(this.Basic)}"))
                {
                    yield return error;
                }
            }
        }

        if (method == OwnerCredentialMethod.OAuthSubject)
        {
            if (this.OAuth is not { } oauth)
            {
                yield return $"{settingPath}:{nameof(this.OAuth)} — this entry accepts '{method.Name}' and states nothing about which authorization servers may speak for this deployment. Write the block naming the resource and its servers.";
            }
            else
            {
                foreach (var error in oauth.FindConfigurationErrors(OAuthSubjectAdmission.ResolvedOwnerCredentials))
                {
                    yield return $"{settingPath}:{nameof(this.OAuth)}:{error}";
                }
            }
        }
        else if (this.OAuth is not null)
        {
            yield return $"{settingPath}:{nameof(this.OAuth)} — this entry accepts '{method.Name}', which no authorization server is involved in. Move the block to an entry naming '{OwnerCredentialMethod.OAuthSubject.Name}', or remove it.";
        }
    }

    private IEnumerable<string> FindGrantErrors(string settingPath)
    {
        if (this.PermissionsFromTokenScopes && this.AcceptedMethod != OwnerCredentialMethod.OAuthSubject)
        {
            yield return $"{settingPath}:{nameof(this.PermissionsFromTokenScopes)} — a token's scopes can narrow a grant and a '{this.AcceptedMethod.Name}' credential carries none, so this entry cannot be read both ways. Write it on an entry naming '{OwnerCredentialMethod.OAuthSubject.Name}', or remove it.";
        }
    }

    private static string Describe(string? written) =>
        string.IsNullOrWhiteSpace(written) ? "an entry with no method" : $"'{written}'";

    private static string PublishedMethodNames() =>
        string.Join(", ", OwnerCredentialMethod.All.Select(method => $"'{method.Name}'"));
}
