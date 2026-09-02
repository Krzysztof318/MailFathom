// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Common.OAuth;

/// <summary>The waits RFC 8628 imposes on a client polling a token endpoint for a device grant.</summary>
/// <remarks>
/// Stated once rather than beside each caller, because these are the specification's numbers rather than either
/// caller's preference: a client that polls faster than it was told is throttled or blocked outright, so a value that
/// drifted in one copy would be a protocol defect in that copy alone and nothing would say which copy was right.
/// </remarks>
public static class DeviceCodePolling
{
    /// <summary>The interval RFC 8628 mandates when the device authorization response states none.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);

    /// <summary>The extra wait RFC 8628 requires after the authorization server answers <c>slow_down</c>.</summary>
    /// <remarks>The interval grows permanently rather than for one iteration, which is what the specification asks for and what a server answering <c>slow_down</c> twice would otherwise punish.</remarks>
    public static readonly TimeSpan BackoffIncrement = TimeSpan.FromSeconds(5);
}
