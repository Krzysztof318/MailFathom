// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli.Commands.Contacts;

/// <summary>The options the contact commands share.</summary>
/// <remarks>
/// A contact is named by the identity the book gave it rather than by an address, because an address is a thing a person
/// has rather than a thing they are: they give one up and gain another and stay the same contact. The one command that
/// takes an address instead answers "who is this from", and it answers with a person.
/// </remarks>
internal static class ContactOptions
{
    /// <summary>Builds the option naming which contact a command acts on.</summary>
    /// <returns>The option.</returns>
    internal static Option<Guid> Identity() => new("--id")
    {
        Description = "The contact to act on, by the identifier the deployment's book gave it.",
        Required = true,
    };

    /// <summary>Builds the option naming one address a command acts on.</summary>
    /// <param name="description">What the address means for the command taking it.</param>
    /// <returns>The option.</returns>
    internal static Option<string> Address(string description) => new("--address")
    {
        Description = description,
        Required = true,
    };
}
