// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Infrastructure.Persistence.Policies.MailAccounts;

/// <summary>One mail account's row as a write that changes its governing policies reads it.</summary>
/// <remarks>
/// Not the entity, for the reason <see cref="StoredGovernedUserRow" /> is not: the document is read only where its
/// length is within what this build binds an account from, so a row written beside MailFathom is passed over rather
/// than transferred.
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "EF Core materializes this row from the bounded read's result set.")]
internal sealed class StoredGovernedMailAccountRow
{
    /// <summary>Gets or sets the account's identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the address the account holds, or <see langword="null" /> where it holds none.</summary>
    public string? EmailAddress { get; set; }

    /// <summary>Gets or sets the name the account is told apart by.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Gets or sets the organization the account belongs to, or <see langword="null" /> for none.</summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>Gets or sets the account's own settings, or <see langword="null" /> where they are past the bound and were not sent.</summary>
    public string? Document { get; set; }

    /// <summary>Gets or sets the version the account stands at.</summary>
    public long Version { get; set; }

    /// <summary>Gets or sets the users the account is assigned to, in the order they were assigned.</summary>
    public Guid[] Users { get; set; } = [];
}
