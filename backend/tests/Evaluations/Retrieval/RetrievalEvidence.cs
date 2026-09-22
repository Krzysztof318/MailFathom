// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Evaluations.Retrieval;

/// <summary>One piece of evidence a question rests on, and every message of the mailbox that carries it.</summary>
/// <param name="Description">What the evidence is, in words a failed run can be read by.</param>
/// <param name="Messages">The messages carrying it; ranking any one of them reaches it.</param>
internal sealed record RetrievalEvidence(string Description, IReadOnlySet<StoredEmailId> Messages);
