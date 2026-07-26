// Copyright © 2026 Krzysztof Kasprowicz

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailMcp.Domain.Transport;

/// <summary>Identifies a SASL authentication mechanism a mail transport policy may permit.</summary>
/// <remarks>
/// <para>
/// The type is a closed enumeration of values rather than a C# <see langword="enum" />, because a mechanism is
/// inseparable from its registered SASL name: configuration accepts that name, a transport adapter matches the
/// server's advertised set against it, and the clear-text classification travels with it. Keeping the name in a
/// separate mapping table would let the two drift apart, and a numeric member value would carry no meaning outside
/// this assembly.
/// </para>
/// <para>
/// The set is closed on purpose so a policy can classify a mechanism as clear-text without interpreting
/// server-provided text. OAuth mechanisms are absent until mailbox OAuth authentication is implemented, and GSSAPI is
/// unsupported. Being a struct, <see langword="default" /> is reachable and is not a mechanism; it is rejected where
/// it matters, in <see cref="MailAuthenticationPolicy.Create" />.
/// </para>
/// </remarks>
[JsonConverter(typeof(MailAuthenticationMechanismJsonConverter))]
public readonly record struct MailAuthenticationMechanism
{
    private readonly string? saslName;

    private MailAuthenticationMechanism(string saslName, bool transmitsCredentialsInClearText)
    {
        this.saslName = saslName;
        this.TransmitsCredentialsInClearText = transmitsCredentialsInClearText;
    }

    /// <summary>Gets the mechanism that sends the user name and password in clear text inside the SASL exchange.</summary>
    public static MailAuthenticationMechanism Plain { get; } = new("PLAIN", transmitsCredentialsInClearText: true);

    /// <summary>Gets the mechanism that sends the user name and password in clear text as separate base64 challenges.</summary>
    public static MailAuthenticationMechanism Login { get; } = new("LOGIN", transmitsCredentialsInClearText: true);

    /// <summary>Gets the mechanism that proves the password with an HMAC-MD5 challenge response.</summary>
    public static MailAuthenticationMechanism CramMd5 { get; } = new("CRAM-MD5", transmitsCredentialsInClearText: false);

    /// <summary>Gets the mechanism that proves the password with a digest challenge response.</summary>
    public static MailAuthenticationMechanism DigestMd5 { get; } = new("DIGEST-MD5", transmitsCredentialsInClearText: false);

    /// <summary>Gets the mechanism that proves the password with a salted challenge response over SHA-1.</summary>
    public static MailAuthenticationMechanism ScramSha1 { get; } = new("SCRAM-SHA-1", transmitsCredentialsInClearText: false);

    /// <summary>Gets the SHA-1 salted challenge response bound to the TLS channel.</summary>
    public static MailAuthenticationMechanism ScramSha1Plus { get; } = new("SCRAM-SHA-1-PLUS", transmitsCredentialsInClearText: false);

    /// <summary>Gets the mechanism that proves the password with a salted challenge response over SHA-256.</summary>
    public static MailAuthenticationMechanism ScramSha256 { get; } = new("SCRAM-SHA-256", transmitsCredentialsInClearText: false);

    /// <summary>Gets the SHA-256 salted challenge response bound to the TLS channel.</summary>
    public static MailAuthenticationMechanism ScramSha256Plus { get; } = new("SCRAM-SHA-256-PLUS", transmitsCredentialsInClearText: false);

    /// <summary>Gets the mechanism that proves the password with a salted challenge response over SHA-512.</summary>
    public static MailAuthenticationMechanism ScramSha512 { get; } = new("SCRAM-SHA-512", transmitsCredentialsInClearText: false);

    /// <summary>Gets the SHA-512 salted challenge response bound to the TLS channel.</summary>
    public static MailAuthenticationMechanism ScramSha512Plus { get; } = new("SCRAM-SHA-512-PLUS", transmitsCredentialsInClearText: false);

    /// <summary>Gets the mechanism that proves the password with the NTLM challenge-response exchange.</summary>
    public static MailAuthenticationMechanism Ntlm { get; } = new("NTLM", transmitsCredentialsInClearText: false);

    /// <summary>Gets every supported mechanism.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<MailAuthenticationMechanism> All { get; } =
    [
        Plain,
        Login,
        CramMd5,
        DigestMd5,
        ScramSha1,
        ScramSha1Plus,
        ScramSha256,
        ScramSha256Plus,
        ScramSha512,
        ScramSha512Plus,
        Ntlm,
    ];

    /// <summary>Gets whether this value names a supported mechanism rather than the unusable struct default.</summary>
    public bool IsSpecified => this.saslName is not null;

    /// <summary>Gets whether the mechanism exposes the password to anyone able to read the channel.</summary>
    /// <remarks>
    /// Challenge-response mechanisms still leak the exchange to an attacker who can read the channel, but only a
    /// clear-text mechanism hands over the reusable password itself, which is why the policy singles them out.
    /// </remarks>
    public bool TransmitsCredentialsInClearText { get; }

    /// <summary>Gets the registered SASL name used on the wire and in configuration.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a mechanism.</exception>
    public string SaslName => this.saslName
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a SASL mechanism.");

    /// <summary>Parses an operator-supplied SASL name, ignoring case and surrounding whitespace.</summary>
    /// <param name="saslName">The configured mechanism name.</param>
    /// <param name="mechanism">The parsed mechanism when the name is supported; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the name is a supported mechanism; otherwise <see langword="false" />.</returns>
    public static bool TryParseSaslName(string? saslName, out MailAuthenticationMechanism mechanism)
    {
        mechanism = default;
        if (string.IsNullOrWhiteSpace(saslName))
        {
            return false;
        }

        var normalizedName = saslName.Trim();
        foreach (var candidate in All)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(candidate.SaslName, normalizedName))
            {
                mechanism = candidate;

                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public override string ToString() => this.saslName ?? "(unspecified)";
}

/// <summary>Serializes <see cref="MailAuthenticationMechanism" /> as its registered SASL name.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so every serializer that meets the
/// value uses it without per-call registration. The JSON form is the SASL name for the same reason the value object
/// exists: an ordinal position would silently change meaning if the supported set were ever reordered, while the name
/// is the identity the SASL registry, operator configuration, and the server's advertised set already agree on.
/// </remarks>
public sealed class MailAuthenticationMechanismJsonConverter : JsonConverter<MailAuthenticationMechanism>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or does not name a supported mechanism.</exception>
    public override MailAuthenticationMechanism Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A SASL mechanism must be a JSON string, but the token was {reader.TokenType}.");
        }

        return ParseOrThrow(reader.GetString());
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void Write(
        Utf8JsonWriter writer,
        MailAuthenticationMechanism value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(SaslNameOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name does not name a supported mechanism.</exception>
    public override MailAuthenticationMechanism ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => ParseOrThrow(reader.GetString());

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        MailAuthenticationMechanism value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(SaslNameOrThrow(value));
    }

    private static MailAuthenticationMechanism ParseOrThrow(string? saslName)
    {
        if (!MailAuthenticationMechanism.TryParseSaslName(saslName, out var mechanism))
        {
            throw new JsonException($"'{saslName}' is not a supported SASL mechanism.");
        }

        return mechanism;
    }

    private static string SaslNameOrThrow(MailAuthenticationMechanism mechanism) => mechanism.IsSpecified
        ? mechanism.SaslName
        : throw new JsonException("An unspecified SASL mechanism cannot be serialized.");
}
