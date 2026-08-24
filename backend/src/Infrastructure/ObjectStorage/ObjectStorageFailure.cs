// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;
using MailFathom.Domain.Failures;

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>Names what ended one operation against the configured object-storage endpoint.</summary>
/// <remarks>
/// <para>
/// The type is a closed enumeration rather than a C# <see langword="enum" /> because a classification here is
/// inseparable from two things it publishes: the <see cref="MailFathomErrorCode" /> a boundary reports it under, and
/// the word a metric carries it as. Both are matched from outside this process — one in an alert, the other in a
/// dashboard query — so neither may follow a rename of a member, and keeping either in a lookup table beside the type
/// would let the two drift apart.
/// </para>
/// <para>
/// The set is closed because the whole point of classifying is to decide: what may be repeated, what a caller is told,
/// and what an operator has to change. <see cref="Unrecognized" /> is the honest end of it rather than an omission — an
/// answer nothing here recognizes is terminal, on the same reasoning every family in
/// <see cref="Resilience.TransientFailureClassifier" /> treats an unrecognized failure as terminal.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and names no classification; <see cref="IsSpecified" />
/// reports that, and <see cref="ObjectStorageUnavailableException" /> rejects it rather than raising a failure with no
/// code.
/// </para>
/// </remarks>
[JsonConverter(typeof(ObjectStorageFailureJsonConverter))]
public readonly record struct ObjectStorageFailure
{
    private readonly string? name;
    private readonly MailFathomErrorCode errorCode;

    private ObjectStorageFailure(string name, MailFathomErrorCode errorCode, bool isWorthRepeating)
    {
        this.name = name;
        this.errorCode = errorCode;
        this.IsWorthRepeating = isWorthRepeating;
    }

    /// <summary>Gets the classification of an operation the caller itself abandoned.</summary>
    /// <remarks>
    /// It is recorded and never raised: a caller's own cancellation reaches that caller as an
    /// <see cref="OperationCanceledException" />, which is what keeps a host shutting down and an endpoint refusing work
    /// from arriving as one failure. What the classification buys is the metric and the log record that say why an
    /// operation ended.
    /// </remarks>
    public static ObjectStorageFailure CallerCancelled { get; } = new(
        "caller_cancelled",
        MailFathomErrorCode.ObjectStorageOperationCancelled,
        isWorthRepeating: false);

    /// <summary>Gets the classification of an operation the host's own shutdown ended.</summary>
    public static ObjectStorageFailure HostShuttingDown { get; } = new(
        "host_shutting_down",
        MailFathomErrorCode.ObjectStorageHostShuttingDown,
        isWorthRepeating: false);

    /// <summary>Gets the classification of an endpoint that did not answer within the budget the operation was given.</summary>
    /// <remarks>
    /// Repeating it is worthwhile because the budget bounds one attempt rather than the endpoint: a request lost on the
    /// way, or one that arrived while the endpoint was briefly saturated, is answered by the attempt after it.
    /// </remarks>
    public static ObjectStorageFailure TimedOut { get; } = new(
        "timed_out",
        MailFathomErrorCode.ObjectStorageOperationTimedOut,
        isWorthRepeating: true);

    /// <summary>Gets the classification of an endpoint that refused the credential the deployment presented.</summary>
    /// <remarks>
    /// Never repeated. The endpoint has decided, every repetition receives the same answer, and against a provider that
    /// counts refused signatures it is the shape that gets an access key disabled.
    /// </remarks>
    public static ObjectStorageFailure AuthenticationFailed { get; } = new(
        "authentication_failed",
        MailFathomErrorCode.ObjectStorageAuthenticationFailed,
        isWorthRepeating: false);

    /// <summary>Gets the classification of an endpoint that could not be reached, or that answered inviting the request again.</summary>
    public static ObjectStorageFailure TransientTransportFailure { get; } = new(
        "transient_transport_failure",
        MailFathomErrorCode.ObjectStorageEndpointUnavailable,
        isWorthRepeating: true);

    /// <summary>Gets the classification of a failure this system does not recognize.</summary>
    public static ObjectStorageFailure Unrecognized { get; } = new(
        "unrecognized",
        MailFathomErrorCode.ObjectStorageOperationFailed,
        isWorthRepeating: false);

    /// <summary>Gets every classification an operation can end in.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<ObjectStorageFailure> All { get; } =
    [
        CallerCancelled,
        HostShuttingDown,
        TimedOut,
        AuthenticationFailed,
        TransientTransportFailure,
        Unrecognized,
    ];

    /// <summary>Gets whether this value names a classification rather than the unusable struct default.</summary>
    public bool IsSpecified => this.name is not null;

    /// <summary>Gets whether an operation that ended this way may be attempted again.</summary>
    /// <remarks>
    /// It is the verdict the resilience pipeline reads through <see cref="Application.Resilience.ITransientFailureClassifier" />,
    /// so the decision is taken once, beside the endpoint's own answer, rather than re-derived from a status the
    /// pipeline never sees.
    /// </remarks>
    public bool IsWorthRepeating { get; }

    /// <summary>Gets the code a boundary reports this classification under.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a classification.</exception>
    public MailFathomErrorCode ErrorCode => this.IsSpecified
        ? this.errorCode
        : throw new InvalidOperationException("The value is the default of the struct and classifies no object-storage failure.");

    /// <summary>Gets the published name a metric, a log record, and a serialized form carry this classification as.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a classification.</exception>
    public string Name => this.name
        ?? throw new InvalidOperationException("The value is the default of the struct and classifies no object-storage failure.");

    /// <summary>Parses a published classification name, ignoring case and surrounding whitespace.</summary>
    /// <param name="name">The name to read.</param>
    /// <param name="failure">The parsed classification when the name is a declared one; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the name is a declared classification; otherwise <see langword="false" />.</returns>
    public static bool TryParse(string? name, out ObjectStorageFailure failure)
    {
        failure = default;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var normalizedName = name.Trim();

        // No declared classification is the struct default, so an unmatched name yields the unspecified value the caller
        // already receives when parsing fails.
        failure = All.FirstOrDefault(candidate => StringComparer.OrdinalIgnoreCase.Equals(candidate.Name, normalizedName));

        return failure.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}

/// <summary>Serializes <see cref="ObjectStorageFailure" /> as its published name.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so every serializer that meets the
/// value uses it without per-call registration. The JSON form is the published name rather than an ordinal for the
/// reason the value object exists at all: the name is what a dashboard and an alert already match on, while a position
/// would change meaning the moment the declared set were reordered.
/// </remarks>
public sealed class ObjectStorageFailureJsonConverter : JsonConverter<ObjectStorageFailure>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or does not name a declared classification.</exception>
    public override ObjectStorageFailure Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"An object-storage failure classification must be a JSON string, but the token was {reader.TokenType}.");
        }

        return ParseOrThrow(reader.GetString());
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void Write(
        Utf8JsonWriter writer,
        ObjectStorageFailure value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(NameOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name does not name a declared classification.</exception>
    public override ObjectStorageFailure ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => ParseOrThrow(reader.GetString());

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        ObjectStorageFailure value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(NameOrThrow(value));
    }

    private static ObjectStorageFailure ParseOrThrow(string? name)
    {
        if (!ObjectStorageFailure.TryParse(name, out var failure))
        {
            throw new JsonException($"'{name}' is not a declared object-storage failure classification.");
        }

        return failure;
    }

    private static string NameOrThrow(ObjectStorageFailure failure) => failure.IsSpecified
        ? failure.Name
        : throw new JsonException("An unspecified object-storage failure classification cannot be serialized.");
}
