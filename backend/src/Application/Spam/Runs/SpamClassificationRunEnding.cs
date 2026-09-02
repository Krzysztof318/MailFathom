// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Spam.Runs;

/// <summary>How a whole-mailbox classification run stopped being outstanding.</summary>
/// <remarks>
/// None of these is a failure, and each names a different thing for an operator to do: nothing, ask again, or switch
/// classification back on first. A run that a process crash interrupted has no ending at all — it is still outstanding,
/// and the next account run carries it from the batch the last one committed.
/// </remarks>
public enum SpamClassificationRunEnding
{
    /// <summary>The run reached the end of the mail its scope names.</summary>
    Completed = 0,

    /// <summary>The settings a verdict is reached under changed while the run was outstanding.</summary>
    /// <remarks>
    /// A run that has scored half a mailbox under one profile cannot finish the other half under another: the two halves
    /// would not be comparable and the record could not say which terms the run applied. Ending it is what leaves the
    /// operator with an honest record and one thing to do, which is ask for the run again under the settings now in
    /// force.
    /// </remarks>
    Superseded = 1,

    /// <summary>Classification was switched off while the run was outstanding.</summary>
    /// <remarks>
    /// Held apart from a completed run because the mail was not looked at rather than found to be clean. Walking on
    /// would classify nothing message after message and report a finished run over a mailbox nothing was decided about.
    /// </remarks>
    Disabled = 2,
}
