// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Spam;

/// <summary>Finds the account and folder of one stored occurrence, which is all classification needs beside the content.</summary>
/// <remarks>
/// A port of its own rather than a reuse of a mailbox read, because those apply the visibility a caller is entitled to
/// and classification is not a caller: it runs over stored mail on the operator's behalf, including over a folder no
/// tool may read. Reading it through a listing would silently leave such a folder unclassified.
/// </remarks>
public interface IClassifiableEmailReader
{
    /// <summary>Finds one stored occurrence.</summary>
    /// <param name="emailId">The stable local identifier.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The occurrence, or <see langword="null" /> when nothing is stored under that identifier.</returns>
    /// <remarks>
    /// An absent occurrence is an ordinary answer: mail can be expunged between the moment classification was asked for
    /// and the moment it runs, and that is the message leaving rather than a failure to report.
    /// </remarks>
    Task<ClassifiableEmail?> FindAsync(StoredEmailId emailId, CancellationToken cancellationToken);
}
