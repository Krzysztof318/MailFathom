// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Output;

/// <summary>What a line says about itself beyond the words in it.</summary>
/// <remarks>
/// The whole set colour is allowed to mark. A line is ordinary unless it reports a state the operator would otherwise
/// have to read the words to notice, which is a failure and a caution and nothing else: guidance, a prompt, and a
/// result are all ordinary however important they are, and marking one of them would leave the marks meaning nothing.
/// </remarks>
internal enum CliEmphasis
{
    /// <summary>A line that reports no state of its own.</summary>
    None = 0,

    /// <summary>A line reporting something the operator has to weigh before going on.</summary>
    Caution = 1,

    /// <summary>A line reporting that what was asked for did not happen.</summary>
    Failure = 2,
}
