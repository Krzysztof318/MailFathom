// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Answers the recorder's contract and records nothing, for calls whose outcome speaks for no role.</summary>
/// <remarks>
/// <para>
/// A role's health answers one question — can this deployment do the thing that role is for — and
/// <see cref="AiProviderRole.Chat" />'s is read to decide whether questions may be asked. A capability that a deployment
/// may point at a model of its own therefore has nothing to write there: its endpoint failing says nothing about whether
/// the answering model can answer, and writing it anyway takes a working capability out of service on the strength of an
/// unrelated endpoint.
/// </para>
/// <para>
/// Not a second role, because nothing reads one. Where such a capability shares the answering model's endpoint, that
/// endpoint's health is already written by the answering calls themselves and this adds nothing; where it does not, there
/// is nothing for it to add.
/// </para>
/// </remarks>
internal sealed class UnreportedProviderHealth : IAiProviderHealthRecorder
{
    /// <summary>Gets the one instance, which holds no state of any kind.</summary>
    public static UnreportedProviderHealth Instance { get; } = new();

    /// <inheritdoc />
    public void RecordServed(AiProviderRole role)
    {
    }

    /// <inheritdoc />
    public void RecordUnavailable(AiProviderRole role)
    {
    }

    /// <inheritdoc />
    public void RecordMisconfigured(AiProviderRole role)
    {
    }
}
