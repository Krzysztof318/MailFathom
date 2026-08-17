// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Contacts;

/// <summary>What one write to a deployment's contact book produced.</summary>
/// <param name="Outcome">How the write ended, by the name the deployment's own outcome carries.</param>
/// <param name="Contact">The record the outcome is about, or <see langword="null" /> where the book held none.</param>
/// <param name="AddressHolder">The contact already holding an address the write claimed, present exactly when that is what refused it.</param>
/// <remarks>
/// A refusal arrives as a named outcome with a success status rather than as an error, because each one is something the
/// operator acts on and continues from: correcting a record whose address somebody else holds, promoting a collected
/// contact before amending it, or discovering the person was erased meanwhile.
/// </remarks>
internal sealed record ContactWriteAnswer(
    [property: JsonPropertyName("outcome")] string? Outcome,
    [property: JsonPropertyName("contact")] ContactRecord? Contact,
    [property: JsonPropertyName("addressHolder")] Guid? AddressHolder)
{
    /// <summary>The outcome naming a write the book performed.</summary>
    internal const string Written = "Written";

    /// <summary>The outcome naming a contact the book does not hold.</summary>
    internal const string NotFound = "NotFound";

    /// <summary>The outcome naming an address a different contact already holds.</summary>
    internal const string AddressHeldByAnotherContact = "AddressHeldByAnotherContact";

    /// <summary>The outcome naming a write the contact's origin does not admit.</summary>
    internal const string OriginRefusesWriter = "OriginRefusesWriter";

    /// <summary>The outcome naming a promotion asked of a contact that is already asserted.</summary>
    internal const string AlreadyAsserted = "AlreadyAsserted";

    /// <summary>Reports whether the book now holds what the operator asked for.</summary>
    /// <returns><see langword="true" /> when the write was performed.</returns>
    internal bool WasWritten() => string.Equals(this.Outcome, Written, StringComparison.Ordinal);

    /// <summary>States what stopped the write, in terms of what the operator does next.</summary>
    /// <returns>One sentence, naming no part of anybody's record beyond an identifier.</returns>
    /// <remarks>
    /// The holder of a clashing address is named by its identity alone, because the deployment answers with that and
    /// nothing more: reading that person is a lookup the operator performs deliberately rather than something a refused
    /// write hands them.
    /// </remarks>
    internal string DescribeRefusal() => this.Outcome switch
    {
        NotFound => "The deployment's contact book holds no contact of that identity.",
        AddressHeldByAnotherContact =>
            $"One of those addresses already belongs to contact {this.AddressHolder:D}. Read that contact, or drop the address from this record.",
        OriginRefusesWriter =>
            "That contact was collected from arriving mail rather than written down, and a collected record is not amended in place. Promote it first with 'mfctl contact promote'.",
        AlreadyAsserted => "That contact is already asserted, so there was no promotion left to perform.",
        _ => "The deployment refused the write without naming an outcome this command understands.",
    };
}
