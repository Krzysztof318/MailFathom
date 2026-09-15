// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Export;

/// <summary>One folder as an export sees it: what an operator names it by, and what the archive lays it out from.</summary>
/// <param name="Path">The folder's path in the words an operator reads and writes, segments joined by <c>/</c>.</param>
/// <param name="Segments">The same path as its own segments, outermost first, exactly as the account holds them.</param>
/// <param name="IsInbox">Whether this is the account's inbox, which the Maildir++ layout writes at the archive root.</param>
/// <remarks>
/// <para>
/// One shape for both kinds of account. A held account's folders are MailFathom's own hierarchy and their segments are
/// that hierarchy's; a mirrored account's are the aliases configuration gave its mapped folders, which have one segment
/// each. Neither the source's own path nor its delimiter reaches the archive, because a remote path is a fact about a
/// server the archive is being carried away from.
/// </para>
/// <para>
/// <see cref="Path" /> is what a caller names to export one folder, and what the measurement reports each row under, so
/// the two cannot disagree about which folder was meant.
/// </para>
/// </remarks>
public sealed record MailboxExportFolder(string Path, IReadOnlyList<string> Segments, bool IsInbox);
