// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Which of the two mail-serving endpoints this user is served on, as their record states it.</summary>
/// <remarks>
/// <para>
/// Stated in the record so that every way a record is written reaches it — the switch command, a whole record saved
/// from an editor, and the record route beneath both — and read back by nothing on a request. Authentication must never
/// depend on reading a document, so the commit that writes the record also writes these two values onto the user's
/// row, in the same statement, and the credential and session reads join the row.
/// </para>
/// <para>
/// Both default on, which is what a record written before this block existed reads as and what a new user is recorded
/// with. Whoever administers the deployment decides them: a user saving their own record is refused a change to either.
/// </para>
/// </remarks>
internal sealed class UserEndpointAccessOptions
{
    /// <summary>The section this block is bound from within the record.</summary>
    internal const string BlockName = "EndpointAccess";

    /// <summary>The record key the MCP switch is written under.</summary>
    internal const string McpEndpointKey = $"{BlockName}:{nameof(McpEndpoint)}";

    /// <summary>The record key the client switch is written under.</summary>
    internal const string ClientEndpointKey = $"{BlockName}:{nameof(ClientEndpoint)}";

    /// <summary>Gets or sets whether this user is served on the MCP endpoint, whichever credential they present there.</summary>
    public bool McpEndpoint { get; set; } = true;

    /// <summary>Gets or sets whether this user is served on the client endpoint, whichever credential or session they present there.</summary>
    public bool ClientEndpoint { get; set; } = true;

    /// <summary>Gets the switches as the value the user's row carries.</summary>
    internal MailUserEndpointAccess Access => new(this.McpEndpoint, this.ClientEndpoint);

    /// <summary>Reads the switches a flattened record states, taking an absent or unreadable one as on.</summary>
    /// <param name="settings">The record, flattened to its colon-separated keys.</param>
    /// <returns>The switches.</returns>
    /// <remarks>
    /// Asked of a record the deployment already holds, which its binder accepted when it was committed, so an
    /// unreadable value is not a case a held record reaches; on is what the binder would have taken for an absent one.
    /// </remarks>
    internal static MailUserEndpointAccess ReadFrom(IReadOnlyDictionary<string, string> settings) =>
        new(Switch(settings, McpEndpointKey), Switch(settings, ClientEndpointKey));

    private static bool Switch(IReadOnlyDictionary<string, string> settings, string key) =>
        !settings.TryGetValue(key, out var written) || !bool.TryParse(written, out var enabled) || enabled;
}
