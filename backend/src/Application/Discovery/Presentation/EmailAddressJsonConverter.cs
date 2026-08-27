// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Discovery.Presentation;

/// <summary>Serializes <see cref="EmailAddress" /> as the bare address a message wrote, which is what a plan publishes.</summary>
/// <remarks>
/// <para>
/// One string rather than the three members the value holds. The comparison form is an internal key for grouping and
/// indexing and means nothing to a client, and the display name is carried by whichever block holds the address —
/// publishing it twice would let a plan disagree with itself about what somebody is called.
/// </para>
/// <para>
/// Declared beside the contract rather than on the domain type, for the reason
/// <see cref="Citations.StoredEmailIdJsonConverter" /> gives: how an address is published is this boundary's decision
/// rather than one an address should take for every boundary that ever meets it.
/// </para>
/// </remarks>
public sealed class EmailAddressJsonConverter : JsonConverter<EmailAddress>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or does not name a usable address.</exception>
    public override EmailAddress Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"An address must be a JSON string, but the token was {reader.TokenType}.");
        }

        if (!EmailAddress.TryCreate(displayName: null, reader.GetString(), out var address))
        {
            throw new JsonException("The value does not name a usable address.");
        }

        return address;
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        EmailAddress value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value.Address);
    }
}
