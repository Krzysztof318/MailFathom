// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Contacts;

/// <summary>What erasing one contact removed from a deployment.</summary>
/// <param name="Contact">The contact the erasure was asked for.</param>
/// <param name="WasHeld">Whether the book held that contact when the erasure ran.</param>
/// <param name="AddressesErased">How many addresses went with them.</param>
/// <remarks>
/// The counts are what the command reports rather than a bare success, because an owner who has just erased somebody is
/// entitled to be told what went. Nothing about the person is in this answer: an erasure that echoed the record would be
/// a copy of what was just removed, printed to a terminal and left in a shell's scrollback.
/// </remarks>
internal sealed record ContactErasure(
    [property: JsonPropertyName("contact")] Guid Contact,
    [property: JsonPropertyName("wasHeld")] bool WasHeld,
    [property: JsonPropertyName("addressesErased")] int AddressesErased);
