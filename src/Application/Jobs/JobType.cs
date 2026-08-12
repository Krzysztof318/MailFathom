// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Application.Jobs;

/// <summary>Names one kind of durable background work, and with it the one payload contract that work is described by.</summary>
/// <remarks>
/// <para>
/// The type is a closed enumeration of values rather than a C# <see langword="enum" />, because the name is the
/// identity: it is the word in a log line, the name of a span, the dimension a counter is broken down by, and the text
/// stored in the job row. An enum member's ordinal would carry no meaning outside this assembly, and a name derived
/// from the member would change with every rename of the work it belongs to.
/// </para>
/// <para>
/// The set is closed because nothing outside this repository enqueues. Every enqueuer is in-tree, so an open string
/// would model a plugin surface that does not exist, would make the type an unbounded metric dimension, and would leave
/// a name read back from the database with no defined meaning. A closed set parses an unknown name as unknown and
/// leaves the row where it is, which is what makes a rolling deployment safe: a job whose type a running version does
/// not know is a fact about the deployment rather than about the work.
/// </para>
/// <para>
/// A type names exactly one payload contract, which is what lets a stored document be read back as the shape it was
/// written as without a discriminator invented for the purpose. Being a struct, <see langword="default" /> is reachable
/// and names no type; <see cref="IsSpecified" /> reports that, and the enqueue and claim contracts refuse it.
/// </para>
/// </remarks>
[JsonConverter(typeof(JobTypeJsonConverter))]
public readonly record struct JobType
{
    private readonly string? name;

    private JobType(string name) => this.name = name;

    /// <summary>Gets the type whose work is deciding whether one stored message occurrence is junk.</summary>
    /// <remarks>Its payload contract is <see cref="EmailOccurrenceJobPayload" />, which names the occurrence and copies nothing out of the message.</remarks>
    public static JobType ClassifyEmailSpam { get; } = new("classify-email-spam");

    /// <summary>Gets every declared job type.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<JobType> All { get; } = [ClassifyEmailSpam];

    /// <summary>Gets whether this value names a declared job type rather than the unusable struct default.</summary>
    public bool IsSpecified => this.name is not null;

    /// <summary>Gets the name a log line, a span, a counter dimension, and the stored row all use for the type.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a job type.</exception>
    public string Name => this.name
        ?? throw new InvalidOperationException("The value is the default of the struct and names no job type.");

    /// <summary>Parses a recorded name back into the job type it names.</summary>
    /// <param name="name">The name read from a log, a stored row, or a serialized document.</param>
    /// <param name="jobType">The parsed type when the name is declared; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the name is a declared job type; otherwise <see langword="false" />.</returns>
    /// <remarks>A name this set does not hold is not accepted, so a type this build does not know is recognized as unknown rather than reconstructed as a value nothing runs.</remarks>
    public static bool TryParseName(string? name, out JobType jobType)
    {
        jobType = default;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var normalizedName = name.Trim();

        // No declared type is the struct default, so an unmatched name yields the unspecified value the caller already
        // receives when parsing fails.
        jobType = All.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Name, normalizedName));

        return jobType.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}

/// <summary>Serializes <see cref="JobType" /> as its name.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so every serializer that meets the
/// value uses it without per-call registration. The JSON form is the name for the same reason the value object exists:
/// it is the identity an operator already reads in a log and a metric, and an ordinal would change meaning silently if
/// the declared set were ever reordered.
/// </remarks>
public sealed class JobTypeJsonConverter : JsonConverter<JobType>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or does not name a declared job type.</exception>
    public override JobType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A job type must be a JSON string, but the token was {reader.TokenType}.");
        }

        return ParseOrThrow(reader.GetString());
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void Write(Utf8JsonWriter writer, JobType value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(NameOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name does not name a declared job type.</exception>
    public override JobType ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => ParseOrThrow(reader.GetString());

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, JobType value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(NameOrThrow(value));
    }

    private static JobType ParseOrThrow(string? name)
    {
        if (!JobType.TryParseName(name, out var jobType))
        {
            throw new JsonException($"'{name}' does not name a declared job type.");
        }

        return jobType;
    }

    private static string NameOrThrow(JobType jobType) => jobType.IsSpecified
        ? jobType.Name
        : throw new JsonException("An unspecified job type cannot be serialized.");
}
