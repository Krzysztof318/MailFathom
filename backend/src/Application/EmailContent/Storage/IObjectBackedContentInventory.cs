// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>Reports whether this deployment holds any payload in the object backend.</summary>
/// <remarks>
/// <para>
/// One question, asked for one decision, which is why the port carries nothing else. A deployment that names no
/// object-storage endpoint can still hold rows pointing into one — it was configured for it once, and the
/// configuration was taken away or lost — and every one of those messages is unreadable until the endpoint comes
/// back. Nothing else notices: the row is intact, the metadata answers, and the failure arrives only when somebody
/// asks for the content of that particular message.
/// </para>
/// <para>
/// It is deliberately a presence rather than a count. What an operator has to act on is that such rows exist at all,
/// and counting them would mean four aggregates over four tables on every readiness scrape to refine an answer that
/// changes nothing about what to do.
/// </para>
/// </remarks>
public interface IObjectBackedContentInventory
{
    /// <summary>Gets whether any stored payload names the object backend as the store holding it.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true" /> when at least one payload of any kind is held in object storage.</returns>
    /// <remarks>
    /// The read reaches a database that may not carry the schema this build expects, because a readiness scrape can
    /// arrive before the startup gate has proven one. An implementation lets that failure surface rather than
    /// answering <see langword="false" /> for it: a database nothing can be read from is not a deployment holding no
    /// object-backed content, and reporting the two alike would hide the first behind the second.
    /// </remarks>
    Task<bool> HoldsObjectBackedContentAsync(CancellationToken cancellationToken);
}
