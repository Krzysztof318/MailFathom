// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Search.Phrasing;

/// <summary>Reads a typed sentence into the constraints it states and the words it leaves to rank by.</summary>
/// <remarks>
/// <para>
/// The port a search screen reaches the model through, and the whole of what a provider decides about a search. It
/// answers with a reading rather than with results, so nothing provider-shaped travels beyond this boundary and the
/// search that follows is the same search a person could have built by hand out of the same filters.
/// </para>
/// <para>
/// It is registered only where the deployment declared a chat endpoint and left this on, so a caller resolves it
/// optionally and a deployment without one offers the plain word search rather than a field that fails. That is a
/// registration rather than a branch inside a call: there is no path by which a sentence leaves an instance whose
/// operator did not ask for this.
/// </para>
/// </remarks>
public interface IMailSearchPhraseReader
{
    /// <summary>Reads one sentence, answering with what a search made of it.</summary>
    /// <param name="phrase">The sentence and the day it was typed on.</param>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <returns>The reading, or <see cref="MailSearchPhraseReading.Nothing" /> where no reading could be made.</returns>
    /// <remarks>
    /// A provider that failed or answered unreadably is not published as a failure: the caller already has a usable
    /// search for that case, which is the words the person typed. A refusal travels only where the deployment's own
    /// spend ceiling declined the call, because falling back there would spend the search on a call the operator had
    /// already said no to.
    /// </remarks>
    Task<MailSearchPhraseReading> ReadAsync(MailSearchPhrase phrase, CancellationToken cancellationToken);
}
