// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Spam.Gating;

/// <summary>Reports what the gate decided, in counts alone.</summary>
/// <remarks>
/// <para>
/// Withholding is invisible by construction: the work is never started, so without an instrument a gate that withheld
/// everything and a mailbox that produced nothing look the same. What an operator has to be able to tell apart is a
/// message held because a verdict called it junk, a message held because no verdict has arrived yet, and a message let
/// through without one — the last of which is the deployment saying its scanner is not answering rather than quietly
/// indexing spam.
/// </para>
/// <para>
/// <b>Nothing recorded here is mail or derived from it.</b> The one tag is an admission, which is MailFathom's own
/// closed set, and the values are counts. No message identity, folder, verdict score, or address reaches an instrument
/// from the gate.
/// </para>
/// </remarks>
public interface IDerivedWorkGateTelemetry
{
    /// <summary>Records one occurrence's admission at a point where it was decided individually.</summary>
    /// <param name="admission">What the gate concluded about it.</param>
    /// <remarks>
    /// Recorded per decision rather than per message, so a message a pass meets and withholds again reports again. That
    /// is deliberate: a sustained rate of withheld decisions is what a growing classification backlog looks like, and a
    /// count that fired once per message would fall silent exactly while the problem lasted.
    /// </remarks>
    void RecordAdmission(DerivedWorkAdmission admission);

    /// <summary>Records the passages removed from one message a classification has just called junk.</summary>
    /// <param name="passageCount">How many passages, and the vectors hanging off them, the removal reached.</param>
    /// <remarks>
    /// A number above zero here is mail that was chunked and embedded before anybody scored it, which is the one case
    /// the ordering cannot prevent. A deployment whose classification has caught up records zeroes and nothing else.
    /// </remarks>
    void RecordDiscardedPassages(int passageCount);
}
