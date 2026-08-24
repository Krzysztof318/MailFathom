// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Workspace;

/// <summary>What the three spaces share, so that moving between them keeps what somebody was working on.</summary>
/// <remarks>
/// The frame reads both of these — the question somebody is composing, and what it would be asked against — and a
/// space writes them when somebody types, narrows to an account, opens a folder, or selects something. There is one
/// of these for the run: a question and a scope held per space would be three of each, and moving between spaces
/// would silently be a fourth thing to keep in step.
/// </remarks>
public interface IWorkspace
{
    /// <summary>The question being composed, which travels with somebody between spaces rather than being retyped.</summary>
    IState<string> Intent { get; }

    /// <summary>The scope every space reads and any of them may narrow.</summary>
    IState<WorkspaceScope> Scope { get; }
}
