// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Contacts;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Mcp.Tools.Categories;
using MailFathom.Mcp.Tools.Results;
using ModelContextProtocol.Server;

namespace MailFathom.Mcp.Tools.Contacts;

/// <summary>Publishes the <c>create_contact</c> tool over the <see cref="ContactBookWriter" /> use case.</summary>
/// <param name="contactBookWriter">Records the person.</param>
/// <remarks>
/// <para>
/// The first tool on this surface that changes state, and it leaves the process for nothing: the record is written to
/// MailFathom's own database and reaches no mail server and no third party, which is what its annotations say. It is not
/// idempotent, and the annotation says that too — the book mints the identity, so calling twice with one record records
/// one person and then refuses, naming the contact that already holds the address.
/// </para>
/// <para>
/// Every rule the record obeys is the use case's, so this class carries no validation of its own. What a caller supplied
/// travels as text and comes back refused by name rather than by value, because a name, an address, and a note are
/// personal data about a third party.
/// </para>
/// </remarks>
[McpServerToolType]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The MCP server materializes this tool type per tool call.")]
internal sealed class CreateContactTool(ContactBookWriter contactBookWriter)
{
    /// <summary>The name the tool is advertised and called under.</summary>
    /// <remarks>Snake case because it is the naming the Model Context Protocol tool ecosystem uses; the C# member naming stops at the boundary.</remarks>
    public const string ToolName = "create_contact";

    /// <summary>The capability a caller must hold to be offered this tool and to reach the use case behind it.</summary>
    /// <remarks>Writing the book is its own grant and does not follow from reading it, so a deployment that lets an agent resolve who somebody is has not thereby let it change the record. Declaring it beside the name is what keeps <see cref="PublishedTools" /> able to answer for every tool this surface publishes.</remarks>
    public static MailFathomPermission RequiredPermission => MailFathomPermission.MailContactsWrite;

    /// <summary>The kind of thing this tool is for, which is what a deployment publishes or withholds it by.</summary>
    /// <remarks>The book is an assembled record about identified third parties rather than mail that arrived, so a deployment decides separately whether this endpoint offers it. A category decides what this endpoint offers rather than who may reach it, so it turns nothing on: the tool appears only where the capability behind it is available and the caller's grant reaches it.</remarks>
    public static McpToolCategory Category => McpToolCategory.Contacts;

    /// <summary>Records a person the book does not yet hold.</summary>
    /// <param name="displayName">The name to record for this person.</param>
    /// <param name="addresses">Every address this person uses.</param>
    /// <param name="preferredAddress">The address to use by default.</param>
    /// <param name="note">What to record about this person, or none.</param>
    /// <param name="cancellationToken">Cancels the write when the caller disconnects or the host shuts down.</param>
    /// <returns>The record as written, or what stopped it.</returns>
    /// <exception cref="MailFathomException">
    /// Raised by the use case for a record or a grant it refuses. The call-tool filter turns every one of them into the
    /// coded result a client reads, so this tool neither catches nor re-describes any.
    /// </exception>
    [McpServerTool(
        Name = ToolName,
        Title = "Create contact",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Records a person in MailFathom's own contact book: their name, every address they use, which one is preferred, "
        + "and an optional note. Writes to local state only — nothing is sent to a mail server or to anybody else, and "
        + "no mail is touched. Calling twice with the same person records them once and then answers "
        + "addressHeldByAnotherContact, because one address belongs to one contact across the whole book; look that "
        + "contact up with get_contact rather than writing a second record. Ask the person you are acting for before "
        + "writing somebody down.")]
    public async Task<ContactWriteToolResult> CreateContactAsync(
        [Description("The name to record for this person, as it should be read back, up to 256 characters. Characters that render as nothing are refused.")]
        string displayName,
        [Description("Every mail address this person uses, at most 32 entries of at most 320 characters each. Two spellings of one address are stored once and the first spelling is the one kept, but both still count towards the 32. An address another contact already holds refuses the write.")]
        IReadOnlyList<string> addresses,
        [Description("The address to use when addressing this person without naming which of theirs to use. Must be one of addresses; state it even where the record names a single address, because nothing picks one for the owner.")]
        string preferredAddress,
        [Description("What to record about this person, up to 4000 characters, or omit for none. Line breaks and tabs are kept. This is free text about a third party: write only what the person you are acting for asked to be recorded.")]
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

        var written = await contactBookWriter.RecordAsync(draft, cancellationToken);

        return ContactWriteToolResult.From(written);
    }
}
