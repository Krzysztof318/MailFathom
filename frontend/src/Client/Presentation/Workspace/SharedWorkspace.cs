// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Workspace;

/// <summary>The one workspace a run has, registered for the whole of it.</summary>
/// <remarks>
/// Both states are owned by this instance rather than by a model, which is the whole point: a model is built and
/// discarded as its view is navigated to and away from, and a question or a scope that went with it would be lost
/// exactly when somebody moved between spaces. Reading either property twice hands back the same state, because MVUX
/// resolves it against the owner rather than building one per read.
/// </remarks>
internal sealed class SharedWorkspace : IWorkspace
{
    /// <inheritdoc />
    public IState<string> Intent => State.Value(this, () => string.Empty);

    /// <inheritdoc />
    public IState<WorkspaceScope> Scope => State.Value(this, () => WorkspaceScope.Everything);
}
