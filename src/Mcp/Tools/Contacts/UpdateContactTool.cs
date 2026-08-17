// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Failures;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Mcp.Tools.Results;
using ModelContextProtocol.Server;

namespace MailFathom.Mcp.Tools.Contacts;

/// <summary>Publishes the <c>update_contact</c> tool over the <see cref="ContactBookWriter" /> use case.</summary>
/// <param name="contactBookWriter">Amends the record.</param>
/// <remarks>
/// <para>
/// An amendment states the whole record rather than the difference from the one held, which is what keeps adding an
/// address, dropping one, choosing a different default, and correcting a name one operation whose result the book's
/// invariants are checked against. It is idempotent for exactly that reason: the same call made twice leaves the person
/// as the first one left them, because the second states what the first already wrote. What the second call does move is
/// <c>amendedAt</c>, which records when the book was last written rather than what it holds — an idempotent call is one
/// a client may safely repeat, not one that leaves no trace of having been made.
/// </para>
/// <para>
/// It changes state and reaches nothing outside this process, which is what its annotations say. It is not destructive:
/// what it replaces is a record the caller is restating, and taking somebody out of the book is
/// <c>delete_contact</c>.
/// </para>
/// </remarks>
[McpServerToolType]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The MCP server materializes this tool type per tool call.")]
internal sealed class UpdateContactTool(ContactBookWriter contactBookWriter)
{
    /// <summary>The name the tool is advertised and called under.</summary>
    /// <remarks>Snake case because it is the naming the Model Context Protocol tool ecosystem uses; the C# member naming stops at the boundary.</remarks>
    public const string ToolName = "update_contact";

    /// <summary>The capability a caller must hold to be offered this tool and to reach the use case behind it.</summary>
    /// <remarks>Writing the book is its own grant and does not follow from reading it, so a deployment that lets an agent resolve who somebody is has not thereby let it change the record. Declaring it beside the name is what keeps <see cref="PublishedTools" /> able to answer for every tool this surface publishes.</remarks>
    public static MailFathomPermission RequiredPermission => MailFathomPermission.MailContactsWrite;

    /// <summary>Amends one contact to the record the caller states.</summary>
    /// <param name="contactId">The contact to amend.</param>
    /// <param name="displayName">The name the contact is to carry.</param>
    /// <param name="addresses">Every address the contact is to hold afterwards.</param>
    /// <param name="preferredAddress">The address to use by default afterwards.</param>
    /// <param name="note">What the contact's note is to say, or none to hold none.</param>
    /// <param name="cancellationToken">Cancels the write when the caller disconnects or the host shuts down.</param>
    /// <returns>The amended record, or what stopped it.</returns>
    /// <exception cref="ContactIdentifierMalformedException">Thrown when the text names no contact this system issued an identifier for.</exception>
    /// <exception cref="MailFathomException">
    /// Raised by the use case for a record or a grant it refuses. The call-tool filter turns every one of them into the
    /// coded result a client reads, so this tool neither catches nor re-describes any.
    /// </exception>
    [McpServerTool(
        Name = ToolName,
        Title = "Update contact",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Amends one person in MailFathom's own contact book. State the whole record you want them to have — the name, "
        + "every address, which one is preferred, and the note — rather than only what changes: an address the new "
        + "record does not name is removed, and an omitted note clears the one held. Read the contact with get_contact "
        + "first so nothing is dropped by accident. Writes to local state only, and touches no mail. A contact this "
        + "deployment collected from arriving mail answers contactWasCollected: only the operator can take such a "
        + "record on, through mfctl, and it can then be amended.")]
    public async Task<ContactWriteToolResult> UpdateContactAsync(
        [Description("The contactId of the person to amend, as a listing or an earlier write returned it.")]
        string contactId,
        [Description("The name the contact is to carry, up to 256 characters. Characters that render as nothing are refused.")]
        string displayName,
        [Description("Every mail address the contact is to hold afterwards, at most 32 entries, two spellings of one address counting as two entries and stored as one. An address the record no longer names is removed and becomes free for another contact to claim; one another contact already holds refuses the write.")]
        IReadOnlyList<string> addresses,
        [Description("The address to use by default afterwards. Must be one of addresses.")]
        string preferredAddress,
        [Description("What the note is to say afterwards, up to 4000 characters. Omit or send empty to clear it; sending the note back unchanged is what keeps it.")]
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        var draft = new ContactRecordDraft
        {
            DisplayName = displayName,
            Addresses = addresses,
            PreferredAddress = preferredAddress,
            Note = note,
        };

        var amended = await contactBookWriter.AmendAsync(
            ContactArguments.NamedContact(contactId),
            draft,
            cancellationToken);

        return ContactWriteToolResult.From(amended);
    }
}
