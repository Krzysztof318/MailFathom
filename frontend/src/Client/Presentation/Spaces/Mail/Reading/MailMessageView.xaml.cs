// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Spaces.Mail.Reading;

/// <summary>Composes one message's headers, sender verdict, attachments, and body.</summary>
public sealed partial class MailMessageView : UserControl
{
    /// <summary>Identifies <see cref="Reading"/>.</summary>
    public static readonly DependencyProperty ReadingProperty = DependencyProperty.Register(
        nameof(Reading),
        typeof(MailMessageReading),
        typeof(MailMessageView),
        new PropertyMetadata(null));
    /// <summary>Identifies <see cref="Body"/>.</summary>
    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
        nameof(Body),
        typeof(MailBodyReading),
        typeof(MailMessageView),
        new PropertyMetadata(null));
    /// <summary>Identifies <see cref="ShowRemoteContentCommand"/>.</summary>
    public static readonly DependencyProperty ShowRemoteContentCommandProperty = DependencyProperty.Register(
        nameof(ShowRemoteContentCommand),
        typeof(ICommand),
        typeof(MailMessageView),
        new PropertyMetadata(null));
    /// <summary>Identifies <see cref="ShowRemoteContentCommandParameter"/>.</summary>
    public static readonly DependencyProperty ShowRemoteContentCommandParameterProperty = DependencyProperty.Register(
        nameof(ShowRemoteContentCommandParameter),
        typeof(object),
        typeof(MailMessageView),
        new PropertyMetadata(null));
    /// <summary>Identifies <see cref="SaveAttachmentCommand"/>.</summary>
    public static readonly DependencyProperty SaveAttachmentCommandProperty = DependencyProperty.Register(
        nameof(SaveAttachmentCommand),
        typeof(ICommand),
        typeof(MailMessageView),
        new PropertyMetadata(null));
    /// <summary>Identifies <see cref="CancelAttachmentCommand"/>.</summary>
    public static readonly DependencyProperty CancelAttachmentCommandProperty = DependencyProperty.Register(
        nameof(CancelAttachmentCommand),
        typeof(ICommand),
        typeof(MailMessageView),
        new PropertyMetadata(null));
    /// <summary>Identifies <see cref="UseSelectionCommand"/>.</summary>
    public static readonly DependencyProperty UseSelectionCommandProperty = DependencyProperty.Register(
        nameof(UseSelectionCommand),
        typeof(ICommand),
        typeof(MailMessageView),
        new PropertyMetadata(null));

    /// <summary>Initializes the pane.</summary>
    public MailMessageView() => this.InitializeComponent();

    /// <summary>Gets or sets everything drawn around the body.</summary>
    public MailMessageReading? Reading
    {
        get => (MailMessageReading?)this.GetValue(ReadingProperty);
        set => this.SetValue(ReadingProperty, value);
    }

    /// <summary>Gets or sets the body drawn inside the message.</summary>
    public MailBodyReading? Body
    {
        get => (MailBodyReading?)this.GetValue(BodyProperty);
        set => this.SetValue(BodyProperty, value);
    }

    /// <summary>Gets or sets the command that re-reads the body with remote pictures.</summary>
    public ICommand? ShowRemoteContentCommand
    {
        get => (ICommand?)this.GetValue(ShowRemoteContentCommandProperty);
        set => this.SetValue(ShowRemoteContentCommandProperty, value);
    }

    /// <summary>Gets or sets the message passed to the remote-content command.</summary>
    public object? ShowRemoteContentCommandParameter
    {
        get => this.GetValue(ShowRemoteContentCommandParameterProperty);
        set => this.SetValue(ShowRemoteContentCommandParameterProperty, value);
    }

    /// <summary>Gets or sets the command that saves an attachment.</summary>
    public ICommand? SaveAttachmentCommand
    {
        get => (ICommand?)this.GetValue(SaveAttachmentCommandProperty);
        set => this.SetValue(SaveAttachmentCommandProperty, value);
    }

    /// <summary>Gets or sets the command that cancels an attachment save.</summary>
    public ICommand? CancelAttachmentCommand
    {
        get => (ICommand?)this.GetValue(CancelAttachmentCommandProperty);
        set => this.SetValue(CancelAttachmentCommandProperty, value);
    }

    /// <summary>Gets or sets the command that makes a selected body passage the workspace scope.</summary>
    public ICommand? UseSelectionCommand
    {
        get => (ICommand?)this.GetValue(UseSelectionCommandProperty);
        set => this.SetValue(UseSelectionCommandProperty, value);
    }
}
