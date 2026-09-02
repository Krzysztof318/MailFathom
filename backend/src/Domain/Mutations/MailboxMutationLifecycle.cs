// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Domain.Mutations;

/// <summary>Names where in its lifecycle one recorded mutation stands, in the four words an operator asks about.</summary>
/// <remarks>
/// <para>
/// <see cref="MailboxMutationStage" /> says which IMAP command a sequence has reached, which is what a resumed attempt
/// needs and what nobody watching a deployment wants to read. This is the reading beside it: whether a change has been
/// asked for and not started, is on its way, is done, or has stopped and needs a person. Three stages collapse into
/// <see cref="Converging" /> because all three mean the same thing to that question, and none of them collapses into
/// another lifecycle value, so the mapping is total and one-way.
/// </para>
/// <para>
/// The type is a closed enumeration rather than a C# <see langword="enum" />, because the name is the identity: it is
/// the dimension a gauge is broken down by and the word a runbook is written against, and an ordinal would carry no
/// meaning outside this assembly. Nothing persists it — every row stores its stage, and this is derived on the way out —
/// so widening the set is a decision about what an operator is shown rather than a schema change.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and names no lifecycle; <see cref="IsSpecified" /> reports
/// that. Every value reaches a caller through <see cref="Of" />, which is total over the stages, so the default cannot
/// arrive from a record.
/// </para>
/// </remarks>
[JsonConverter(typeof(MailboxMutationLifecycleJsonConverter))]
public readonly record struct MailboxMutationLifecycle
{
    private readonly string? name;

    private MailboxMutationLifecycle(string name) => this.name = name;

    /// <summary>Gets the lifecycle of a change that is durable and that no IMAP command has gone out for.</summary>
    public static MailboxMutationLifecycle Pending { get; } = new("pending");

    /// <summary>Gets the lifecycle of a change whose sequence has started and has not finished.</summary>
    /// <remarks>
    /// It covers an unacknowledged placement as well as an acknowledged one, because both are a mailbox on its way to
    /// the state that was asked for. Which of them a mutation is at is the stage's answer, and an operator reading a
    /// count of changes in flight is not asking it.
    /// </remarks>
    public static MailboxMutationLifecycle Converging { get; } = new("converging");

    /// <summary>Gets the lifecycle of a change the server has made.</summary>
    public static MailboxMutationLifecycle Completed { get; } = new("completed");

    /// <summary>Gets the lifecycle of a change nothing will attempt again, which is waiting for a person.</summary>
    /// <remarks>
    /// This is the value the whole convergence design exists to make reachable. A change that cannot be made has to
    /// become visible instead of staying pending forever, because pending forever looks exactly like success from every
    /// screen an operator reads.
    /// </remarks>
    public static MailboxMutationLifecycle DeadLettered { get; } = new("dead-lettered");

    /// <summary>Gets every lifecycle a mutation can stand in.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<MailboxMutationLifecycle> All { get; } =
        [Pending, Converging, Completed, DeadLettered];

    /// <summary>Gets whether this value names a lifecycle rather than the unusable struct default.</summary>
    public bool IsSpecified => this.name is not null;

    /// <summary>Gets the name a log line and a gauge dimension both use for the lifecycle.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a lifecycle.</exception>
    public string Name => this.name
        ?? throw new InvalidOperationException("The value is the default of the struct and names no mutation lifecycle.");

    /// <summary>Reads which lifecycle one recorded stage stands in.</summary>
    /// <param name="stage">The stage a record carries.</param>
    /// <returns>The lifecycle that stage belongs to.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the stage is not one this build declares.</exception>
    public static MailboxMutationLifecycle Of(MailboxMutationStage stage) => stage switch
    {
        MailboxMutationStage.Recorded => Pending,
        MailboxMutationStage.PlacementIssued
            or MailboxMutationStage.PlacementConfirmed
            or MailboxMutationStage.SourceFlaggedDeleted => Converging,
        MailboxMutationStage.Completed => Completed,
        MailboxMutationStage.Abandoned => DeadLettered,
        _ => throw new ArgumentOutOfRangeException(
            nameof(stage),
            stage,
            "No mutation lifecycle is defined for this stage."),
    };

    /// <summary>Parses a recorded name back into the lifecycle it names.</summary>
    /// <param name="name">The name read from a log, a dashboard query, or a serialized document.</param>
    /// <param name="lifecycle">The parsed lifecycle when the name is one of the four; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the name is a declared lifecycle; otherwise <see langword="false" />.</returns>
    public static bool TryParseName(string? name, out MailboxMutationLifecycle lifecycle)
    {
        lifecycle = default;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var normalizedName = name.Trim();

        // No declared lifecycle is the struct default, so an unmatched name yields the unspecified value the caller
        // already receives when parsing fails.
        lifecycle = All.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Name, normalizedName));

        return lifecycle.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}

/// <summary>Serializes <see cref="MailboxMutationLifecycle" /> as its name.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so every serializer that meets the
/// value uses it without per-call registration. The JSON form is the name for the same reason the value object exists:
/// the name is the published identity, and an ordinal would change meaning silently if the set were ever reordered.
/// </remarks>
public sealed class MailboxMutationLifecycleJsonConverter : JsonConverter<MailboxMutationLifecycle>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or does not name a declared lifecycle.</exception>
    public override MailboxMutationLifecycle Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A mutation lifecycle must be a JSON string, but the token was {reader.TokenType}.");
        }

        return ParseOrThrow(reader.GetString());
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void Write(
        Utf8JsonWriter writer,
        MailboxMutationLifecycle value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(NameOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name does not name a declared lifecycle.</exception>
    public override MailboxMutationLifecycle ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => ParseOrThrow(reader.GetString());

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        MailboxMutationLifecycle value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(NameOrThrow(value));
    }

    private static MailboxMutationLifecycle ParseOrThrow(string? name)
    {
        if (!MailboxMutationLifecycle.TryParseName(name, out var lifecycle))
        {
            throw new JsonException($"'{name}' does not name a mailbox mutation lifecycle.");
        }

        return lifecycle;
    }

    private static string NameOrThrow(MailboxMutationLifecycle lifecycle) => lifecycle.IsSpecified
        ? lifecycle.Name
        : throw new JsonException("An unspecified mutation lifecycle cannot be serialized.");
}
