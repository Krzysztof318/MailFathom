// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Failures;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Mcp.Tools.Categories;
using MailFathom.Mcp.Tools.Results;
using ModelContextProtocol.Server;

namespace MailFathom.Mcp.Tools.Contacts;

/// <summary>Publishes the <c>promote_contact</c> tool over the <see cref="ContactBookWriter" /> use case.</summary>
/// <param name="contactBookWriter">Promotes the record.</param>
/// <remarks>
/// <para>
/// The one crossing between the two origins, and it runs one way: a contact this deployment collected from arriving
/// mail becomes one somebody wrote down. It exists on this surface as well as on the administrative one because that is
/// what a collected record is for — an agent that read the book and found somebody the deployment picked up is taking
/// the record on for the same owner an operator at a terminal would, and a promotion reachable from only one of the two
/// would leave <c>update_contact</c> permanently refused on this surface for every record collection produced.
/// </para>
/// <para>
/// It changes state, reaches nothing outside this process, and is not destructive: nothing about the person is
/// rewritten, and what moves is which half of the book they are in. Asking twice is asking once — the second call
/// answers <c>alreadyAsserted</c>, which is the state the first call left the record in.
/// </para>
/// <para>
/// <b>It answers with the outcome and never with the record.</b> The caller supplied an identifier rather than a
/// person, so publishing the promoted contact back would hand the whole of what <c>get_contact</c> serves to a caller
/// holding only <c>mailfathom.mail.contacts.write</c> — and no permission in this system implies another. A caller that
/// also holds the reading grant reads the person through the tool published for reading them. The administrative route
/// behind the same use case answers the same way and for the same reason.
/// </para>
/// </remarks>
[McpServerToolType]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The MCP server materializes this tool type per tool call.")]
internal sealed class PromoteContactTool(ContactBookWriter contactBookWriter)
{
    /// <summary>The name the tool is advertised and called under.</summary>
    /// <remarks>Snake case because it is the naming the Model Context Protocol tool ecosystem uses; the C# member naming stops at the boundary.</remarks>
    public const string ToolName = "promote_contact";

    /// <summary>The capability a caller must hold to be offered this tool and to reach the use case behind it.</summary>
    /// <remarks>The writing grant rather than a name of its own, because promotion is a write to the book: it is what a caller does instead of amending a record it may not amend in place.</remarks>
    public static MailFathomPermission RequiredPermission => MailFathomPermission.MailContactsWrite;

    /// <summary>The kind of thing this tool is for, which is what a deployment publishes or withholds it by.</summary>
    /// <remarks>The book is an assembled record about identified third parties rather than mail that arrived, so a deployment decides separately whether this endpoint offers it. A category decides what this endpoint offers rather than who may reach it, so it turns nothing on: the tool appears only where the capability behind it is available and the caller's grant reaches it.</remarks>
    public static McpToolCategory Category => McpToolCategory.Contacts;

    /// <summary>Takes on one contact this deployment collected.</summary>
    /// <param name="contactId">The contact to promote.</param>
    /// <param name="cancellationToken">Cancels the write when the caller disconnects or the host shuts down.</param>
    /// <returns>How the promotion ended, carrying no record whichever way it did.</returns>
    /// <exception cref="ContactIdentifierMalformedException">Thrown when the text names no contact this system issued an identifier for.</exception>
    /// <exception cref="MailFathomException">
    /// Raised by the use case for a grant it refuses. The call-tool filter turns every one of them into the coded result
    /// a client reads, so this tool neither catches nor re-describes any.
    /// </exception>
    [McpServerTool(
        Name = ToolName,
        Title = "Promote contact",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Takes on one person MailFathom collected from arriving mail, so the record becomes one the owner asserted "
        + "rather than one the deployment inferred. This is the only path between the two origins and it runs one way; "
        + "it is also what unlocks update_contact on a record that answered contactWasCollected. Nothing about the "
        + "person is rewritten. Writes to local state only, and touches no mail. A contact that was already asserted "
        + "answers alreadyAsserted. The answer carries the outcome alone and never the record; read the person with "
        + "get_contact.")]
    public async Task<ContactWriteToolResult> PromoteContactAsync(
        [Description("The contactId of the collected person to take on, as a listing or an earlier read returned it.")]
        string contactId,
        CancellationToken cancellationToken = default)
    {
        var promoted = await contactBookWriter.PromoteAsync(
            ContactArguments.NamedContact(contactId),
            cancellationToken);

        return ContactWriteToolResult.OutcomeOf(promoted);
    }
}
