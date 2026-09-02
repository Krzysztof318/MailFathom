// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.UnitTests.TestDoubles;

/// <summary>A terminal that remembers what was written to each of its two streams.</summary>
/// <remarks>
/// The split matters to the assertions rather than only to the implementation: the corpus goes to one stream and
/// everything the run says about itself to the other, which is what lets a test check that a dry run's output is
/// comparable between two invocations of one seed.
/// </remarks>
internal sealed class RecordingSyntheticMailConsole : ISyntheticMailConsole
{
    /// <summary>What the command wrote as its output: one line per generated message.</summary>
    internal List<string> Output { get; } = [];

    /// <summary>What the command said about the run itself.</summary>
    internal List<string> Diagnostics { get; } = [];

    /// <inheritdoc />
    public void WriteLine(string message) => this.Output.Add(message);

    /// <inheritdoc />
    public void WriteError(string message) => this.Diagnostics.Add(message);
}
