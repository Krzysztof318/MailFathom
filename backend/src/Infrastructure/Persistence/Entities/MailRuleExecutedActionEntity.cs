// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One change a matching rule declared, and what the pass that matched it did about the change.</summary>
/// <remarks>
/// <para>
/// A table of its own rather than a column holding a list, because each row names the mutation record the request went
/// into and that pointer is the join between a rule's decision and what happened on the server.
/// </para>
/// <para>
/// The mutation record is named and never joined to. The two records have retention windows of their own, so a pointer
/// that outlives what it points at is expected rather than a defect: the decision stays explainable after the trail of
/// the change has aged out, and a foreign key would have made the trail's retention silently erase the history instead.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailRuleExecutedActionEntity
{
    public Guid MailRuleExecutionId { get; set; }

    /// <summary>Gets or sets where the action sits in the order its own rule declares its changes, counted from zero.</summary>
    public int Position { get; set; }

    /// <summary>Gets or sets the change asked for, held as its own name.</summary>
    public required string Mutation { get; set; }

    /// <summary>Gets or sets what became of it, held as its own name.</summary>
    public required string Outcome { get; set; }

    /// <summary>Gets or sets the folder the action named, absent for an action naming none.</summary>
    /// <remarks>
    /// An alias where the pass resolved the destination, and the text the rule wrote where it did not, which is why the
    /// column is not named for an alias. The domain record states why the two differ.
    /// </remarks>
    public string? Destination { get; set; }

    /// <summary>Gets or sets why nothing was recorded, present exactly for an action the recorder refused.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Gets or sets the mutation record carrying the request, present exactly for an action that opened one.</summary>
    public Guid? MutationRecordId { get; set; }
}
