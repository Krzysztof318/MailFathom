// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Workspace;

/// <summary>
/// The workspace the frame gives a space: one column on a narrow window, and that column beside a companion one on a
/// wide window.
/// </summary>
/// <remarks>
/// It is the frame's rather than any space's, so that all three read the same at the same width and a later space
/// inherits the composition instead of restating it. What goes in either column is the space's own decision.
/// </remarks>
public sealed partial class WorkspaceColumns : UserControl
{
    /// <summary>Identifies the <see cref="Primary"/> property.</summary>
    public static readonly DependencyProperty PrimaryProperty = DependencyProperty.Register(
        nameof(Primary),
        typeof(object),
        typeof(WorkspaceColumns),
        new PropertyMetadata(defaultValue: null));

    /// <summary>Identifies the <see cref="Companion"/> property.</summary>
    public static readonly DependencyProperty CompanionProperty = DependencyProperty.Register(
        nameof(Companion),
        typeof(object),
        typeof(WorkspaceColumns),
        new PropertyMetadata(defaultValue: null));

    /// <summary>Initializes the workspace.</summary>
    public WorkspaceColumns()
    {
        this.InitializeComponent();
    }

    /// <summary>What the space is for, shown at every width.</summary>
    public object? Primary
    {
        get => this.GetValue(PrimaryProperty);
        set => this.SetValue(PrimaryProperty, value);
    }

    /// <summary>
    /// What stands beside the primary column when there is room for it, and is not shown when there is not.
    /// </summary>
    /// <remarks>
    /// A narrow window drops the column rather than stacking it, because two things stacked on a phone are one thing
    /// with something in the way of it. What that content is instead reachable as there is a route, which is what puts
    /// it on the screen stack the system back gesture moves through.
    /// </remarks>
    public object? Companion
    {
        get => this.GetValue(CompanionProperty);
        set => this.SetValue(CompanionProperty, value);
    }
}
