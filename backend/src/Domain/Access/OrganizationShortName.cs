// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Domain.Access;

/// <summary>The short name an organization is signed in under, in the single form the deployment stores and compares.</summary>
/// <remarks>
/// <para>
/// A member of an organization signs in as <c>SHORTNAME/username</c>, so this is the half of a login that says which
/// company a person belongs to. It is unique across the deployment and folded to upper case on the way in, so the form an
/// operator stored, the form the unique index holds, and the form a request is resolved by are one form decided here.
/// </para>
/// <para>
/// The accepted characters are <c>A</c>–<c>Z</c>, <c>0</c>–<c>9</c>, and <c>-</c>. A <c>/</c> is refused because it is what
/// separates the short name from the username, and a colon because RFC 7617 separates the login from the password with
/// the first one; neither is in the set, so neither needs a rule of its own.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and is not a short name. It reports itself through
/// <see cref="IsSpecified" />, which is also how a login naming no organization is told apart.
/// </para>
/// </remarks>
public readonly record struct OrganizationShortName
{
    /// <summary>The longest short name this deployment accepts, in characters.</summary>
    /// <remarks>Long enough for an abbreviation a person types before every sign-in and short enough that the column and the login it prefixes stay bounded.</remarks>
    public const int MaximumLength = 32;

    private readonly string? value;

    private OrganizationShortName(string value) => this.value = value;

    /// <summary>Gets whether this value names a short name rather than the unusable struct default.</summary>
    public bool IsSpecified => this.value is not null;

    /// <summary>Gets the canonical upper-case form, which is what is stored, indexed, and compared.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a short name.</exception>
    public string Value => this.value
        ?? throw new InvalidOperationException("The value is the default of the struct and names no organization.");

    /// <summary>Reads a written short name into its canonical form.</summary>
    /// <param name="written">The name as a person or an operator typed it, or <see langword="null" /> when none was supplied.</param>
    /// <param name="shortName">The canonical short name when the written form is usable; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the written form is a short name this deployment accepts.</returns>
    public static bool TryCreate(string? written, out OrganizationShortName shortName)
    {
        shortName = default;

        if (written is null)
        {
            return false;
        }

        var canonical = written.Trim().ToUpperInvariant();

        if (canonical.Length == 0 || canonical.Length > MaximumLength || !canonical.All(IsWritable))
        {
            return false;
        }

        shortName = new OrganizationShortName(canonical);

        return true;
    }

    /// <summary>Reads a written short name into its canonical form, refusing anything this deployment does not accept.</summary>
    /// <param name="written">The name as it was stored or typed.</param>
    /// <returns>The canonical short name.</returns>
    /// <exception cref="ArgumentException">Thrown when the written form is not a short name this deployment accepts.</exception>
    public static OrganizationShortName Create(string written) =>
        TryCreate(written, out var shortName)
            ? shortName
            : throw new ArgumentException(DescribeAcceptedForm(), nameof(written));

    /// <summary>Describes what a short name may contain, for a refusal an operator reads.</summary>
    /// <returns>The sentence naming the accepted form.</returns>
    public static string DescribeAcceptedForm() => string.Format(
        CultureInfo.InvariantCulture,
        "An organization's short name is 1 to {0} characters of 'A' to 'Z', '0' to '9', or '-', and is stored upper-cased.",
        MaximumLength);

    /// <inheritdoc />
    public override string ToString() => this.value ?? "(unspecified)";

    private static bool IsWritable(char character) =>
        char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character == '-';
}
