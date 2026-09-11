// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Cleaning;

/// <summary>Proposes which blocks of one reduced body a reader is shown.</summary>
/// <remarks>
/// <para>
/// The port is asked with an outline and answers with ranges, which is the whole of the contract: nothing crossing it
/// carries message text in either direction, so an implementation has no way to return words a sender did not write.
/// </para>
/// <para>
/// Registered in both of its states rather than in one, for the reason every derivation port here is: a deployment that
/// declared no chat endpoint still has to say *why* a body was not cleaned, and a missing registration would have made
/// every caller decide for itself what an absence meant.
/// </para>
/// </remarks>
public interface IMailBodyCleaner
{
    /// <summary>Gets whether this deployment cleans a body at all, which is a registration rather than a call.</summary>
    bool IsActive { get; }

    /// <summary>Proposes which blocks of one body to keep.</summary>
    /// <param name="body">The outline of the reduced body, which is the whole of what may be sent.</param>
    /// <param name="cancellationToken">Cancels the proposal when the reader stops waiting for it.</param>
    /// <returns>The ranges a producer wrote, or why nothing was proposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// It answers rather than raising: every reason a proposal does not arrive is one the caller already has an outcome
    /// for, and the reader is shown the ordinary reduced body in each of them. Cancellation stays outside, being the
    /// reader withdrawing the wait rather than a producer failing to answer.
    /// </remarks>
    Task<MailBodyCleaningProposal> ProposeAsync(CleanableMailBody body, CancellationToken cancellationToken);
}
