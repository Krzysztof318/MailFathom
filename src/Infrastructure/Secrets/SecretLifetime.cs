// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Globalization;

namespace MailMcp.Infrastructure.Secrets;

/// <summary>How long a configured secret stays usable: without limit, or until an absolute instant.</summary>
/// <remarks>
/// <para>
/// The two states are modelled rather than encoded. A nullable instant would say "no expiration" and "nobody filled
/// this in" with the same value, and a sentinel timestamp far in the future would be a limit an operator never chose
/// and a date some comparison eventually reaches. <see cref="NoLimit" /> is therefore its own value, and it is the value
/// a setting that names no lifetime reads as — which is also why it is the struct default. The type's default and the
/// setting's default agree deliberately, so an unassigned field cannot mean something the configuration never could.
/// </para>
/// <para>
/// A bounded lifetime is an absolute instant, never a duration. A duration would restart at every process start and at
/// every configuration reload, so a credential an operator retired for a week would come back with the next deployment.
/// An instant expires once and stays expired until the configuration that carries it changes.
/// </para>
/// <para>
/// The declaration is uniform across every secret; enforcement is not, because only a consumer knows what a lapsed
/// credential means for the operation it serves. The MCP API keys enforce it by refusing an expired key, which is what
/// makes overlapping keys a rotation rather than an outage. Elsewhere the lifetime is recorded and reported at startup,
/// and the operations documentation says so rather than implying a control that does not exist.
/// </para>
/// </remarks>
public readonly record struct SecretLifetime
{
    /// <summary>The configured value that states a secret carries no expiration.</summary>
    public const string NoLimitValue = "NoLimit";

    private static readonly string[] ExpirationFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
    ];

    private readonly bool bounded;
    private readonly DateTimeOffset expiration;

    private SecretLifetime(bool bounded, DateTimeOffset expiration)
    {
        this.bounded = bounded;
        this.expiration = expiration;
    }

    /// <summary>Gets the lifetime of a secret that never expires, which is what an unconfigured lifetime reads as.</summary>
    public static SecretLifetime NoLimit => default;

    /// <summary>Gets whether the secret expires at an instant rather than lasting indefinitely.</summary>
    public bool IsBounded => this.bounded;

    /// <summary>Gets the instant the secret stops being usable, in UTC.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the lifetime is <see cref="NoLimit" /> and names no instant.</exception>
    public DateTimeOffset Expiration => this.bounded
        ? this.expiration
        : throw new InvalidOperationException("The lifetime carries no limit and therefore names no expiration instant.");

    /// <summary>Creates a lifetime that ends at an absolute instant.</summary>
    /// <param name="expiration">The instant the secret stops being usable. It is stored in UTC whatever offset it arrives with.</param>
    /// <returns>The bounded lifetime.</returns>
    public static SecretLifetime ExpiringAt(DateTimeOffset expiration) => new(bounded: true, expiration.ToUniversalTime());

    /// <summary>Reads a configured lifetime.</summary>
    /// <param name="configuredValue">The bound value: <see cref="NoLimitValue" /> or an ISO 8601 instant carrying an explicit offset.</param>
    /// <param name="lifetime">The parsed lifetime when the value is well formed; otherwise <see cref="NoLimit" />.</param>
    /// <returns><see langword="true" /> when the value is well formed; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// An instant without an explicit offset is refused rather than read in the host's local time, because the same
    /// configuration would then expire at a different moment on every machine that ran it. Absence and an empty value
    /// are refused too: the setting has a default, and a blank one is a mistake rather than a way of spelling it.
    /// </remarks>
    public static bool TryParse(string? configuredValue, out SecretLifetime lifetime)
    {
        lifetime = NoLimit;

        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return false;
        }

        var trimmedValue = configuredValue.Trim();

        if (string.Equals(trimmedValue, NoLimitValue, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // AssumeUniversal covers the two formats whose 'Z' is a literal rather than an offset specifier; without it the
        // platform reads them in the host's local time and the same configuration expires an hour apart on two machines.
        // It changes nothing for the formats that carry an explicit offset, and no format matches a value carrying none.
        if (!DateTimeOffset.TryParseExact(
                trimmedValue,
                ExpirationFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var expiration))
        {
            return false;
        }

        lifetime = ExpiringAt(expiration);

        return true;
    }

    /// <summary>Gets whether the secret has stopped being usable at a given instant.</summary>
    /// <param name="instant">The instant to judge the lifetime at, which callers take from an injected <see cref="TimeProvider" />.</param>
    /// <returns><see langword="true" /> when the lifetime is bounded and that bound has passed; otherwise <see langword="false" />.</returns>
    /// <remarks>The expiration instant itself is already past: a secret configured to expire at midnight is unusable at midnight rather than for one more tick.</remarks>
    public bool HasExpiredAt(DateTimeOffset instant) => this.bounded && instant >= this.expiration;

    /// <summary>Returns the configured spelling of the lifetime, which carries no secret material.</summary>
    /// <returns><see cref="NoLimitValue" />, or the expiration instant in ISO 8601 UTC.</returns>
    public override string ToString() => this.bounded
        ? this.expiration.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        : NoLimitValue;
}
