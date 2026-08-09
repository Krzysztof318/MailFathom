// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.AI.Chat;

/// <summary>How much reasoning a model is asked to spend before it answers.</summary>
/// <remarks>
/// <para>
/// Stating it is what makes a reasoning model usable here at all: a provider that refuses function tools beside an
/// unstated effort names two ways out, and this is the one that keeps the tools. A run is a tool loop by construction —
/// the model asks for mail, retrieval answers, the model writes — so removing the tools would remove the capability
/// rather than work around the refusal.
/// </para>
/// <para>
/// The members are what the neutral AI abstraction the request is built through can carry, rather than one provider's
/// vocabulary. A value MailFathom accepted but could not send would be a setting an operator writes and nothing reads.
/// </para>
/// </remarks>
public enum ChatReasoningEffort
{
    /// <summary>No reasoning at all before the answer.</summary>
    /// <remarks>Distinct from writing nothing: this states the effort and sends it, which is what a provider refusing an unstated effort asks for. Writing nothing sends no parameter.</remarks>
    None = 0,

    /// <summary>The least reasoning the model offers beyond none.</summary>
    Low = 1,

    /// <summary>The model's own middle setting.</summary>
    Medium = 2,

    /// <summary>More reasoning, for a longer and more expensive answer.</summary>
    High = 3,

    /// <summary>The most reasoning the model offers.</summary>
    /// <remarks>Sent as the provider's <c>xhigh</c>. Not every reasoning model accepts it, and one that does not refuses the request rather than falling back.</remarks>
    ExtraHigh = 4,
}
