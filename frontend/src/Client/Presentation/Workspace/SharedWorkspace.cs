// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Presentation.Mailboxes;

namespace MailFathom.Client.Presentation.Workspace;

/// <summary>The one workspace a run has, registered for the whole of it.</summary>
/// <remarks>
/// <para>
/// Both states are owned by this instance rather than by a model, which is the whole point: a model is built and
/// discarded as its view is navigated to and away from, and a question or a scope that went with it would be lost
/// exactly when somebody moved between spaces. Reading either property twice hands back the same state, because MVUX
/// resolves it against the owner rather than building one per read.
/// </para>
/// <para>
/// The scope starts where the mailbox tree was left rather than at everything, because the two are the same fact: the
/// tree is the client's scope selector, so a run that opened the tree on somebody's folder and the workspace on
/// everything would be showing a selected row that narrowed nothing. The question is not restored beside it — an
/// unsent question is what somebody was about to ask a moment ago rather than where they work.
/// </para>
/// </remarks>
internal sealed class SharedWorkspace : IWorkspace
{
    private readonly IMailboxTreeMemory memory;

    /// <summary>Initializes the workspace over where the mailbox tree was left.</summary>
    /// <param name="memory">Where the place somebody was working in outlives the run that chose it.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="memory" /> is <see langword="null" />.</exception>
    public SharedWorkspace(IMailboxTreeMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);

        this.memory = memory;
    }

    /// <inheritdoc />
    public IState<string> Intent => State.Value(this, () => string.Empty);

    /// <inheritdoc />
    public IState<WorkspaceScope> Scope => State.Value(this, () => this.memory.Read().Scope);
}
