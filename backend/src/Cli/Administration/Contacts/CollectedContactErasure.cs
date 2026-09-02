// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Contacts;

/// <summary>What erasing the collected half of a deployment's book removed.</summary>
/// <param name="ContactsErased">How many contacts the deployment had collected.</param>
/// <param name="AddressesErased">How many addresses went with them.</param>
/// <remarks>
/// Two counts and nobody's identity, for the reason the single erasure beside it carries none. What an owner reversing
/// their mind about collection is told is how much of a record about other people their instance had built, not who was
/// in it.
/// </remarks>
internal sealed record CollectedContactErasure(
    [property: JsonPropertyName("contactsErased")] int ContactsErased,
    [property: JsonPropertyName("addressesErased")] int AddressesErased);
