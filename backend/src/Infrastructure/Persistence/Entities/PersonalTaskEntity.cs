// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Tasks;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One thing a person owes, entered by them or read out of their mail.</summary>
/// <remarks>
/// The user is a value, as it is on every other table, and so is the message — which is the one place this row
/// deliberately differs from the notification beside it. A notification only offers to open something, so it is erased
/// with the mail it leads to; a task is a commitment, and what a person owes is not undone by the mail naming it being
/// erased. <c>mailbox_mutation_audit_entries</c> and the payload of a job cite a message the same way and for the
/// same reason: a reference that has to outlive what it points at is a value rather than an association.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersonalTaskEntity
{
    public Guid Id { get; set; }

    /// <summary>Gets or sets the person whose list it is on, as a value rather than as an association.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the line the list is drawn with.</summary>
    public required string Title { get; set; }

    /// <summary>Gets or sets the day it is due on, and <see langword="null" /> where nobody has said when.</summary>
    public DateOnly? DueOn { get; set; }

    /// <summary>Gets or sets where the task came from, which accepting a proposal moves rather than replaces.</summary>
    public PersonalTaskOrigin Origin { get; set; }

    /// <summary>Gets or sets whether the person has done it.</summary>
    public bool IsCompleted { get; set; }

    /// <summary>Gets or sets the message the task was read out of, and <see langword="null" /> where it cites none.</summary>
    /// <remarks>
    /// A value rather than a foreign key, which is what lets the task outlive the message. A reader resolving a
    /// citation whose message has been erased finds nothing under that identity and says so, exactly as a job whose
    /// payload names erased mail does.
    /// </remarks>
    public Guid? SourceStoredEmailId { get; set; }
}
