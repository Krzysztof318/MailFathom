// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Contacts;

namespace MailFathom.Cli.Commands.Contacts;

/// <summary>Changes part of a contact by reading the record the book holds and sending back what it is to become.</summary>
/// <remarks>
/// <para>
/// The book takes the whole record rather than the difference from the one held, so correcting a name, adding an
/// address, and dropping one are all this: read, apply the change to what came back, send the result. That is what keeps
/// the invariants checkable — a record is only ever validated as a whole — and it is why there is no route that adds an
/// address on its own.
/// </para>
/// <para>
/// The read and the write are two requests, so two operators editing one contact at once are last-writer-wins, exactly
/// as the book's own amendment rule states. What is not left to that is an amendment racing an erasure: the deployment
/// answers such a write as a contact it does not hold rather than putting the person back.
/// </para>
/// </remarks>
internal static class ContactRecordEdit
{
    /// <summary>Reads one contact, applies a change to it, and asks the deployment to hold the result.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <param name="contactId">The contact to amend.</param>
    /// <param name="requestedDeployment">The deployment the operator named for this invocation, or <see langword="null" />.</param>
    /// <param name="change">Produces the record the contact is to have, from the one it has.</param>
    /// <param name="performed">What the command did, as a sentence's opening.</param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>The exit code the command ends with.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment holds no such contact, or when the change is one the record leaves nothing to do.</exception>
    internal static async Task<int> AmendAsync(
        CliContext context,
        Guid contactId,
        string? requestedDeployment,
        Func<ContactRecord, ContactRecordRequest> change,
        string performed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(change);

        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var lookup = await deployment.ReadContactAsync(profile.Token, contactId, cancellationToken);

        if (lookup.Contact is not { } held)
        {
            throw new CliFailure(
                $"The deployment's contact book holds no contact {contactId:D}, so there was nothing to amend.");
        }

        var amended = await deployment.AmendContactAsync(
            profile.Token,
            contactId,
            change(held),
            cancellationToken);

        return ContactOutput.ReportWrite(context, amended, performed);
    }

    /// <summary>Reads the addresses a held record carries, refusing one the deployment reported without any.</summary>
    /// <param name="held">The contact as the book holds it.</param>
    /// <returns>The addresses.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="held" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the record carries no address, which no contact the book admits can.</exception>
    internal static IReadOnlyList<string> AddressesOf(ContactRecord held)
    {
        ArgumentNullException.ThrowIfNull(held);

        return held.Addresses is { Count: > 0 } addresses
            ? addresses
            : throw new CliFailure(
                $"The deployment answered for contact {held.Id:D} with a record holding no address, which no contact it admits can.");
    }

    /// <summary>Reports whether a record already holds an address, comparing the way the book does.</summary>
    /// <param name="addresses">The addresses the record holds.</param>
    /// <param name="address">The address to look for.</param>
    /// <returns><see langword="true" /> when one of them names the same mailbox.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// Case-insensitively over the whole address, which is the book's own rule: RFC 5321 makes the local part
    /// case-sensitive and almost no provider honours it, so <c>Anna@example.test</c> and <c>anna@example.test</c> are one
    /// address here exactly as they are there. Comparing any other way would have the command ask a deployment to add an
    /// address it already holds, and be refused by it rather than by this.
    /// </remarks>
    internal static bool Holds(IReadOnlyList<string> addresses, string address)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentNullException.ThrowIfNull(address);

        return addresses.Any(held => string.Equals(held, address, StringComparison.OrdinalIgnoreCase));
    }
}
