// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Answers with the one owner a deployment declaring its accounts in a file holds.</summary>
/// <remarks>
/// The real source is settled by a startup gate against the owner records the database holds, and the refusals it
/// raises are asserted where that gate lives. Everything downstream of it only needs an owner to exist, so a test of
/// what a configuration serves states it the way the composition root supplies it rather than reaching for the gate.
/// </remarks>
internal sealed class StubDeploymentMailOwnerSource : IDeploymentMailOwnerSource
{
    /// <inheritdoc />
    public MailOwnerId Owner => SyntheticMailOwner.Deployment;
}
