// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Contacts;

/// <summary>One person as a deployment's contact book holds them.</summary>
/// <param name="Id">The identity the book gave them, which every other command names them by.</param>
/// <param name="DisplayName">The name the owner recorded, in the casing they wrote it.</param>
/// <param name="Addresses">Every address they use, the preferred one first.</param>
/// <param name="PreferredAddress">The address to use when something addresses them without naming which of theirs.</param>
/// <param name="Note">What the owner wrote about them, or <see langword="null" /> where they wrote nothing.</param>
/// <param name="Origin">How the contact came to be in the book, which decides who may amend it.</param>
/// <param name="RecordedAt">When the contact entered the book.</param>
/// <param name="AmendedAt">When it was last amended.</param>
/// <remarks>
/// Everything here but the identity and the origin is personal data about a third party. It is printed to the terminal
/// the operator asked from and reaches nothing else: no failure message this command writes carries any of it, and the
/// identifier is what a refusal names.
/// </remarks>
internal sealed record ContactRecord(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("addresses")] IReadOnlyList<string>? Addresses,
    [property: JsonPropertyName("preferredAddress")] string? PreferredAddress,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("origin")] string? Origin,
    [property: JsonPropertyName("recordedAt")] DateTimeOffset RecordedAt,
    [property: JsonPropertyName("amendedAt")] DateTimeOffset AmendedAt);

/// <summary>The person a lookup found, or that the book holds none.</summary>
/// <param name="Contact">The contact, or <see langword="null" /> when the book holds nobody matching what was asked.</param>
/// <remarks>
/// A book holding nobody is an answer rather than a refusal, so the deployment reports it in the body and the command
/// says so plainly instead of reading a missing person as an endpoint that is not there.
/// </remarks>
internal sealed record ContactLookup(
    [property: JsonPropertyName("contact")] ContactRecord? Contact);
