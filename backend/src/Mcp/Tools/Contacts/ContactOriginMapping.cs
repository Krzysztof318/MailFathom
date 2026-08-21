// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;

namespace MailFathom.Mcp.Tools.Contacts;

/// <summary>Converts between the origin the book records and the one this surface publishes.</summary>
/// <remarks>
/// Both directions live together because they are one agreement about two spellings of one fact, and separating them is
/// how the two come to disagree. Each refuses a value naming nothing rather than defaulting, so an origin added to
/// either side surfaces as a failure instead of arriving on the wire as the wrong one.
/// </remarks>
internal static class ContactOriginMapping
{
    /// <summary>Publishes the origin a contact carries.</summary>
    /// <param name="origin">The origin the book recorded.</param>
    /// <returns>The value this surface publishes it as.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value names no origin the book records.</exception>
    public static PublishedContactOrigin Published(ContactOrigin origin) => origin switch
    {
        ContactOrigin.Asserted => PublishedContactOrigin.Asserted,
        ContactOrigin.Collected => PublishedContactOrigin.Collected,
        _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "The value names no contact origin."),
    };

    /// <summary>Reads the origin a caller's value names.</summary>
    /// <param name="origin">The published value, or <see langword="null" /> for the whole book.</param>
    /// <returns>The origin the book records, or <see langword="null" /> for the whole book.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value names no origin, which the advertised schema already refuses.</exception>
    public static ContactOrigin? Recorded(PublishedContactOrigin? origin) => origin switch
    {
        null => null,
        PublishedContactOrigin.Asserted => ContactOrigin.Asserted,
        PublishedContactOrigin.Collected => ContactOrigin.Collected,
        _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "The value names no contact origin."),
    };
}
