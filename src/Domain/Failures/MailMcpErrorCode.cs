// Copyright © 2026 Krzysztof Kasprowicz

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailMcp.Domain.Failures;

/// <summary>Identifies a failure MailMcp raised deliberately, as a five-digit code stable enough to publish.</summary>
/// <remarks>
/// <para>
/// The type is a closed enumeration of values rather than a C# <see langword="enum" />, because the number is the
/// identity: it is what a log records, what an alert matches, and what a support conversation names. An enum member's
/// ordinal would carry no meaning outside this assembly, and its name would change with every rename of the failure it
/// belongs to.
/// </para>
/// <para>
/// The code reads as <c>C S NNN</c>: the first digit is the <see cref="Category" />, the second is the
/// <see cref="Subcategory" /> within it, and the last three number the failure inside that subcategory. A reader who
/// sees <c>21001</c> knows it is a mail-protocol failure about authentication before looking anything up.
/// </para>
/// <para>
/// Numbers are allocated once and never reused or renumbered, for the same reason an enum member's value is never
/// reordered: a code that changes meaning silently invalidates every runbook, alert, and log search written against it.
/// Being a struct, <see langword="default" /> is reachable and names no failure; <see cref="IsSpecified" /> reports
/// that, and every failure reaches its code through a declared member, so the default cannot arrive from a raised
/// exception.
/// </para>
/// <para>
/// The cost against an enum is that the members are not compile-time constants, so a boundary translates them through a
/// lookup rather than through a <see langword="switch" /> over constants.
/// </para>
/// </remarks>
[JsonConverter(typeof(MailMcpErrorCodeJsonConverter))]
public readonly record struct MailMcpErrorCode
{
    private MailMcpErrorCode(int value) => this.Value = value;

    #region Category 1 — Configuration and transport security

    /// <summary>Gets subcategory 1, transport security policy: a configured combination would weaken protection in a way no opt-in allows.</summary>
    public static MailMcpErrorCode MailTransportSecurityPolicyViolated { get; } = new(11001);

    #endregion

    #region Category 2 — Mail protocol

    /// <summary>Gets subcategory 1, authentication: a mail server advertises no authentication mechanism the account's policy permits.</summary>
    public static MailMcpErrorCode MailAuthenticationMechanismUnavailable { get; } = new(21001);

    /// <summary>Gets subcategory 2, session availability: a mail server did not serve an operation within the resilience budget configured for it.</summary>
    public static MailMcpErrorCode MailboxUnavailable { get; } = new(22001);

    /// <summary>Gets subcategory 3, folder identity: a folder was reselected with a UIDVALIDITY that makes the session's identities name different emails.</summary>
    public static MailMcpErrorCode MailboxFolderRecreated { get; } = new(23001);

    #endregion

    #region Category 3 — Persistence

    /// <summary>Gets subcategory 1, concurrent writes: a local write did not commit because another writer changed the same durable state.</summary>
    public static MailMcpErrorCode PersistenceConcurrencyConflict { get; } = new(31001);

    #endregion

    #region Category 4 — Outbound resilience

    /// <summary>Gets subcategory 1, pipeline rejection: a resilience pipeline declined to serve an operation against an outbound dependency any further.</summary>
    public static MailMcpErrorCode OutboundDependencyUnavailable { get; } = new(41001);

    #endregion

    /// <summary>Gets every allocated code.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<MailMcpErrorCode> All { get; } =
    [
        MailTransportSecurityPolicyViolated,
        MailAuthenticationMechanismUnavailable,
        MailboxUnavailable,
        MailboxFolderRecreated,
        PersistenceConcurrencyConflict,
        OutboundDependencyUnavailable,
    ];

    /// <summary>Gets the five-digit code.</summary>
    public int Value { get; }

    /// <summary>Gets whether this value names an allocated code rather than the unusable struct default.</summary>
    public bool IsSpecified => this.Value is not 0;

    /// <summary>Gets the subsystem the failure belongs to, which is the code's first digit.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than an allocated code.</exception>
    public int Category => this.IsSpecified
        ? this.Value / 10000
        : throw new InvalidOperationException("The value is the default of the struct and belongs to no category.");

    /// <summary>Gets the concern within the category, which is the code's second digit.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than an allocated code.</exception>
    public int Subcategory => this.IsSpecified
        ? this.Value / 1000 % 10
        : throw new InvalidOperationException("The value is the default of the struct and belongs to no subcategory.");

    /// <summary>Parses a recorded five-digit code back into the value it names.</summary>
    /// <param name="value">The number read from a log, an alert, or a serialized error.</param>
    /// <param name="errorCode">The parsed code when the number is allocated; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the number is an allocated code; otherwise <see langword="false" />.</returns>
    /// <remarks>An unallocated number is not accepted, so a code retired or mistyped is recognized as unknown rather than reconstructed as a value nothing raises.</remarks>
    public static bool TryParse(int value, out MailMcpErrorCode errorCode)
    {
        // No allocated code is the struct default, so an unmatched number yields the unspecified value the caller
        // already receives when parsing fails.
        errorCode = All.FirstOrDefault(candidate => candidate.Value == value);

        return errorCode.IsSpecified;
    }

    /// <summary>Returns the five-digit code, so a log or an error response records the number rather than the structure.</summary>
    /// <returns>The code formatted as five digits, or a marker when the value is the struct default.</returns>
    public override string ToString() => this.IsSpecified
        ? this.Value.ToString("D5", CultureInfo.InvariantCulture)
        : "(unspecified)";
}

/// <summary>Serializes <see cref="MailMcpErrorCode" /> as its five-digit number.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so every serializer that meets the
/// value uses it without per-call registration. The JSON form is the number for the same reason the value object
/// exists: the number is the published identity, and a member name would change with a rename that the code is meant
/// to survive.
/// </remarks>
public sealed class MailMcpErrorCodeJsonConverter : JsonConverter<MailMcpErrorCode>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a number or does not name an allocated code.</exception>
    public override MailMcpErrorCode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw new JsonException($"An error code must be a JSON number, but the token was {reader.TokenType}.");
        }

        return ParseOrThrow(reader.GetInt32());
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void Write(
        Utf8JsonWriter writer,
        MailMcpErrorCode value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteNumberValue(SpecifiedValueOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name is not a number or does not name an allocated code.</exception>
    public override MailMcpErrorCode ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var propertyName = reader.GetString();

        return int.TryParse(propertyName, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? ParseOrThrow(value)
            : throw new JsonException($"'{propertyName}' is not a five-digit error code.");
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        MailMcpErrorCode value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(SpecifiedValueOrThrow(value).ToString("D5", CultureInfo.InvariantCulture));
    }

    private static MailMcpErrorCode ParseOrThrow(int value)
    {
        if (!MailMcpErrorCode.TryParse(value, out var errorCode))
        {
            throw new JsonException($"'{value}' is not an allocated MailMcp error code.");
        }

        return errorCode;
    }

    private static int SpecifiedValueOrThrow(MailMcpErrorCode errorCode) => errorCode.IsSpecified
        ? errorCode.Value
        : throw new JsonException("An unspecified error code cannot be serialized.");
}
