// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Names the owner this suite's deployment serves, which every account it declares belongs to.</summary>
/// <remarks>
/// <para>
/// A composed host resolves this from its owner records while it starts. Nothing here starts one, so the harness states
/// the answer instead — and the value only has to agree with the caller the harness admits, because what the
/// caller-scoped catalog decides is whether the caller is acting for the owner the deployment's accounts belong to. That
/// the persisted rows can be read at all is a claim of its own and belongs to a test about the owner directory rather
/// than to every test that reads a mailbox.
/// </para>
/// <para>
/// It is fixed rather than generated so a failure names the same value on every run, in the same spirit as the synthetic
/// account identifiers beside it.
/// </para>
/// </remarks>
internal sealed class OrchestratedDeploymentOwner : IDeploymentMailOwnerSource
{
    /// <summary>Gets the owner every account this harness declares belongs to.</summary>
    public static MailOwnerId ServedOwner { get; } = MailOwnerId.Create(new Guid("0198f0aa-0000-7000-8000-00000000000d"));

    /// <inheritdoc />
    public MailOwnerId Owner => ServedOwner;
}
