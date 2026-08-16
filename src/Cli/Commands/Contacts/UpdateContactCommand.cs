// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration.Contacts;

namespace MailFathom.Cli.Commands.Contacts;

/// <summary>Corrects what the deployment's book holds about one person.</summary>
/// <remarks>
/// <para>
/// What is not named is kept. The book itself takes the whole record rather than the difference, so this command reads
/// the contact, replaces the parts the operator named, and sends back what it is to become — which is what lets a single
/// invocation correct a name without restating every address.
/// </para>
/// <para>
/// A collected contact is refused here rather than amended, because it is a record the deployment wrote from arriving
/// mail. <c>contact promote</c> is the act that makes it the owner's, and the refusal says so.
/// </para>
/// </remarks>
internal static class UpdateContactCommand
{
    /// <summary>Builds the <c>contact update</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var identityOption = ContactOptions.Identity();

        Option<string?> nameOption = new("--name")
        {
            Description = "The name to record instead. Kept as it is when this is left out.",
        };

        Option<string[]> addressOption = new("--address")
        {
            Description =
                "The addresses the person is to hold afterwards, replacing every one they hold now. Repeat it for each. Kept as they are when this is left out.",
            AllowMultipleArgumentsPerToken = false,
        };

        Option<string?> preferredOption = new("--preferred")
        {
            Description = "The address to use by default afterwards, which is one of the addresses the record holds.",
        };

        Option<string?> noteOption = new("--note")
        {
            Description = "What to record about the person instead. Kept as it is when this is left out.",
        };

        Option<bool> clearNoteOption = new("--clear-note")
        {
            Description = "Hold no note about the person afterwards.",
        };

        Command command = new("update", "Correct what the deployment's book holds about one person.")
        {
            identityOption,
            nameOption,
            addressOption,
            preferredOption,
            noteOption,
            clearNoteOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(identityOption),
            new ContactCorrection(
                result.GetValue(nameOption),
                result.GetValue(addressOption) ?? [],
                result.GetValue(preferredOption),
                result.GetValue(noteOption),
                result.GetValue(clearNoteOption)),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption)),
            cancellationToken));

        return command;
    }

    private static Task<int> RunAsync(
        CliContext context,
        Guid contactId,
        ContactCorrection correction,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        correction.RefuseWhereItSaysNothing();

        return ContactRecordEdit.AmendAsync(
            context,
            contactId,
            requestedDeployment,
            correction.ApplyTo,
            "Corrected",
            cancellationToken);
    }

    /// <summary>What the operator asked to change about one contact, and how it is applied to the record held.</summary>
    /// <param name="DisplayName">The name to record instead, or <see langword="null" /> to keep the one held.</param>
    /// <param name="Addresses">The addresses to hold afterwards, or empty to keep the ones held.</param>
    /// <param name="PreferredAddress">The address to prefer afterwards, or <see langword="null" /> to keep the one held where it survives.</param>
    /// <param name="Note">The note to record instead, or <see langword="null" /> to keep the one held.</param>
    /// <param name="ClearsNote">Whether the contact is to hold no note afterwards.</param>
    private sealed record ContactCorrection(
        string? DisplayName,
        IReadOnlyList<string> Addresses,
        string? PreferredAddress,
        string? Note,
        bool ClearsNote)
    {
        /// <summary>Refuses an invocation that changes nothing, or one that asks for two opposite things.</summary>
        /// <exception cref="CliFailure">Thrown when the invocation names no change, or names a note and asks to clear one.</exception>
        /// <remarks>
        /// Both are refused before the contact is read rather than after, so an operator who mistyped an option does not
        /// send a write that restates the record they already have.
        /// </remarks>
        internal void RefuseWhereItSaysNothing()
        {
            if (this.Note is { Length: > 0 } && this.ClearsNote)
            {
                throw new CliFailure("A note is either recorded or cleared. Pass --note or --clear-note, not both.");
            }

            if (this.DisplayName is null
                && this.Addresses.Count == 0
                && this.PreferredAddress is null
                && this.Note is null
                && !this.ClearsNote)
            {
                throw new CliFailure(
                    "The invocation names nothing to change. Pass --name, --address, --preferred, --note, or --clear-note.");
            }
        }

        /// <summary>Produces the record the contact is to have, from the one the book holds.</summary>
        /// <param name="held">The contact as the book holds it.</param>
        /// <returns>The record to send.</returns>
        /// <exception cref="CliFailure">Thrown when replacing the addresses leaves no preferred address the operator chose.</exception>
        internal ContactRecordRequest ApplyTo(ContactRecord held)
        {
            var addresses = this.Addresses.Count > 0 ? this.Addresses : ContactRecordEdit.AddressesOf(held);
            var note = this.ClearsNote ? null : this.Note ?? held.Note;

            return new ContactRecordRequest(
                this.DisplayName ?? held.DisplayName ?? string.Empty,
                addresses,
                this.ChoosePreferred(addresses, held),
                note);
        }

        /// <summary>Reports which address the record is to prefer once the change is applied.</summary>
        /// <remarks>
        /// Naming one settles it. Otherwise the one the contact already prefers is kept where the record still holds it,
        /// which is what makes correcting a name leave the rest alone; and a replacement that drops the preferred address
        /// without naming a new one is refused rather than resolved, because which address a message to that person goes
        /// to is the operator's decision rather than an ordering accident.
        /// </remarks>
        private string ChoosePreferred(IReadOnlyList<string> addresses, ContactRecord held)
        {
            if (this.PreferredAddress is { Length: > 0 } named)
            {
                return named;
            }

            if (held.PreferredAddress is { Length: > 0 } kept && ContactRecordEdit.Holds(addresses, kept))
            {
                return kept;
            }

            throw new CliFailure(
                "The addresses given do not include the one the contact prefers, so the record has to say which of them it prefers instead. Pass --preferred naming one of them.");
        }
    }
}
