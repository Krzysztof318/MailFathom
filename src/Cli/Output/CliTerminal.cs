// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Output;

/// <summary>What the stream a command is writing to will accept.</summary>
/// <param name="PermitsColour">Whether an escape sequence may be written at all.</param>
/// <remarks>
/// Colour is for the person at the terminal and for nobody else, so it is withheld from everything that is not one: a
/// redirected stream, and a run whose environment sets <c>NO_COLOR</c>. Both produce the bytes a script parses, which is
/// the contract that predates any of this drawing and is not weakened by it.
/// </remarks>
internal sealed record CliTerminal(bool PermitsColour)
{
    /// <summary>Decides what a stream accepts, from what the platform reports about it.</summary>
    /// <param name="redirected">Whether the stream is redirected rather than attached to a terminal.</param>
    /// <param name="refusedColour">The <c>NO_COLOR</c> setting, or <see langword="null" /> when it is unset.</param>
    /// <returns>What the stream accepts.</returns>
    /// <remarks>
    /// <c>NO_COLOR</c> is honoured on presence with any non-empty value, which is what the convention specifies: a run
    /// that sets it to <c>0</c> is asking for no colour, the same as one that sets it to <c>1</c>.
    /// </remarks>
    internal static CliTerminal Decide(bool redirected, string? refusedColour) =>
        redirected
            ? new CliTerminal(PermitsColour: false)
            : new CliTerminal(refusedColour is not { Length: > 0 });

    /// <summary>Decides what standard output accepts.</summary>
    /// <returns>What the stream accepts.</returns>
    internal static CliTerminal ForStandardOutput() => Decide(Console.IsOutputRedirected, RefusedColour());

    /// <summary>Decides what standard error accepts.</summary>
    /// <returns>What the stream accepts.</returns>
    internal static CliTerminal ForStandardError() => Decide(Console.IsErrorRedirected, RefusedColour());

    private static string? RefusedColour() => Environment.GetEnvironmentVariable("NO_COLOR");
}
