// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Session;

/// <summary>Whether this client can reach the deployment it is pointed at, which is not a question about any mailbox.</summary>
/// <remarks>
/// Three separable facts meet on a screen and this is the first of them. Whether the client reaches its deployment at
/// all is answered here; whether the deployment reaches a given account's mail server, and when that account was last
/// reconciled, are answered per account and mean nothing until this says <see cref="Reached" />. A person whose
/// network dropped and a person whose mail provider is refusing connections need different sentences.
/// </remarks>
public enum ConnectionStanding
{
    /// <summary>The client is asking the deployment, either for the first time or after one attempt did not arrive.</summary>
    Reaching = 0,

    /// <summary>The deployment answered, whatever it answered — a refusal is a deployment that was reached.</summary>
    Reached = 1,

    /// <summary>Nothing answered within the attempts this client makes on its own, so the next one is a person's to ask for.</summary>
    Lost = 2,
}
