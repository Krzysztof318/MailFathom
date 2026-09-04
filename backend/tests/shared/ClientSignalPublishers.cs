// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Signals;

namespace MailFathom.TestSupport;

/// <summary>The publisher a test hands to something that raises signals when the signals are not what it is proving.</summary>
public static class ClientSignalPublishers
{
    /// <summary>Gets a publisher with no channel registered, which folds nothing, starts no timer, and holds no state.</summary>
    /// <remarks>
    /// One instance is shared by every test in the process and needs no disposal, which is what makes it the right
    /// shape for the many call sites that construct a collaborator only to exercise something else about it. A test
    /// whose subject <em>is</em> what was signalled registers a recording channel of its own and builds a
    /// publisher around it instead.
    /// </remarks>
    public static ClientSignals ReachingNobody { get; } = new([], TimeProvider.System);
}
