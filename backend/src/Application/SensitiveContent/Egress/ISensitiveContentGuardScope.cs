// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.SensitiveContent.Egress;

/// <summary>Holds one guarded operation's report open while the values it publishes are being scanned.</summary>
/// <remarks>
/// <para>
/// An operation rather than a value, because a value is not what anybody waits for: one read of a message scans a body
/// representation, a subject, and a display name, and the number a reader felt is the sum of them. A span apiece would
/// report each of those as fast while the operation they compose stayed slow.
/// </para>
/// <para>
/// What reaches it is a count, an ending, and the egress point — never a finding, a category's match, a position, or
/// any part of the text, for the reason the instruments beside it carry none of those: a span store is the last place
/// the credential a scan removed should be written down.
/// </para>
/// </remarks>
public interface ISensitiveContentGuardScope : IDisposable
{
    /// <summary>Records one more text scanned inside this operation.</summary>
    void TextGuarded();

    /// <summary>Records that a scanner could not answer, which refuses the operation rather than serving it unscanned.</summary>
    void Refused();

    /// <summary>Records that the operation guarded everything it was going to, which is what separates it from one that stopped.</summary>
    /// <remarks>
    /// A scan reaches its consumer as an exception rather than as a result — a refusal, a cancelled shutdown, a
    /// scanner that faulted — so the values scanned so far say nothing about whether the operation finished. Without
    /// this the count of an interrupted operation would be published as an operation that succeeded with fewer texts,
    /// which is the reading an operator would use to rule the scanner out.
    /// </remarks>
    void Completed();
}
