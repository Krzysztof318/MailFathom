// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Host.Configuration.Policies;

/// <summary>Where a settings policy is held, which is also the whole answer to who holds a setting.</summary>
/// <remarks>
/// A closed enumeration rather than a C# <see langword="enum" /> because the name travels: it is what a read of a
/// record answers with as <c>heldBy</c> on the client and the administrative surface alike, and what
/// <c>mfctl user show --effective</c> prints beside a setting. A member rename must not change either. The struct
/// default is reachable, as it is for any struct, and names no scope; <see cref="Name" /> and the converter refuse it.
/// </remarks>
[JsonConverter(typeof(SettingsPolicyScopeJsonConverter))]
internal readonly record struct SettingsPolicyScope
{
    private readonly string? name;

    private SettingsPolicyScope(string name) => this.name = name;

    /// <summary>Gets the deployment's own policy, which governs every record.</summary>
    internal static SettingsPolicyScope Deployment { get; } = new("Deployment");

    /// <summary>Gets the policy of the organization a record belongs to.</summary>
    internal static SettingsPolicyScope Organization { get; } = new("Organization");

    /// <summary>Gets every scope a policy is held at.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs, and widest first.</remarks>
    internal static IReadOnlyList<SettingsPolicyScope> All { get; } = [Deployment, Organization];

    /// <summary>Gets whether this value names a scope rather than the unusable struct default.</summary>
    internal bool IsSpecified => this.name is not null;

    /// <summary>Gets the published name, which is what a read answers with and what an operator reads.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a scope.</exception>
    internal string Name => this.name
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a settings policy scope.");

    /// <summary>Reports the scope by its published name.</summary>
    /// <param name="name">The name to resolve.</param>
    /// <param name="scope">The scope the name publishes, or the struct default when no scope publishes it.</param>
    /// <returns><see langword="true" /> when the name is one this build publishes.</returns>
    internal static bool TryParse(string? name, out SettingsPolicyScope scope)
    {
        scope = All.FirstOrDefault(candidate => string.Equals(candidate.name, name, StringComparison.Ordinal));

        return scope.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}

/// <summary>Serializes <see cref="SettingsPolicyScope" /> as its published name.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so every serializer that meets the
/// value uses it without per-call registration. The record responses map the scope to its name before the value
/// crosses the transport, and the converter is what keeps a serializer that ever met one directly from writing the
/// struct as an empty object.
/// </remarks>
internal sealed class SettingsPolicyScopeJsonConverter : JsonConverter<SettingsPolicyScope>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or does not name a published scope.</exception>
    public override SettingsPolicyScope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A settings policy scope must be a JSON string, but the token was {reader.TokenType}.");
        }

        return ParseOrThrow(reader.GetString());
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void Write(Utf8JsonWriter writer, SettingsPolicyScope value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(NameOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name does not name a published scope.</exception>
    public override SettingsPolicyScope ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => ParseOrThrow(reader.GetString());

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, SettingsPolicyScope value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(NameOrThrow(value));
    }

    private static SettingsPolicyScope ParseOrThrow(string? name)
    {
        if (!SettingsPolicyScope.TryParse(name, out var scope))
        {
            throw new JsonException($"'{name}' is not a settings policy scope this build publishes.");
        }

        return scope;
    }

    private static string NameOrThrow(SettingsPolicyScope value) => value.IsSpecified
        ? value.Name
        : throw new JsonException("The value is the default of the struct and does not name a settings policy scope.");
}
