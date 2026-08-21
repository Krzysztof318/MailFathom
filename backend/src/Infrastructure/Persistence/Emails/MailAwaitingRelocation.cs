// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Linq.Expressions;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Recognizes a message that is on its way out of the folder it is currently in.</summary>
/// <remarks>
/// <para>
/// A rule declares a move rather than performing one: the record is durable when the rule pass ends and the account's
/// <em>next</em> run carries it to the mail server, so between the two the message sits in a folder it is leaving.
/// Everything derived from a message is derived under that folder's mapping, and passages are not undone by the message
/// moving afterwards — so every path that cuts passages waits, and they wait by the same reading rather than by three
/// copies of it.
/// </para>
/// <para>
/// Only a relocation is read. A copy leaves this message where it is and its second occurrence is discovered in the
/// destination and derived from there, and a pending delete costs at most one cut whose passages the deletion cascades
/// away — neither derives anything under a mapping the message is leaving. A relocation that has completed or been
/// abandoned is not converging either: neither will move the message again, so holding a cut back for one would hold it
/// back for the life of the deployment.
/// </para>
/// </remarks>
internal static class MailAwaitingRelocation
{
    /// <summary>The stored discriminator of the one mutation that can still change which folder a message is in.</summary>
    /// <remarks>
    /// Internal rather than private because one caller narrows a single disjunct of a larger predicate and therefore
    /// writes the clause inline, where an expression of its own cannot be composed. The name is the part that would go
    /// wrong silently — a discriminator nothing stores matches nothing and withholds nothing — so it is the part shared.
    /// </remarks>
    internal static readonly string RelocateMutationName = MailboxMutation.Relocate.Name;

    /// <summary>Gets the predicate that holds for a message no relocation is still converging for.</summary>
    /// <remarks>
    /// Published as an expression so it composes into a query the database evaluates in full, and so a path that
    /// narrows a whole query by it states the rule once rather than restating it.
    /// </remarks>
    internal static Expression<Func<StoredEmailEntity, bool>> IsSettledWhereItIs { get; } =
        email => !email.Mutations.Any(mutation =>
            mutation.Mutation == RelocateMutationName
            && mutation.Stage != MailboxMutationStage.Completed
            && mutation.Stage != MailboxMutationStage.Abandoned);
}
