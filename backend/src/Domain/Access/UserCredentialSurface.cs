// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Access;

/// <summary>One endpoint a user's credential may be presented on.</summary>
/// <remarks>
/// <para>
/// A credential carries the surfaces it is good for, so the authority to administer the deployment is never acquired by
/// a credential provisioned to read mail: the administrative endpoint is one of them only where somebody wrote it.
/// <see cref="Default" /> is what a credential states when nobody wrote anything, and it holds the two mail-serving
/// endpoints alone.
/// </para>
/// <para>
/// A closed enumeration rather than a C# <see langword="enum" /> for the reason <see cref="UserCredentialMethod" /> is
/// one: the name is written by an operator on a command line, published in a credential listing, and recorded in a
/// column, and none of those readers has a member's ordinal to hand. A name is allocated once and never renamed.
/// </para>
/// <para>
/// The user's own endpoint switches still decide whether a person is served on the MCP and client endpoints at all; a
/// credential is admitted on a surface only where both allow it. On the administrative endpoint the switch is the user's
/// administrative grant instead.
/// </para>
/// </remarks>
public readonly record struct UserCredentialSurface
{
    private readonly string? name;

    private UserCredentialSurface(string name) => this.name = name;

    /// <summary>Gets the endpoint an agent reads a user's mail through over the MCP protocol.</summary>
    public static UserCredentialSurface Mcp { get; } = new("mcp");

    /// <summary>Gets the endpoint the MailFathom client reads and writes a user's mail through.</summary>
    public static UserCredentialSurface Client { get; } = new("client");

    /// <summary>Gets the endpoint the deployment is administered through.</summary>
    /// <remarks>Never part of <see cref="Default" />: a credential reaches it only where it was written.</remarks>
    public static UserCredentialSurface Administration { get; } = new("admin");

    /// <summary>Gets every surface this repository publishes.</summary>
    /// <remarks>Declared after the members so they are initialized when this initializer runs.</remarks>
    public static IReadOnlyList<UserCredentialSurface> All { get; } = [Mcp, Client, Administration];

    /// <summary>Gets the surfaces a credential states when nothing was written for it.</summary>
    public static IReadOnlyList<UserCredentialSurface> Default { get; } = [Mcp, Client];

    /// <summary>Gets the published name, which is what an operator writes and a row records.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the unspecified struct default.</exception>
    public string Name => this.name
        ?? throw new InvalidOperationException("The unspecified credential surface has no published name.");

    /// <summary>Gets whether this value names a published surface rather than the unusable struct default.</summary>
    public bool IsSpecified => this.name is not null;

    /// <summary>Parses a name written outside this process.</summary>
    /// <param name="name">The written name.</param>
    /// <param name="surface">The surface the name declares, or the unspecified default when it declares none.</param>
    /// <returns><see langword="true" /> when the name is one this repository publishes; otherwise <see langword="false" />.</returns>
    /// <remarks>Ordinal and case-insensitive, because the name is typed by hand on a command line.</remarks>
    public static bool TryParse(string? name, out UserCredentialSurface surface)
    {
        surface = All.FirstOrDefault(candidate => string.Equals(candidate.name, name, StringComparison.OrdinalIgnoreCase));

        return surface.IsSpecified;
    }

    /// <summary>Reads the surfaces a stored or written list names, in published order and without repetition.</summary>
    /// <param name="names">The written names.</param>
    /// <returns>The surfaces the names declare; a name nothing publishes declares none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="names" /> is <see langword="null" />.</exception>
    public static IReadOnlyList<UserCredentialSurface> ParseKnown(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var named = names.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return [.. All.Where(surface => named.Contains(surface.Name))];
    }

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}
