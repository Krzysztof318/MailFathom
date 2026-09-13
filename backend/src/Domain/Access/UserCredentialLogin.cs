// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Access;

/// <summary>What a person types to sign in with a password: a username, prefixed by their organization's short name when they belong to one.</summary>
/// <remarks>
/// <para>
/// A member of an organization signs in as <c>SHORTNAME/username</c> and somebody in no organization as <c>username</c>,
/// so two companies can each have a <c>jan</c>. The login is split at its first <c>/</c>, and each half keeps the fold its
/// own type owns — the short name upper-cased, the username lowercased — so the canonical form is decided once whichever
/// spelling a keyboard produced.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and is not a login; it reports itself through
/// <see cref="IsSpecified" />.
/// </para>
/// </remarks>
public readonly record struct UserCredentialLogin
{
    /// <summary>What separates an organization's short name from the username in a login.</summary>
    public const char OrganizationSeparator = '/';

    private UserCredentialLogin(OrganizationShortName organization, UserCredentialUsername username)
    {
        this.Organization = organization;
        this.Username = username;
    }

    /// <summary>Gets whether this value names a login rather than the unusable struct default.</summary>
    public bool IsSpecified => this.Username.IsSpecified;

    /// <summary>Gets the organization the login is scoped to, or the unspecified default for somebody in no organization.</summary>
    public OrganizationShortName Organization { get; }

    /// <summary>Gets the username half of the login.</summary>
    public UserCredentialUsername Username { get; }

    /// <summary>Gets the canonical form, which is what the attempt limiter partitions on and what an operator is shown.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a login.</exception>
    public string Value => Compose(this.Organization, this.Username);

    /// <summary>Reads a typed login into its two canonical halves.</summary>
    /// <param name="written">The login as the credential carried it, or <see langword="null" /> when none was supplied.</param>
    /// <param name="login">The login when the written form is usable; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when both halves are ones this deployment accepts.</returns>
    public static bool TryRead(string? written, out UserCredentialLogin login)
    {
        login = default;

        if (written is null)
        {
            return false;
        }

        var separator = written.IndexOf(OrganizationSeparator, StringComparison.Ordinal);

        if (separator < 0)
        {
            if (!UserCredentialUsername.TryCreate(written, out var unscoped))
            {
                return false;
            }

            login = new UserCredentialLogin(default, unscoped);

            return true;
        }

        if (!OrganizationShortName.TryCreate(written[..separator], out var organization)
            || !UserCredentialUsername.TryCreate(written[(separator + 1)..], out var username))
        {
            return false;
        }

        login = new UserCredentialLogin(organization, username);

        return true;
    }

    /// <summary>Composes the login a username is typed as within an organization, or without one.</summary>
    /// <param name="organization">The organization's short name, or the unspecified default for somebody in no organization.</param>
    /// <param name="username">The canonical username.</param>
    /// <returns>The login in the form it is typed.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="username" /> is the unspecified struct default.</exception>
    public static string Compose(OrganizationShortName organization, UserCredentialUsername username) =>
        organization.IsSpecified
            ? $"{organization.Value}{OrganizationSeparator}{username.Value}"
            : username.Value;

    /// <inheritdoc />
    public override string ToString() => this.IsSpecified ? this.Value : "(unspecified)";
}
