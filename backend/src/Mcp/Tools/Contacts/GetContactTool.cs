// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Failures;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Failures;
using MailFathom.Mcp.Tools.Categories;
using MailFathom.Mcp.Tools.Results;
using ModelContextProtocol.Server;

namespace MailFathom.Mcp.Tools.Contacts;

/// <summary>Publishes the <c>get_contact</c> tool over the <see cref="ContactBookReader" /> use case.</summary>
/// <param name="contactBookReader">Answers the lookup from the contact book.</param>
/// <remarks>
/// <para>
/// Two ways to name one person, because the two questions an agent actually asks are different. It has an identifier
/// when a listing or a write handed it one, and it has an address when it read one out of mail — and the second is the
/// question the book exists to answer. Both are lookups of one person served from an index, which is why neither is a
/// search over the book.
/// </para>
/// <para>
/// Exactly one of the two is named. Naming neither asks nothing, and naming both can name two different people, which
/// would leave a caller unable to tell which of its questions was answered.
/// </para>
/// <para>
/// A person the book does not hold is an answer rather than a failure. Everything else about the lookup — the grant the
/// caller has to hold, and what the book holds — is the use case's own.
/// </para>
/// </remarks>
[McpServerToolType]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The MCP server materializes this tool type per tool call.")]
internal sealed class GetContactTool(ContactBookReader contactBookReader)
{
    /// <summary>The name the tool is advertised and called under.</summary>
    /// <remarks>Snake case because it is the naming the Model Context Protocol tool ecosystem uses; the C# member naming stops at the boundary.</remarks>
    public const string ToolName = "get_contact";

    /// <summary>The capability a caller must hold to be offered this tool and to reach the use case behind it.</summary>
    /// <remarks>Reading the book is its own grant, so a deployment can let an agent resolve who somebody is without letting it change or erase the record. Declaring it beside the name is what keeps <see cref="PublishedTools" /> able to answer for every tool this surface publishes.</remarks>
    public static MailFathomPermission RequiredPermission => MailFathomPermission.MailContactsRead;

    /// <summary>The kind of thing this tool is for, which is what a deployment publishes or withholds it by.</summary>
    /// <remarks>The book is an assembled record about identified third parties rather than mail that arrived, so a deployment decides separately whether this endpoint offers it. A category decides what this endpoint offers rather than who may reach it, so it turns nothing on: the tool appears only where the capability behind it is available and the caller's grant reaches it.</remarks>
    public static McpToolCategory Category => McpToolCategory.Contacts;

    /// <summary>Reads one person, by the identity the book gave them or by an address they use.</summary>
    /// <param name="contactId">The identifier a listing or a write returned.</param>
    /// <param name="address">An address the person uses.</param>
    /// <param name="cancellationToken">Cancels the read when the caller disconnects or the host shuts down.</param>
    /// <returns>The person, or none where the book holds nobody.</returns>
    /// <exception cref="ContactIdentifierMalformedException">Thrown when neither argument is named, when both are, or when what was named is no identifier and no usable address.</exception>
    /// <exception cref="MailFathomException">
    /// Raised by the use case for a grant it refuses. The call-tool filter turns it into the coded result a client reads,
    /// so this tool neither catches nor re-describes it.
    /// </exception>
    [McpServerTool(
        Name = ToolName,
        Title = "Get contact",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Reads one person from MailFathom's own contact book, named either by the contactId a listing returned or by "
        + "any address they use — name exactly one of the two. Use the address form to answer who a message is from or "
        + "who an address belongs to: at most one contact in the book holds a given address, and the lookup ignores "
        + "casing. Reads local state only: it never contacts a mail server and changes nothing. A person this deployment "
        + "has no record of comes back as an empty answer rather than as an error.")]
    public async Task<GetContactToolResult> GetContactAsync(
        [Description("The contactId a listing or a write returned. Name this or address, and exactly one of the two.")]
        string? contactId = null,
        [Description("Any mail address the person uses, such as the one on a message you are reading, written as the address alone — send anna@example.test rather than Anna Kowalska <anna@example.test>. Matched as a whole address without regard to case. Name this or contactId, and exactly one of the two.")]
        string? address = null,
        CancellationToken cancellationToken = default)
    {
        var namedById = !string.IsNullOrWhiteSpace(contactId);
        var namedByAddress = !string.IsNullOrWhiteSpace(address);

        if (namedById == namedByAddress)
        {
            throw ContactIdentifierMalformedException.NotExactlyOneWay();
        }

        Contact? held = namedById
            ? await contactBookReader.FindAsync(ContactArguments.NamedContact(contactId), cancellationToken)
            : await contactBookReader.FindByAddressAsync(ContactArguments.NamedAddress(address), cancellationToken);

        return GetContactToolResult.From(held);
    }
}
