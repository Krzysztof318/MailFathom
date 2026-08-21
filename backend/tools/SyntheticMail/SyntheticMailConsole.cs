// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail;

/// <summary>The terminal the command reports to.</summary>
/// <remarks>
/// Two members and no more. <c>mfctl</c>'s console also reads a secret and asks a confirming question; neither belongs
/// here, because this command takes its credential from a local file and has nothing to ask — a batch bound for a
/// throwaway mailbox weakens no protection somebody has to agree to.
/// </remarks>
internal interface ISyntheticMailConsole
{
    /// <summary>Writes one generated message, which is the command's machine-readable output.</summary>
    /// <param name="message">The line.</param>
    void WriteLine(string message);

    /// <summary>Writes a line about the run itself: what it is doing, what it delivered, and what failed.</summary>
    /// <param name="message">The line.</param>
    void WriteError(string message);
}

/// <summary>The terminal the command actually runs against.</summary>
/// <remarks>
/// The generated corpus goes to standard output and everything the run says about itself to standard error. That split
/// is what makes two runs of one seed comparable with an ordinary <c>diff</c>: the reported seed and the delivery
/// counts differ between runs by design, and mixing them into the captured stream would drown the thing being compared.
/// </remarks>
internal sealed class SystemSyntheticMailConsole : ISyntheticMailConsole
{
    /// <inheritdoc />
    public void WriteLine(string message) => Console.Out.WriteLine(message);

    /// <inheritdoc />
    public void WriteError(string message) => Console.Error.WriteLine(message);
}
