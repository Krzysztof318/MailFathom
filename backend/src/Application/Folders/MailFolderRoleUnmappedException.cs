// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Indicates that a folder was named by a role no folder of that account is mapped with.</summary>
/// <remarks>
/// <para>
/// It is raised rather than returned because the fact travels: a rule's destination, a tool's folder filter, and a
/// future feature asking for the junk folder each reach the same resolution and none of them can carry on without an
/// answer. Raising one failure from one place is what makes them refuse identically, instead of a role reading as an
/// empty result here and as an unknown alias there.
/// </para>
/// <para>
/// The message names the role, and the account only when the question was about one. A request that named no account is
/// answered without the deployment's account list, exactly as an unserved account is refused by naming the text the
/// caller supplied and nothing else: a refusal must not become the way to enumerate what is there.
/// </para>
/// </remarks>
public sealed class MailFolderRoleUnmappedException : MailFathomException
{
    /// <summary>Initializes a new refusal naming the account and the role it maps no folder with.</summary>
    /// <param name="accountId">The account the folder was looked for in.</param>
    /// <param name="role">The role nothing of that account is mapped with.</param>
    public MailFolderRoleUnmappedException(MailAccountId accountId, MailFolderSpecialUse role)
        : base($"Account '{accountId.Value}' maps no folder with the special-use role '{role}', so nothing names the folder that was asked for.")
    {
        this.AccountId = accountId;
        this.Role = role;
    }

    /// <summary>Initializes a new refusal for a role no account of a whole scope maps a folder with.</summary>
    /// <param name="role">The role nothing in scope is mapped with.</param>
    public MailFolderRoleUnmappedException(MailFolderSpecialUse role)
        : base($"No mail account in scope maps a folder with the special-use role '{role}', so nothing names the folder that was asked for.") =>
        this.Role = role;

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.MailFolderRoleUnmapped;

    /// <summary>Gets the account the folder was looked for in, and <see langword="null" /> when a whole scope was asked.</summary>
    /// <remarks>
    /// The absence is a distinct fact rather than a missing value: a request that named several accounts, or none, asked
    /// every mailbox the deployment serves, and naming one of them would say the refusal was about that one.
    /// </remarks>
    public MailAccountId? AccountId { get; }

    /// <summary>Gets the role nothing is mapped with.</summary>
    public MailFolderSpecialUse Role { get; }
}
