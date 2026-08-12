// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Spam.Actions;

/// <summary>Reads where a classified email currently is and whether its mail server already reports it read.</summary>
/// <remarks>
/// A port of its own rather than a widening of <see cref="IClassifiableEmailReader" />, because the two are asked at
/// different moments and about different things: that one is asked before a verdict exists and answers what the verdict
/// is judged from, and this one is asked after and answers what a change would be issued against. Widening the first
/// would put a remote occurrence and a flag into every classification that never touches a mailbox.
/// </remarks>
public interface ISpamActionOccurrenceReader
{
    /// <summary>Finds the occurrence one local email currently has.</summary>
    /// <param name="emailId">The local email a classification was recorded for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Where the email is and how it stands, or <see langword="null" /> when nothing is stored under that identifier.</returns>
    Task<SpamActionOccurrence?> FindAsync(StoredEmailId emailId, CancellationToken cancellationToken);
}
