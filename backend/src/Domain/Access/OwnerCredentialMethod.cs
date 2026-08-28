// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Domain.Access;

/// <summary>How one owner-facing credential is presented, and therefore what a request is resolved to an owner by.</summary>
/// <remarks>
/// <para>
/// The type is a closed enumeration of values rather than a C# <see langword="enum" />, because the name is the
/// identity and it travels outside this process: an administrator writes it as an argument to <c>mfctl</c>, the
/// administrative surface publishes it in a credential listing, an endpoint section names it to say which methods it
/// accepts, and a row records it in a column that outlives every rename this assembly may take. A member's ordinal
/// would mean nothing to any of the four.
/// </para>
/// <para>
/// The set is closed because a credential MailFathom cannot resolve to an owner is not a credential it may admit. A
/// name nothing declares here is unknown rather than new, which is what lets an endpoint section refuse a misspelled
/// method at startup and a stored row refuse to materialize as a method the process cannot judge.
/// </para>
/// <para>
/// The identity is lower-case and hyphenated, so it is one word an operator can type and one value a URL segment or a
/// command-line argument carries unquoted. A member is allocated once and never renamed: a name written into a row is
/// the row's meaning rather than a label over it.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and is not a method. It reports itself through
/// <see cref="IsSpecified" />, and it is rejected where the invariant matters: the JSON converter below, the persisted
/// credential's own conversion, and every store operation that names a method.
/// </para>
/// </remarks>
[JsonConverter(typeof(OwnerCredentialMethodJsonConverter))]
public readonly record struct OwnerCredentialMethod
{
    private readonly string? name;

    private OwnerCredentialMethod(
        string name,
        bool storesMaterial,
        bool materialIsReplaceable,
        bool lookupIsDerivedFromTheSecret,
        bool lookupMovesWithTheMaterial)
    {
        this.name = name;
        this.StoresMaterial = storesMaterial;
        this.MaterialIsReplaceable = materialIsReplaceable;
        this.LookupIsDerivedFromTheSecret = lookupIsDerivedFromTheSecret;
        this.LookupMovesWithTheMaterial = lookupMovesWithTheMaterial;
    }

    /// <summary>Gets the method by which an owner presents a username and a password over HTTP Basic.</summary>
    /// <remarks>The lookup is the canonical username, and the stored material is the password's own record — never the password.</remarks>
    public static OwnerCredentialMethod Password { get; } =
        new("password", storesMaterial: true, materialIsReplaceable: true, lookupIsDerivedFromTheSecret: false, lookupMovesWithTheMaterial: false);

    /// <summary>Gets the method by which a client presents an opaque key this deployment minted.</summary>
    /// <remarks>
    /// The lookup is the key's digest and there is no stored material, because the digest is the whole of what a
    /// deployment needs to recognize the key again. The key itself exists while it is minted and while it is presented,
    /// and nowhere else.
    /// </remarks>
    public static OwnerCredentialMethod ApiKey { get; } =
        new("api-key", storesMaterial: false, materialIsReplaceable: true, lookupIsDerivedFromTheSecret: true, lookupMovesWithTheMaterial: true);

    /// <summary>Gets the method by which a client presents an assertion signed by a key pair it holds.</summary>
    /// <remarks>The lookup is the fingerprint of the client's public key, which the assertion names in its own <c>kid</c> header, and the stored material is that public key. Nothing secret is stored, which is the point of the method.</remarks>
    public static OwnerCredentialMethod PublicKey { get; } =
        new("public-key", storesMaterial: true, materialIsReplaceable: true, lookupIsDerivedFromTheSecret: false, lookupMovesWithTheMaterial: true);

    /// <summary>Gets the method by which an authorization server's validated subject names the owner it stands for.</summary>
    /// <remarks>
    /// The lookup is the issuer and the subject together, and there is no material at all: the credential is a token
    /// this deployment did not issue and cannot hold, so what the record adds is the one thing the token cannot carry —
    /// which owner the subject acts for.
    /// </remarks>
    public static OwnerCredentialMethod OAuthSubject { get; } =
        new("oauth-subject", storesMaterial: false, materialIsReplaceable: false, lookupIsDerivedFromTheSecret: false, lookupMovesWithTheMaterial: false);

    /// <summary>Gets every method this repository publishes.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<OwnerCredentialMethod> All { get; } = [Password, ApiKey, PublicKey, OAuthSubject];

    /// <summary>Gets the published name of the method, which is what an operator writes and a row records.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the unspecified struct default.</exception>
    public string Name => this.name
        ?? throw new InvalidOperationException("The unspecified credential method has no published name.");

    /// <summary>Gets whether a credential of this method carries material beside the value it is resolved by.</summary>
    /// <remarks>A password's own record and a client's public key are material; a key digest and a validated subject are the lookup itself, so there is nothing else to keep.</remarks>
    public bool StoresMaterial { get; }

    /// <summary>Gets whether what a credential is presented as can be replaced without provisioning a second one.</summary>
    /// <remarks>
    /// Three of the four can: a password is retyped, a key is minted again, and a client sends a new public key, each
    /// of which leaves the credential's identifier, its owner, and its grant exactly where they were. A validated
    /// subject cannot, because there is nothing about it this deployment issued — pointing an owner at a different
    /// subject is a different credential rather than a new secret for this one.
    /// </remarks>
    public bool MaterialIsReplaceable { get; }

    /// <summary>Gets whether the value a credential of this method is resolved by is derived from the secret it resolves.</summary>
    /// <remarks>
    /// True of a key alone, whose lookup is the key's own digest. Everything a listing publishes is a fact about the
    /// record rather than about the secret, so this is what keeps the digest out of one: it verifies a presented key,
    /// and a verifier served to whoever may read a listing is material this deployment gave away. The other three
    /// publish theirs — a username is what an owner types, a fingerprint is what the client's assertions must name, and
    /// a subject is what an administrator wrote.
    /// </remarks>
    public bool LookupIsDerivedFromTheSecret { get; }

    /// <summary>Gets whether replacing a credential's material also replaces the value it is resolved by.</summary>
    /// <remarks>
    /// A separate question from <see cref="LookupIsDerivedFromTheSecret" />, and the two answer differently for a
    /// client's key pair: its fingerprint is published rather than derived from anything secret, and it still moves,
    /// because a client that sends a new public key is resolved by that key's fingerprint from then on. A key this
    /// deployment minted moves for the same reason. A username and a validated subject do not — replacing a password
    /// leaves the username where it was, and a subject has no material to replace at all.
    /// <para>
    /// What reads it is the store's rotation predicate. Where the lookup moves, the stated value is the new one and the
    /// row is matched by identity alone; where it does not, the stated value is the one the row must already carry, so
    /// a mistyped username matches no row instead of renaming somebody's sign-in.
    /// </para>
    /// </remarks>
    public bool LookupMovesWithTheMaterial { get; }

    /// <summary>Gets whether this value names a published method rather than the unusable struct default.</summary>
    public bool IsSpecified => this.name is not null;

    /// <summary>Parses a name written outside this process.</summary>
    /// <param name="name">The written name, in the published lower-case hyphenated form.</param>
    /// <param name="method">The method the name declares, or the unspecified default when it declares none.</param>
    /// <returns><see langword="true" /> when the name is one this repository publishes; otherwise <see langword="false" />.</returns>
    /// <remarks>The comparison is ordinal and case-insensitive, because the name is written by hand in a configuration file and on a command line, and no two published names differ by case alone.</remarks>
    public static bool TryParse(string? name, out OwnerCredentialMethod method)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(candidate.name, name, StringComparison.OrdinalIgnoreCase))
            {
                method = candidate;

                return true;
            }
        }

        method = default;

        return false;
    }

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}

/// <summary>Reads and writes a credential method as its published name.</summary>
/// <remarks>The name rather than an ordinal, for the reason the type gives: what is serialized is read by an operator and matched by a client, and neither has the assembly to hand.</remarks>
public sealed class OwnerCredentialMethodJsonConverter : JsonConverter<OwnerCredentialMethod>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or does not name a published method.</exception>
    public override OwnerCredentialMethod Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A credential method must be a JSON string, but the token was {reader.TokenType}.");
        }

        return ParseOrThrow(reader.GetString());
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void Write(Utf8JsonWriter writer, OwnerCredentialMethod value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(NameOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name does not name a published method.</exception>
    public override OwnerCredentialMethod ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => ParseOrThrow(reader.GetString());

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        OwnerCredentialMethod value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(NameOrThrow(value));
    }

    private static OwnerCredentialMethod ParseOrThrow(string? name)
    {
        if (!OwnerCredentialMethod.TryParse(name, out var method))
        {
            throw new JsonException($"'{name}' is not a credential method MailFathom publishes.");
        }

        return method;
    }

    private static string NameOrThrow(OwnerCredentialMethod method) => method.IsSpecified
        ? method.Name
        : throw new JsonException("An unspecified credential method cannot be serialized.");
}
