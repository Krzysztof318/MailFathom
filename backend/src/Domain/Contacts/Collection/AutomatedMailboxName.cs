// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Contacts.Collection;

/// <summary>Answers whether an address names a machine, a role, or a mailing list's administration rather than a person.</summary>
/// <remarks>
/// <para>
/// This is the half of collection's bounds an owner does not write. A book is worth having because it holds people, and
/// the addresses below are the ones every mailbox receives from without anybody corresponding with them — so leaving
/// them to a configured exclusion list would mean every deployment discovering the same noise and writing the same list
/// against it.
/// </para>
/// <para>
/// Each of the three rules is anchored in something published rather than in a survey of what senders happen to use.
/// <b>The role mailboxes</b> are RFC 2142's, which defines them precisely as names a function is reached at rather than
/// a person, together with <c>mailer-daemon</c>, the name a transport has reported delivery failures under since RFC
/// 821. <b>The list-administration suffixes</b> are the convention RFC 2142 § 5 states for reaching a list's machinery
/// instead of its readers. <b>The no-reply prefixes</b> are the one rule with no standard behind them, and they are here
/// because an address that says in its own name that a reply goes nowhere is stating that nobody corresponds with it.
/// </para>
/// <para>
/// The comparison is on the address's own comparison form, so the rule is the same rule the book matches addresses by
/// and a sender's casing decides nothing. What the rule costs is stated rather than hidden: a person whose mailbox is
/// genuinely <c>news@</c> or <c>sales@</c> at their own domain is not collected, and an owner who corresponds with them
/// writes them down instead — which is the safe direction, since a book that is missing somebody is corrected by
/// recording them and a book full of machines is not corrected at all.
/// </para>
/// </remarks>
public static class AutomatedMailboxName
{
    /// <summary>The names RFC 2142 publishes for a function rather than a person, and the transport's own reporting name.</summary>
    private static readonly string[] RoleMailboxes =
    [
        "ABUSE",
        "FTP",
        "HOSTMASTER",
        "INFO",
        "MAILER-DAEMON",
        "MARKETING",
        "NEWS",
        "NOC",
        "POSTMASTER",
        "SALES",
        "SECURITY",
        "SUPPORT",
        "USENET",
        "UUCP",
        "WEBMASTER",
    ];

    /// <summary>The spellings an address uses to say that a reply to it reaches nobody.</summary>
    private static readonly string[] NoReplyPrefixes =
    [
        "NOREPLY",
        "NO-REPLY",
        "NO_REPLY",
        "NO.REPLY",
        "DONOTREPLY",
        "DO-NOT-REPLY",
        "DO_NOT_REPLY",
    ];

    /// <summary>The suffixes RFC 2142 § 5's convention reaches a list's machinery by rather than its readers.</summary>
    private static readonly string[] ListAdministrationSuffixes =
    [
        "-REQUEST",
        "-BOUNCES",
        "-OWNER",
        "-ADMIN",
        "-SUBSCRIBE",
        "-UNSUBSCRIBE",
    ];

    /// <summary>Answers whether the address names something other than a person to correspond with.</summary>
    /// <param name="address">The address to judge.</param>
    /// <returns><see langword="true" /> when the address is a role, a machine, or a list's administration.</returns>
    /// <remarks>
    /// An address whose local part cannot be read — which is an address with nothing before its at-sign, and therefore
    /// one no mailbox is reached at — is treated as automated, because the alternative is recording a person the book
    /// could never address.
    /// </remarks>
    public static bool Names(EmailAddress address)
    {
        var localPart = LocalPartOf(address);

        if (localPart.Length == 0)
        {
            return true;
        }

        return RoleMailboxes.Contains(localPart, StringComparer.Ordinal)
            || NoReplyPrefixes.Any(prefix => localPart.StartsWith(prefix, StringComparison.Ordinal))
            || ListAdministrationSuffixes.Any(suffix => localPart.EndsWith(suffix, StringComparison.Ordinal));
    }

    /// <summary>Reads the comparison form of what precedes the address's last at-sign.</summary>
    /// <remarks>The domain half is not read at all here, because what a name says about a mailbox is said by its local part.</remarks>
    private static string LocalPartOf(EmailAddress address) =>
        address.TrySplit(out var localPart, out _) ? localPart : string.Empty;
}
