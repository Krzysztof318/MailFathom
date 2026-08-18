// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.Contacts;
using MailFathom.Mcp.Tools.Contacts;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Publishes what recording, amending, or promoting one contact produced.</summary>
/// <remarks>
/// <para>
/// One shape for every write, because each of them either leaves the book holding a record or does not, and the reasons
/// it does not are the same set. A caller reads <see cref="State" /> first and everything else from what that state
/// admits.
/// </para>
/// <para>
/// <strong>A refusal publishes an identity and never a record.</strong> Reading the book and writing to it are separate
/// grants, so a caller holding only the writing one must not learn what this deployment holds about somebody by being
/// refused: an amendment stopped because the contact was collected answers with the identity the caller itself named,
/// and an address another contact holds answers with that contact's identity and nothing about them. Only a write that
/// succeeded publishes a record, which is the record the caller just supplied.
/// </para>
/// </remarks>
[Description("What the write produced: whether the book now holds the record, and what stopped it when it does not.")]
internal sealed record ContactWriteToolResult
{
    /// <summary>Gets how the write ended.</summary>
    [Description("How the write ended. written means the book holds the record; notFound means no contact of that identifier is in the book; addressHeldByAnotherContact means one of the addresses already belongs to somebody else, named by addressHolderContactId; contactWasCollected means the record came from mail that arrived rather than from somebody writing it down, so promote_contact it before amending it; alreadyAsserted means a promotion had nothing left to do.")]
    public required ContactWriteState State { get; init; }

    /// <summary>Gets the record as it now stands, or <see langword="null" /> when the write did not happen.</summary>
    [Description("The record as the book now holds it, or null when the write did not happen. Only a successful write publishes a record.")]
    public PublishedContact? Contact { get; init; }

    /// <summary>Gets the contact already holding an address the write claimed, when that is what refused it.</summary>
    [Description("The identifier of one contact that already holds an address this write claimed, or null when that is not what stopped it. Read that contact with get_contact to see who it is; a record may clash with more than one person, and this names one of them.")]
    public string? AddressHolderContactId { get; init; }

    /// <summary>Publishes what a write to the book produced.</summary>
    /// <param name="result">The outcome to publish.</param>
    /// <returns>The wire representation of <paramref name="result" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a written outcome carries no record, which is a defect in the use case rather than a refusal a caller can act on.</exception>
    public static ContactWriteToolResult From(ContactWriteResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Outcome switch
        {
            ContactWriteOutcome.Written when result.Contact is { } written => new ContactWriteToolResult
            {
                State = ContactWriteState.Written,
                Contact = PublishedContact.From(written),
            },
            ContactWriteOutcome.NotFound => new ContactWriteToolResult { State = ContactWriteState.NotFound },
            ContactWriteOutcome.AddressHeldByAnotherContact => new ContactWriteToolResult
            {
                State = ContactWriteState.AddressHeldByAnotherContact,
                AddressHolderContactId = result.AddressHolder?.ToString(),
            },
            ContactWriteOutcome.OriginRefusesWriter => new ContactWriteToolResult
            {
                State = ContactWriteState.ContactWasCollected,
            },
            ContactWriteOutcome.AlreadyAsserted => new ContactWriteToolResult
            {
                State = ContactWriteState.AlreadyAsserted,
            },
            _ => throw new ArgumentOutOfRangeException(
                nameof(result),
                result.Outcome,
                "The outcome is not one the contact tools can produce, or it carries no record where one is required."),
        };
    }
}
