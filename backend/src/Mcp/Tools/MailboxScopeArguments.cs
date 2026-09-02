// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Mcp.Tools;

/// <summary>Converts the accounts and folders a caller named into the domain identities a mailbox read is expressed in.</summary>
/// <remarks>
/// <para>
/// This is the one thing a use case cannot do for a protocol adapter: a caller sends text, and a query takes identities.
/// It is shared by every tool that takes a mailbox scope so the conversion, the ceiling it is applied under, and the
/// refusal it raises are one behavior rather than one per tool, which is what stops a second tool from being written
/// with a subtly weaker rule.
/// </para>
/// <para>
/// No bound of the query itself is restated here. The counts are the query's own limits, read from
/// <see cref="MailboxScope" />, and everything a filter accepts beyond them is decided by the use case.
/// </para>
/// </remarks>
internal static class MailboxScopeArguments
{
    /// <summary>The greatest length an account identifier or folder alias a caller names may carry.</summary>
    /// <remarks>
    /// Generous against every identifier an operator configures and short enough that a refusal cannot become a way to
    /// place a paragraph of caller-chosen text into an error message or a log line.
    /// </remarks>
    private const int MaximumIdentifierLength = 256;

    /// <summary>Turns the text a caller named accounts with into domain values.</summary>
    /// <param name="accounts">The accounts the caller named, or <see langword="null" /> when it named none.</param>
    /// <returns>The named accounts, empty when the caller named none.</returns>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when more than the accepted number are named, or one of them is text no account of this system could be named by.</exception>
    /// <remarks>
    /// Which account the text names is deliberately not settled here. An account may be named by its configured
    /// identifier or by the display name it is published under, and the two are matched inside the use case against the
    /// accounts the caller's owner owns — which is the only set in which either spelling names one mailbox — so text
    /// naming nothing meets the same refusal as an account the deployment stopped serving and as one somebody else owns.
    /// </remarks>
    public static IReadOnlyList<MailAccountSelector> Accounts(string[]? accounts) =>
        Parse(accounts, MailAccountSelector.Create, MailboxScope.MaximumAccountIds, "accounts");

    /// <summary>Turns the folders a caller supplied into domain values.</summary>
    /// <param name="folders">The folders the caller named, or <see langword="null" /> when it named none.</param>
    /// <returns>The named folders, empty when the caller named none.</returns>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when more than the accepted number are named, or one of them names neither an alias this system issues nor a role it knows.</exception>
    /// <remarks>
    /// Which folder of which account a role names is deliberately not settled here, exactly as which account a name
    /// selects is not: that answer belongs to the account in scope, so it is given inside the use case and text naming
    /// no folder of any of them meets the same refusal wherever it came from.
    /// </remarks>
    public static IReadOnlyList<MailFolderReference> Folders(string[]? folders) =>
        Parse(folders, MailFolderReference.Create, MailboxScope.MaximumFolders, "folders");

    /// <summary>Converts one list of caller-supplied text into the domain identity it names.</summary>
    /// <remarks>
    /// <para>
    /// The count is checked before anything is converted. The list arrives from a caller nobody vouches for, and a
    /// ceiling applied after the trimming and upper-casing it exists to prevent has already run over every element is not
    /// a ceiling. The limit is the query's own, so the boundary refuses early without inventing a second one.
    /// </para>
    /// <para>
    /// The conversion is refused through the query's own filter failure rather than through a failure this boundary
    /// declares. The code it carries already names a filter the query cannot accept, so a caller reads one code for one
    /// answer, and the refusal reaches the client and the log through the single path every use-case refusal takes.
    /// </para>
    /// <para>
    /// The refused value is deliberately absent from it. An identifier a caller invented is that caller's own input rather
    /// than one of MailFathom's configured names, and echoing input back is how a boundary starts reflecting content.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<TIdentity> Parse<TIdentity>(
        string[]? values,
        Func<string, TIdentity> createIdentity,
        int limit,
        string filterName)
    {
        if (values is null or { Length: 0 })
        {
            return [];
        }

        MailboxQueryFilterInvalidException.ThrowIfCountExceeded(values.Length, limit, filterName);

        try
        {
            return [.. values.Select(value => createIdentity(UsableIdentifierText(value, filterName)))];
        }
        catch (ArgumentException exception)
        {
            throw MailboxQueryFilterInvalidException.NotAUsableIdentifier(filterName, exception);
        }
    }

    /// <summary>Refuses text no name of this system is spelled with, before a domain type is asked to read it.</summary>
    /// <remarks>
    /// One rule is applied to both kinds of name at this boundary, whatever each domain type goes on to check for
    /// itself, because a name that is never matched still travels: an account this deployment does not serve is named
    /// back in the refusal a client reads, so an unbounded string carrying newlines would be a way to write arbitrary
    /// text into that contract and into the log beside it.
    /// </remarks>
    private static string UsableIdentifierText(string value, string filterName)
    {
        if (value.Length > MaximumIdentifierLength || value.Any(char.IsControl))
        {
            throw MailboxQueryFilterInvalidException.NotAUsableIdentifier(filterName);
        }

        return value;
    }
}
