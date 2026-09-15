// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Commands.Accounts;

/// <summary>What the two custody commands share: the values a deployment declares, and how one is read off a command line.</summary>
/// <remarks>
/// Here rather than on either command because the value <c>switch</c> sends and the value <c>show</c> prints back are
/// one vocabulary, and two copies of it would drift the day a third custody exists. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </remarks>
internal static class MailAccountCustodies
{
    /// <summary>The custody under which MailFathom holds the mailbox and the source is emptied of what it holds.</summary>
    internal const string HoldMailbox = "HoldMailbox";

    /// <summary>The custody every account has until somebody changes it, under which the source server is the truth.</summary>
    internal const string MirrorSource = "MirrorSource";

    /// <summary>Reads what the operator wrote as one of the two declared custodies, however they cased it.</summary>
    /// <param name="custody">What was written on the command line.</param>
    /// <returns>The declared value, or <see langword="null" /> where what was written names neither.</returns>
    internal static string? Normalize(string custody) => custody.Trim() switch
    {
        var written when written.Equals(HoldMailbox, StringComparison.OrdinalIgnoreCase) => HoldMailbox,
        var written when written.Equals(MirrorSource, StringComparison.OrdinalIgnoreCase) => MirrorSource,
        _ => null,
    };
}
