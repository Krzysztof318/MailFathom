// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.AI.Chat;

/// <summary>Which of the provider's two request APIs a chat call is conducted through.</summary>
/// <remarks>
/// <para>
/// Declared by the deployment rather than derived from the model, and that is the decision rather than a default nobody
/// got round to replacing. Deriving it would mean reading the routed model name, and that name is not a model identity:
/// for a cloud deployment it is whatever the operator called the deployment, so a derivation would be guessing from a
/// string the operator invented — and a wrong guess is one nothing in the deployment could correct.
/// </para>
/// <para>
/// Both are reached over the same endpoint, the same credential, the same transport, and the same resilience budget.
/// What differs is the path the request goes to and, for a reasoning model, whether the provider will accept function
/// tools beside a stated reasoning effort at all.
/// </para>
/// </remarks>
public enum ChatProviderApi
{
    /// <summary>The chat completions API, at <c>/chat/completions</c> under the endpoint's address.</summary>
    /// <remarks>What every OpenAI-compatible server offers, which is why it is the value a deployment that states nothing runs on.</remarks>
    ChatCompletions = 0,

    /// <summary>The responses API, at <c>/responses</c> under the endpoint's address.</summary>
    /// <remarks>
    /// What a current reasoning model requires to be given function tools while a reasoning effort is stated. Not every
    /// OpenAI-compatible server offers it, which is why choosing it is the deployment's statement about its own provider
    /// rather than something this can establish.
    /// </remarks>
    Responses = 1,
}
