// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Delivery;

namespace MailFathom.SyntheticMail.Corpus;

/// <summary>One corpus read back off disk, ready to be delivered and generating nothing.</summary>
/// <param name="Invocation">The invocation that produced it, which a replay reports rather than acts on.</param>
/// <param name="Exchanges">The exchanges, each oldest turn first and each alternating from the correspondent onwards.</param>
/// <remarks>
/// The messages are held rather than opened one at a time, which is the opposite of what generation does and is right
/// for the same reason: a generated batch is bounded by a count and an attachment ceiling that reach into gigabytes,
/// and a corpus is a file whose size somebody chose when they committed it. Reading it once means a replay that
/// started cannot fail halfway through because the archive moved underneath it.
/// </remarks>
internal sealed record ExportedCorpus(string Invocation, IReadOnlyList<IReadOnlyList<DeliverableTurn>> Exchanges);
