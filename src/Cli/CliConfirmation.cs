// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli;

/// <summary>The one rule every command that does something irreversible asks its question under.</summary>
/// <remarks>
/// Written once because the rule is one rule: the flag states the agreement, an invocation with nobody at the terminal
/// is refused rather than guessed at, and everything else is asked. Each command that had its own copy could have
/// answered the middle case differently, and a redirected input answered from whatever was piped in would turn a stray
/// line into an agreement to spend money or to erase somebody.
/// </remarks>
internal static class CliConfirmation
{
    /// <summary>Reports whether the person running this agreed to what the command is about to do.</summary>
    /// <param name="context">What the command needs from its surroundings, which is where the terminal is reached.</param>
    /// <param name="confirmedUpFront">Whether the operator stated the agreement in the command with <c>--yes</c>.</param>
    /// <param name="unattendedRefusal">What to tell an invocation with nobody at the terminal, naming what it would have done and the flag that states the agreement.</param>
    /// <param name="question">The question to put to the operator, ending in the accepted answers.</param>
    /// <returns><see langword="true" /> when the command may go ahead.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when nothing was stated in the command and there is nobody to ask.</exception>
    internal static bool Agreed(CliContext context, bool confirmedUpFront, string unattendedRefusal, string question)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (confirmedUpFront)
        {
            return true;
        }

        if (!context.Console.CanConfirm)
        {
            throw new CliFailure(unattendedRefusal);
        }

        return context.Console.Confirm(question);
    }
}
