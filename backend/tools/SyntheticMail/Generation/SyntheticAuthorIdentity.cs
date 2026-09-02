// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation;

/// <summary>Whose address a generated message is <c>From</c>.</summary>
/// <remarks>
/// This exists because submission servers disagree about it. A local mail server accepts any author, which is what
/// makes <see cref="Fabricated" /> the default and the more useful corpus: the author is one of the axes MailFathom
/// reads, and a batch whose every message came from one person exercises none of it. A hosted provider generally
/// refuses to submit a message whose author is not the authenticated account, and against one of those the whole batch
/// would fail for a reason that has nothing to do with this tool — so the account configures
/// <see cref="SendingAccount" /> instead and keeps the invented participants in <c>Reply-To</c> and <c>Cc</c>.
/// </remarks>
internal enum SyntheticAuthorIdentity
{
    /// <summary>An invented participant under the reserved domain, with the sending account named in <c>Sender</c>.</summary>
    Fabricated = 0,

    /// <summary>The configured sending account, with the invented participant named in <c>Reply-To</c>.</summary>
    SendingAccount = 1,
}
