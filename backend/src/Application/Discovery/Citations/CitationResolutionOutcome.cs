// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Discovery.Citations;

/// <summary>What became of one citation a caller asked to follow.</summary>
/// <remarks>
/// <para>
/// Three answers and no fourth, because the two that are not a resolution are states rather than errors. A reader shown
/// a fact whose source cannot be opened is being told something about the source, and answering either of them with a
/// failure would make the whole request fail over one citation — which is the shape that turns a checkable answer into
/// an unusable one.
/// </para>
/// <para>
/// <see cref="PrivateSource" /> and <see cref="Unresolvable" /> are separated by what the caller may do next, which is
/// the only distinction worth publishing: a private source is somebody else's mail and stays private however long the
/// caller waits, while an unresolvable place is a message this caller can open with the citation's refinement gone.
/// </para>
/// </remarks>
public enum CitationResolutionOutcome
{
    /// <summary>The citation was followed to the place it names.</summary>
    Resolved = 0,

    /// <summary>The citation could not be followed to the place it names, which a re-cut passage, a message whose parts have changed, and a damaged local copy all produce.</summary>
    Unresolvable = 1,

    /// <summary>The caller may not read the source, which is the answer for mail belonging to somebody else and for mail this deployment does not hold.</summary>
    PrivateSource = 2,
}
