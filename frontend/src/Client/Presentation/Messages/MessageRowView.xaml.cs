// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Messages;

/// <summary>The row shared by the timeline and the ranked result list.</summary>
public sealed partial class MessageRowView : UserControl
{
    /// <summary>Identifies the message row drawn by this control.</summary>
    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(
        nameof(Row),
        typeof(MessageRow),
        typeof(MessageRowView),
        new PropertyMetadata(MessageRow.Nothing));

    /// <summary>Initializes the row.</summary>
    public MessageRowView() => this.InitializeComponent();

    /// <summary>Gets or sets the row to draw.</summary>
    public MessageRow Row
    {
        get => (MessageRow)this.GetValue(RowProperty);
        set => this.SetValue(RowProperty, value);
    }
}
