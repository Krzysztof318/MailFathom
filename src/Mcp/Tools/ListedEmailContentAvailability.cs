// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Mcp.Tools;

/// <summary>Reports whether the raw content of a listed email is stored locally, as the protocol spells it.</summary>
/// <remarks>
/// The transport carries its own enumeration for the reason <see cref="ListEmailsDirection" /> does: the member names are
/// the published wire values, so they belong to the boundary that publishes them rather than to the domain that happens
/// to describe the same states today.
/// </remarks>
internal enum ListedEmailContentAvailability
{
    /// <summary>The raw content is stored locally and a content read will find it.</summary>
    Available = 0,

    /// <summary>The email was deliberately stored without its content because it exceeded the configured size limit.</summary>
    ExceededSizeLimit = 1,
}
