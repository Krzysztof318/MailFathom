// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Domain.Contacts;
using MailFathom.Mcp.Tools.Contacts;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Publishes the answer to a lookup of one person.</summary>
/// <remarks>
/// A lookup that resolves nobody is an answered question rather than a failed call: the caller asked whether this
/// deployment holds a person and the answer is that it does not. Reporting it as a failure would make "nobody is
/// recorded" indistinguishable from a request that went wrong, and would put a client into a retry it can never win.
/// </remarks>
[Description("The person the lookup resolved, or none when the contact book holds nobody of that identity or nobody using that address.")]
internal sealed record GetContactToolResult
{
    /// <summary>Gets the contact the lookup resolved, or <see langword="null" /> when the book holds none.</summary>
    [Description("The person the lookup resolved, or null when the book holds nobody. Null is an answer rather than an error: this deployment has no record of them.")]
    public PublishedContact? Contact { get; init; }

    /// <summary>Publishes what a lookup resolved.</summary>
    /// <param name="contact">The contact the book held, or <see langword="null" /> when it held none.</param>
    /// <returns>The wire representation of the lookup.</returns>
    public static GetContactToolResult From(Contact? contact) =>
        new() { Contact = contact is null ? null : PublishedContact.From(contact) };
}
