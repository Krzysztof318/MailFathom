// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs;

/// <summary>States whether repeating the work that failed could succeed without anything else changing first.</summary>
/// <remarks>
/// <para>
/// The classification is what decides between another attempt and a dead letter, so it is answered before the attempt
/// budget is consulted rather than after it: a permanent failure ends the job on its first attempt instead of spending
/// the budget a fixed number of times to reach the answer it already had.
/// </para>
/// <para>
/// It is stored on the row as its name, like every other bounded value in this schema, so an operator asking what is
/// stuck reads the verdict beside the reason for it.
/// </para>
/// </remarks>
public enum JobFailureClassification
{
    /// <summary>The failure is expected to clear on its own, so the same work is worth attempting again later.</summary>
    Transient = 0,

    /// <summary>Nothing about repeating the work can change the answer; somebody has to act before it could succeed.</summary>
    Permanent = 1,
}
