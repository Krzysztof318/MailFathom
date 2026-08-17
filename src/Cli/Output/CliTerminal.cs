// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Output;

/// <summary>What the stream a command is writing to will accept.</summary>
/// <param name="PermitsColour">Whether an escape sequence may be written at all.</param>
/// <param name="Width">How many columns the drawing may use.</param>
/// <remarks>
/// <para>
/// Colour is for the person at the terminal and for nobody else, so it is withheld from everything that is not one: a
/// redirected stream, and a run whose environment sets <c>NO_COLOR</c>. Both produce the bytes a script parses, which is
/// the contract that predates any of this drawing and is not weakened by it.
/// </para>
/// <para>
/// The width is why a redirected stream needs an answer of its own rather than only a colour decision. A drawing sized
/// to a guessed terminal would wrap a captured listing at some column nobody chose; a width nothing reaches makes the
/// wrap impossible instead, so a piped row stays one line however long it is.
/// </para>
/// </remarks>
internal sealed record CliTerminal(bool PermitsColour, int Width)
{
    /// <summary>The width a drawing is laid out in when nothing is watching it, chosen to be past any line this command writes.</summary>
    internal const int WidthWhenRedirected = 512;

    /// <summary>The width a drawing falls back to when the platform reports none, which is the conventional terminal.</summary>
    internal const int WidthWhenUnreported = 80;

    /// <summary>Decides what a stream accepts, from what the platform reports about it.</summary>
    /// <param name="redirected">Whether the stream is redirected rather than attached to a terminal.</param>
    /// <param name="refusedColour">The <c>NO_COLOR</c> setting, or <see langword="null" /> when it is unset.</param>
    /// <param name="reportedWidth">The terminal width the platform reports, or a value of zero or less when it reports none.</param>
    /// <returns>What the stream accepts.</returns>
    /// <remarks>
    /// <c>NO_COLOR</c> is honoured on presence with any non-empty value, which is what the convention specifies: a run
    /// that sets it to <c>0</c> is asking for no colour, the same as one that sets it to <c>1</c>.
    /// </remarks>
    internal static CliTerminal Decide(bool redirected, string? refusedColour, int reportedWidth)
    {
        if (redirected)
        {
            return new CliTerminal(PermitsColour: false, WidthWhenRedirected);
        }

        var width = reportedWidth > 0 ? reportedWidth : WidthWhenUnreported;

        return new CliTerminal(refusedColour is not { Length: > 0 }, width);
    }

    /// <summary>Decides what standard output accepts.</summary>
    /// <returns>What the stream accepts.</returns>
    internal static CliTerminal ForStandardOutput() => Decide(Console.IsOutputRedirected, RefusedColour(), ReportedWidth());

    /// <summary>Decides what standard error accepts.</summary>
    /// <returns>What the stream accepts.</returns>
    internal static CliTerminal ForStandardError() => Decide(Console.IsErrorRedirected, RefusedColour(), ReportedWidth());

    private static string? RefusedColour() => Environment.GetEnvironmentVariable("NO_COLOR");

    /// <summary>Reads the terminal width, treating a platform that will not answer as one that reports none.</summary>
    /// <remarks>
    /// The property throws where no console is attached, which is every way this command is run without a terminal —
    /// a service, a container, a continuous-integration step — and none of those is a failure worth ending a command on.
    /// </remarks>
    private static int ReportedWidth()
    {
        try
        {
            return Console.WindowWidth;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (PlatformNotSupportedException)
        {
            return 0;
        }
    }
}
