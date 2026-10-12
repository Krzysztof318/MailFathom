// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Host.Configuration.Policies;

/// <summary>What judging a change to a governed record against its policies answered.</summary>
/// <param name="Json">The document to commit where nothing was refused.</param>
/// <param name="Refusals">One sentence per held property the change would have moved, empty where the change may stand.</param>
internal sealed record GovernedRecordChangeJudgement(string Json, IReadOnlyList<string> Refusals);
