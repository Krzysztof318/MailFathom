// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.Contacts;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Publishes what erasing one person removed.</summary>
/// <remarks>
/// The counts are the point rather than a courtesy. Erasure is a data-subject obligation, so whoever asked for one is
/// entitled to an answer saying what was removed instead of a call that returned without complaint. Nothing about the
/// person travels with it: what an erasure reports about somebody is that they are gone.
/// </remarks>
[Description("What the erasure removed. Erasing somebody the book does not hold is a completed erasure rather than a failure.")]
internal sealed record DeleteContactToolResult
{
    /// <summary>Gets the contact the erasure was asked for.</summary>
    [Description("The identifier the erasure was asked for, echoed back so a caller can match the answer to the request.")]
    public required string ContactId { get; init; }

    /// <summary>Gets whether the book held that contact when the erasure ran.</summary>
    [Description("Whether the book held that contact when the erasure ran. False means it already held nobody, which is the state the caller asked for and not a failure.")]
    public required bool WasHeld { get; init; }

    /// <summary>Gets how many addresses went with the contact.</summary>
    [Description("How many of the person's addresses were removed with them. Zero when the book held nobody of that identifier.")]
    public required int AddressesErased { get; init; }

    /// <summary>Publishes what an erasure removed.</summary>
    /// <param name="erasure">The erasure to publish.</param>
    /// <returns>The wire representation of <paramref name="erasure" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="erasure" /> is <see langword="null" />.</exception>
    public static DeleteContactToolResult From(ContactErasure erasure)
    {
        ArgumentNullException.ThrowIfNull(erasure);

        return new DeleteContactToolResult
        {
            ContactId = erasure.ContactId.ToString(),
            WasHeld = erasure.WasHeld,
            AddressesErased = erasure.AddressesErased,
        };
    }
}
