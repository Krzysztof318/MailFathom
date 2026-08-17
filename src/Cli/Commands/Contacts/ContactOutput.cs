// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Administration.Contacts;

namespace MailFathom.Cli.Commands.Contacts;

/// <summary>Writes what the contact commands print, so every one of them prints a person the same way.</summary>
/// <remarks>
/// <para>
/// A contact printed to a terminal is personal data on somebody's screen and in their shell's scrollback, so what
/// reaches it is what the operator asked for and nothing beside it: a command that acted on one person prints that
/// person, a listing prints one line each, and neither prints anybody a request did not name.
/// </para>
/// <para>
/// Nothing here writes to standard error. A refusal names the outcome and the contact's identifier, which is the one
/// part of the record that is not personal data — a name or an address in a failure message would end up in a log
/// wherever the command is run from a script.
/// </para>
/// </remarks>
internal static class ContactOutput
{
    /// <summary>Prints one contact in full.</summary>
    /// <param name="console">The terminal to write to.</param>
    /// <param name="contact">The contact to print.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The preferred address is marked where it sits rather than printed a second time, because it is one of the
    /// addresses rather than a value of its own — and the deployment already serves it first.
    /// </remarks>
    internal static void WriteContact(ICliConsole console, ContactRecord contact)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(contact);

        console.WriteLine($"Contact:   {contact.Id:D}");
        console.WriteLine($"Name:      {contact.DisplayName}");
        console.WriteLine($"Origin:    {contact.Origin}");

        var addresses = contact.Addresses ?? [];

        if (addresses.Count == 0)
        {
            console.WriteLine("Addresses: none reported");
        }

        foreach (var (address, position) in addresses.Select((address, position) => (address, position)))
        {
            var label = position == 0 ? "Addresses:" : "          ";
            var preferred = string.Equals(address, contact.PreferredAddress, StringComparison.Ordinal)
                ? "  (preferred)"
                : string.Empty;

            console.WriteLine($"{label} {address}{preferred}");
        }

        if (contact.Note is { Length: > 0 } note)
        {
            console.WriteLine($"Note:      {note}");
        }

        console.WriteLine($"Recorded:  {contact.RecordedAt:u}");
        console.WriteLine($"Amended:   {contact.AmendedAt:u}");
    }

    /// <summary>Prints one contact as a line of a listing.</summary>
    /// <param name="console">The terminal to write to.</param>
    /// <param name="contact">The contact to print.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The preferred address alone rather than every address a person uses. A listing answers who is in the book, and a
    /// page that unfolded every address would put most of the book's contents on a screen the operator was scanning.
    /// </remarks>
    internal static void WriteSummary(ICliConsole console, ContactRecord contact)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(contact);

        console.WriteLine($"{contact.Id:D}  {contact.DisplayName} <{contact.PreferredAddress}>  ({contact.Origin})");
    }

    /// <summary>Reports what one write to the book produced, printing the record it wrote or the reason it did not.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <param name="answer">What the deployment answered.</param>
    /// <param name="performed">What the command did, as a sentence's opening, such as <c>Recorded</c>.</param>
    /// <returns>The exit code the command ends with.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// A refusal ends the command with a failing code and a sentence on standard error, which is what every other
    /// command here does when it did not do what it was asked: a caller that redirected the output reads an empty result
    /// and a reason rather than a sentence about nothing having happened mixed into what it captured.
    /// </remarks>
    internal static int ReportWrite(CliContext context, ContactWriteAnswer answer, string performed)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(answer);

        if (!answer.WasWritten())
        {
            context.Console.WriteError(answer.DescribeRefusal());

            return CliExitCode.Failure;
        }

        if (answer.Contact is not { } written)
        {
            context.Console.WriteError(
                "The deployment reported the write as performed but answered with no record, which no operation here sends.");

            return CliExitCode.Failure;
        }

        context.Console.WriteLine($"{performed} contact {written.Id:D}.");
        WriteContact(context.Console, written);

        return CliExitCode.Success;
    }
}
