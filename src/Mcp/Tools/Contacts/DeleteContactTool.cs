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

/// <summary>Publishes the <c>delete_contact</c> tool over the <see cref="ContactBookWriter" /> use case.</summary>
/// <param name="contactBookWriter">Performs the erasure.</param>
/// <remarks>
/// <para>
/// The clearest destructive tool this surface has, and the annotation says so: it erases a record about a person and
/// everything the book derived from them, it cannot be undone, and a client that auto-approves it is approving something
/// no read tool has ever done. It is idempotent all the same — erasing somebody twice leaves the book in the state the
/// caller asked for, which is the state it was already in.
/// </para>
/// <para>
/// Erasure is the data-subject path, so no origin gates it: somebody asking to be taken out of a contact book is not
/// answered with which half of the book they happen to be in. That is why a contact this deployment collected can be
/// erased by a caller that could not have amended it.
/// </para>
/// </remarks>
[McpServerToolType]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The MCP server materializes this tool type per tool call.")]
internal sealed class DeleteContactTool(ContactBookWriter contactBookWriter)
{
    /// <summary>The name the tool is advertised and called under.</summary>
    /// <remarks>Snake case because it is the naming the Model Context Protocol tool ecosystem uses; the C# member naming stops at the boundary.</remarks>
    public const string ToolName = "delete_contact";

    /// <summary>The capability a caller must hold to be offered this tool and to reach the use case behind it.</summary>
    /// <remarks>The same grant the other two writes are behind: a caller that may edit the book may take somebody out of it, and no smaller grant reaches an act that cannot be undone. Declaring it beside the name is what keeps <see cref="PublishedTools" /> able to answer for every tool this surface publishes.</remarks>
    public static MailFathomPermission RequiredPermission => MailFathomPermission.MailContactsWrite;

    /// <summary>Erases one person and everything the book derived from them.</summary>
    /// <param name="contactId">The contact to erase.</param>
    /// <param name="cancellationToken">Cancels the erasure when the caller disconnects or the host shuts down.</param>
    /// <returns>What the erasure removed.</returns>
    /// <exception cref="ContactIdentifierMalformedException">Thrown when the text names no contact this system issued an identifier for.</exception>
    /// <exception cref="MailFathomException">
    /// Raised by the use case for a grant it refuses. The call-tool filter turns it into the coded result a client reads,
    /// so this tool neither catches nor re-describes it.
    /// </exception>
    [McpServerTool(
        Name = ToolName,
        Title = "Delete contact",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Erases one person from MailFathom's own contact book and removes every address recorded with them. This cannot "
        + "be undone: the record is deleted rather than marked, and nothing here can bring it back. It removes only the "
        + "contact record — no mail is deleted and no mail server is contacted. Erasing somebody the book does not hold "
        + "is reported as a completed erasure rather than as an error, so repeating the call is safe. Confirm with the "
        + "person you are acting for before calling it.")]
    public async Task<DeleteContactToolResult> DeleteContactAsync(
        [Description("The contactId of the person to erase, as a listing or an earlier write returned it. Read them with get_contact first if you need to be sure who this is: the answer afterwards carries no name, address, or note.")]
        string contactId,
        CancellationToken cancellationToken = default)
    {
        var erasure = await contactBookWriter.EraseAsync(
            ContactArguments.NamedContact(contactId),
            cancellationToken);

        return DeleteContactToolResult.From(erasure);
    }
}
