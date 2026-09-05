// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.AI.Orchestration;

/// <summary>Wraps every operation's instruction in nothing, which is what this build ships.</summary>
/// <remarks>
/// The registered default, and the thing that makes the seam free: a composition over this produces the operation's own
/// instruction byte for byte, so no run pays for a wrapper nobody has written yet.
/// </remarks>
internal sealed class EmptyAgentInstructionEnvelope : IAgentInstructionEnvelope
{
    /// <inheritdoc />
    public string Preamble => string.Empty;

    /// <inheritdoc />
    public string Postamble => string.Empty;
}
