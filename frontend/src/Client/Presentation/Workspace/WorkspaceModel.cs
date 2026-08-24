// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.Localization;

namespace MailFathom.Client.Presentation.Workspace;

/// <summary>
/// The model behind <see cref="WorkspacePage"/>, which is the frame the three spaces are shown inside rather than a
/// space of its own. It owns what sits above them: the question being composed, and what that question would be asked
/// against.
/// </summary>
/// <remarks>
/// Neither is answered here. Running a question is Discover's work, and narrowing a scope is done by the space
/// somebody is narrowing in — the frame's part is that both survive moving between spaces, which is why it reads them
/// from <see cref="IWorkspace"/> rather than holding them itself.
/// </remarks>
public partial record WorkspaceModel
{
    private const string EverythingKey = "WorkspaceScope.Everything";
    private const string AccountFolderKey = "WorkspaceScope.AccountFolder";
    private const string SelectedKey = "WorkspaceScope.Selected";

    private readonly IWorkspace workspace;
    private readonly IStringLocalizer localizer;

    /// <summary>Initializes the frame over the workspace its spaces share.</summary>
    /// <param name="workspace">The question and the scope every space reads and writes.</param>
    /// <param name="localizer">Where the words describing a scope come from, since they are composed rather than per control.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public WorkspaceModel(IWorkspace workspace, IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(localizer);

        this.workspace = workspace;
        this.localizer = localizer;
        this.ScopeDescription = workspace.Scope.Select(this.Describe);
    }

    /// <summary>What somebody is about to ask, held for the run rather than for the screen they typed it on.</summary>
    public IState<string> Intent => this.workspace.Intent;

    /// <summary>The scope the question would be asked against, as the words the indicator beside the field shows.</summary>
    /// <remarks>
    /// Built once rather than per read: the indicator is bound to it, and a second feed per read would leave the view
    /// and anything the model asked subscribed to two descriptions of one scope.
    /// </remarks>
    public IFeed<string> ScopeDescription { get; }

    /// <summary>
    /// The keys the words above are resolved by, in the one place they are written.
    /// </summary>
    /// <remarks>
    /// A scope is described by composing words rather than by a <c>x:Uid</c> on a control, so these keys are asked
    /// for from code — which makes a typo in one of them the single way a reader would meet the key itself instead of
    /// a sentence. The unit suite holds every authored table to naming all three.
    /// </remarks>
    internal static IReadOnlyList<string> ScopeResourceKeys { get; } = [EverythingKey, AccountFolderKey, SelectedKey];

    private string Describe(WorkspaceScope scope)
    {
        var where = scope switch
        {
            { Account: { } account, Folder: { } folder } => this.localizer[AccountFolderKey, account, folder].Value,
            { Account: { } account } => account,
            _ => this.localizer[EverythingKey].Value,
        };

        return scope.Selection.Count is 0
            ? where
            : this.localizer[SelectedKey, where, scope.Selection.Count].Value;
    }
}
