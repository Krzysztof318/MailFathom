// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Linq.Expressions;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Recognizes a message the rule passes have finished with, however they finished with it.</summary>
/// <remarks>
/// <para>
/// Everything derived from a message waits for the rules, because a rule declares a move and passages derived before it
/// would describe a folder the message is leaving. There are two ways to be finished: a pass evaluated the message and
/// stamped it, or the message is a copy MailFathom itself filed of this deployment's own outgoing mail, which no pass
/// will ever evaluate — such a row is deliberately never stamped, since stamping it would claim an evaluation that
/// never happened.
/// </para>
/// <para>
/// Stating both here is what keeps the second from becoming a silence. A gate reading the stamp alone would leave every
/// filed copy uncut and unembedded for the life of the deployment, which is invisible until somebody asks a question
/// about mail they sent and is answered from everything except it.
/// </para>
/// </remarks>
internal static class MailAwaitingRuleEvaluation
{
    /// <summary>Gets the predicate that holds for a message no rule pass still owes an evaluation.</summary>
    /// <remarks>
    /// Published as an expression so it composes into a query the database evaluates in full. A path that narrows a
    /// single disjunct of a larger predicate writes the two clauses inline instead, exactly as
    /// <see cref="MailAwaitingRelocation" /> is written inline there, and names this type in a comment so the rule is
    /// read from one place.
    /// </remarks>
    internal static Expression<Func<StoredEmailEntity, bool>> IsFinishedWith { get; } =
        email => email.RulesEvaluatedAt != null || email.FiledFromOutgoingEmailId != null;
}
