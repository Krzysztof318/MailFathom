// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Presentation.Workspace;

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>A run's shared question and scope, initialized to what a test names.</summary>
internal sealed class StubWorkspace : IWorkspace
{
    internal StubWorkspace(WorkspaceScope? scope = null)
    {
        this.Intent = State.Value(this, () => string.Empty);
        this.Scope = State.Value(this, () => scope ?? WorkspaceScope.Everything);
    }

    public IState<string> Intent { get; }

    public IState<WorkspaceScope> Scope { get; }
}
