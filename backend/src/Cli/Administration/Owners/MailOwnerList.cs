// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Administration.Owners;

/// <summary>The owners a deployment holds records for.</summary>
/// <param name="Owners">The owner identifiers, in the deployment's own stable order.</param>
/// <remarks>
/// Identifiers and nothing else, because that is all the deployment publishes here: an owner's identifier is its own
/// generated handle for a record and says nothing about the person, which is what makes listing it safe. It is also the
/// only handle either side has for an owner, so it is what every credential the command administers is selected by.
/// </remarks>
internal sealed record MailOwnerList(IReadOnlyList<Guid> Owners);
