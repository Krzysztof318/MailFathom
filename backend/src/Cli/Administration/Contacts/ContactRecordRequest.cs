// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Contacts;

/// <summary>The record the command asks a deployment to hold for one person.</summary>
/// <param name="DisplayName">The name to record.</param>
/// <param name="Addresses">Every address the person uses.</param>
/// <param name="PreferredAddress">The address to use by default, which is one of <paramref name="Addresses" />.</param>
/// <param name="Note">What the owner wrote about the person, or <see langword="null" /> to hold no note.</param>
/// <remarks>
/// One shape for recording a person and for correcting one, because an amendment states the whole record rather than
/// the difference from the one held. Which of the two is meant is the route and the verb, never a field.
/// </remarks>
internal sealed record ContactRecordRequest(
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("addresses")] IReadOnlyList<string> Addresses,
    [property: JsonPropertyName("preferredAddress")] string PreferredAddress,
    [property: JsonPropertyName("note")] string? Note);
