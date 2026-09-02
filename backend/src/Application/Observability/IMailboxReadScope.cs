// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Observability;

/// <summary>Holds one local read's report open for as long as the read is running.</summary>
/// <remarks>
/// The report is open <em>around</em> the read rather than written after it, so the persistence and content-store work
/// the read causes is reported beneath it. A read that never reports what it returned is reported as one that did not
/// finish, which is what makes a refusal, a cancellation, and a fault visible without the use case catching anything.
/// </remarks>
public interface IMailboxReadScope : IDisposable
{
    /// <summary>Records that the read produced an answer, and how much of one.</summary>
    /// <param name="resultCount">How many accounts, summaries, matches, or emails the read is returning.</param>
    /// <remarks>
    /// Called once, with what the caller actually receives. An empty answer is a completed read rather than an
    /// unfinished one: matching nothing is a normal result everywhere on this surface.
    /// </remarks>
    void Completed(int resultCount);
}
