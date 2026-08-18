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
/// and an address another contact holds answers with that contact's identity and nothing about them.
/// </para>
/// <para>
/// <strong>What a success publishes follows the same rule, which is why there are two factories.</strong>
/// <see cref="From" /> answers a write whose record the caller stated, so reading it back is an answer about their own
/// request rather than about the book. <see cref="OutcomeOf" /> answers a write that named a person and nothing else —
/// a promotion — where a record would be the book's own contents obtained from an identifier alone, under a grant that
/// never included reading them. Such a caller learns that the promotion happened and reads the person through
/// <c>get_contact</c>, which is the tool published for reading them.
/// </para>
/// </remarks>
[Description("What the write produced: whether the book now holds the record, and what stopped it when it does not.")]
internal sealed record ContactWriteToolResult
{
    /// <summary>Gets how the write ended.</summary>
    [Description("How the write ended. written means the book holds the record; notFound means no contact of that identifier is in the book; addressHeldByAnotherContact means one of the addresses already belongs to somebody else, named by addressHolderContactId; contactWasCollected means the record came from mail that arrived rather than from somebody writing it down, so promote_contact it before amending it; alreadyAsserted means a promotion had nothing left to do.")]
    public required ContactWriteState State { get; init; }

    /// <summary>Gets the record as it now stands, or <see langword="null" /> when the write did not happen.</summary>
    [Description("The record as the book now holds it, or null. Only a write whose record you supplied publishes one: create_contact, update_contact, add_contact_address, and remove_contact_address answer with the record when they succeed, while promote_contact answers with the outcome alone and is read back with get_contact.")]
    public PublishedContact? Contact { get; init; }

    /// <summary>Gets the contact already holding an address the write claimed, when that is what refused it.</summary>
    [Description("The identifier of one contact that already holds an address this write claimed, or null when that is not what stopped it. Read that contact with get_contact to see who it is; a record may clash with more than one person, and this names one of them.")]
    public string? AddressHolderContactId { get; init; }

    /// <summary>Publishes what a write whose record the caller stated produced.</summary>
    /// <param name="result">The outcome to publish.</param>
    /// <returns>The wire representation of <paramref name="result" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a written outcome carries no record, which is a defect in the use case rather than a refusal a caller can act on.</exception>
    /// <remarks>The written record is the caller's own request as the book settled it, which is what makes reading it back an answer about the request rather than about the book.</remarks>
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

    /// <summary>Publishes what a write the caller stated no record for produced, which is the outcome and nothing else.</summary>
    /// <param name="result">The outcome to publish.</param>
    /// <returns>The wire representation of <paramref name="result" />, carrying no record.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the outcome is not one a promotion produces.</exception>
    /// <remarks>
    /// A promotion names one person and changes their origin, so a record in the answer would be the whole of what
    /// <c>get_contact</c> publishes, reached from an identifier alone under a grant that never included reading it.
    /// <c>mailfathom.mail.contacts.write</c> does not imply <c>mailfathom.mail.contacts.read</c>, and this is where that
    /// would otherwise leak.
    /// </remarks>
    public static ContactWriteToolResult OutcomeOf(ContactWriteResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Outcome switch
        {
            ContactWriteOutcome.Written => new ContactWriteToolResult { State = ContactWriteState.Written },
            ContactWriteOutcome.NotFound => new ContactWriteToolResult { State = ContactWriteState.NotFound },
            ContactWriteOutcome.AlreadyAsserted => new ContactWriteToolResult
            {
                State = ContactWriteState.AlreadyAsserted,
            },
            _ => throw new ArgumentOutOfRangeException(
                nameof(result),
                result.Outcome,
                "The outcome is not one a promotion produces."),
        };
    }
}
