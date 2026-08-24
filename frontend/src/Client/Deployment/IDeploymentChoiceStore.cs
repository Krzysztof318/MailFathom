// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Deployment;

/// <summary>Where the deployment somebody chose is kept, so that starting the client again is opening it.</summary>
/// <remarks>
/// <para>
/// A seam because the thing behind it is a platform store that no unit test can reach — a per-user preferences file on
/// a desktop, the browser's own storage for the origin the page came from — while everything decided around it is
/// ordinary logic that ought to be tested. What is written is an address and nothing else: no credential, no mailbox,
/// nothing a deployment answered, so it carries none of the classification the rest of this client's data does.
/// </para>
/// <para>
/// Per user rather than per installation, and that is the platform's own answer rather than one this application
/// arranges. A desktop head writes into the account's own application data, so two people sharing a machine keep two
/// answers; a browser head writes into storage scoped to the origin it was served from, which is that browser profile's
/// and nobody else's.
/// </para>
/// </remarks>
internal interface IDeploymentChoiceStore
{
    /// <summary>Reads the deployment chosen before this run.</summary>
    /// <returns>What was chosen, or <see langword="null" /> where nobody has chosen yet or what was kept is no longer readable as an address.</returns>
    Uri? Read();

    /// <summary>Keeps a deployment as the one this installation reaches from now on.</summary>
    /// <param name="address">The chosen deployment's base address.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="address" /> is <see langword="null" />.</exception>
    void Write(Uri address);
}
