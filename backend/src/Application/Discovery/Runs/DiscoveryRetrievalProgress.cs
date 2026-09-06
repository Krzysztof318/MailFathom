// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Discovery.Runs;

/// <summary>How far a retrieval plan has got, as one of its lookups finishes.</summary>
/// <param name="LookupsRun">How many of the plan's lookups have run so far.</param>
/// <param name="LookupsRefused">How many the deployment refused because a filter on them was not usable.</param>
/// <param name="LookupsPlanned">How many the plan holds altogether, which is what the two counts above are read against.</param>
/// <param name="PassagesFound">How many distinct passages the run may answer from so far.</param>
/// <remarks>
/// Four bounded counts and nothing else. A progress report travels to a client while a question is still being answered,
/// so it carries nothing about the mail that was found — not a subject, not an extract, not an identifier — and stays
/// readable as *this is working and here is how far it got*.
/// </remarks>
public sealed record DiscoveryRetrievalProgress(
    int LookupsRun,
    int LookupsRefused,
    int LookupsPlanned,
    int PassagesFound);
