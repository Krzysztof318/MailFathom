// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Mail;
using Windows.System;

namespace MailFathom.Client.Presentation.Spaces.Mail.Reading;

/// <summary>Draws one message's body, and asks before anything in it leaves the application.</summary>
/// <remarks>
/// <para>
/// It takes what it draws as a property rather than reaching for a model, which is what lets the pane that composes it
/// decide where a message comes from. The reading it is given has already been turned into what a screen shows —
/// whether the document is drawn, why it is not, and what a reader is told about what was left out — so nothing here
/// decides any of that.
/// </para>
/// <para>
/// No message content is ever put in a log, a telemetry event, or an error message: a body that cannot be drawn becomes
/// the words the deployment sent beside a reason, and a picture that cannot be drawn becomes what the message said it
/// shows.
/// </para>
/// </remarks>
public sealed partial class MailBodyView : UserControl
{
    /// <summary>Identifies the <see cref="Reading"/> property.</summary>
    public static readonly DependencyProperty ReadingProperty = DependencyProperty.Register(
        nameof(Reading),
        typeof(MailBodyReading),
        typeof(MailBodyView),
        new PropertyMetadata(null, OnReadingChanged));

    /// <summary>Identifies the <see cref="ShowRemoteContentCommand"/> property.</summary>
    public static readonly DependencyProperty ShowRemoteContentCommandProperty = DependencyProperty.Register(
        nameof(ShowRemoteContentCommand),
        typeof(ICommand),
        typeof(MailBodyView),
        new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="Nothing"/> property.</summary>
    public static readonly DependencyProperty NothingProperty = DependencyProperty.Register(
        nameof(Nothing),
        typeof(bool),
        typeof(MailBodyView),
        new PropertyMetadata(true));

    private bool asking;

    /// <summary>Initializes the pane.</summary>
    public MailBodyView()
    {
        this.InitializeComponent();
    }

    /// <summary>Gets or sets the message this pane is drawing, or <see langword="null" /> where nothing is open.</summary>
    public MailBodyReading? Reading
    {
        get => (MailBodyReading?)this.GetValue(ReadingProperty);
        set => this.SetValue(ReadingProperty, value);
    }

    /// <summary>Gets or sets what re-reads the open message with its remote content followed.</summary>
    /// <remarks>
    /// Supplied by whoever composes this pane rather than held here, because asking again is a read of the deployment
    /// and this is a view. Where nothing supplies one, the offer is simply not actionable.
    /// </remarks>
    public ICommand? ShowRemoteContentCommand
    {
        get => (ICommand?)this.GetValue(ShowRemoteContentCommandProperty);
        set => this.SetValue(ShowRemoteContentCommandProperty, value);
    }

    /// <summary>Gets or sets whether the pane has nothing open in it.</summary>
    /// <remarks>
    /// Stated as its own affirmative rather than read as the absence of a reading, so the state a binding carries
    /// before anything has been opened is the one the pane is actually in.
    /// </remarks>
    public bool Nothing
    {
        get => (bool)this.GetValue(NothingProperty);
        set => this.SetValue(NothingProperty, value);
    }

    private static void OnReadingChanged(DependencyObject subject, DependencyPropertyChangedEventArgs change) =>
        ((MailBodyView)subject).Redraw();

    private void Redraw()
    {
        this.Nothing = this.Reading is not { IsOpen: true };
        this.Document.Child = null;

        if (this.Reading is { DrawsDocument: true } reading)
        {
            _ = this.DrawAsync(reading);
        }
    }

    /// <summary>Builds the document and puts it in place, unless another message arrived while it was being built.</summary>
    /// <remarks>
    /// Started rather than awaited, because a dependency property change is not a place to wait: the notices and the
    /// words are already on the screen and the drawing joins them when it is ready. Its continuations keep their
    /// context, every one of them ending at the visual tree.
    /// </remarks>
    private async Task DrawAsync(MailBodyReading reading)
    {
        var drawing = new MailBodyDrawing(
            reading.Words,
            (link, displayed) => _ = this.AskAsync(link, displayed, reading.Words));

        // The tree is attached before a picture is resolved, so the message is on the screen while its pictures are
        // being decided. A remote one the reader consented to is a request to somebody else's server, and waiting for
        // it here would leave the reader an empty pane for as long as that server takes to answer or fail.
        var drawn = drawing.Draw(reading.Blocks);

        if (!ReferenceEquals(this.Reading, reading))
        {
            return;
        }

        this.Document.Child = drawn;

        await drawing.FillPicturesAsync();
    }

    /// <summary>Shows where a link actually goes, and opens it only if the reader says so.</summary>
    /// <remarks>
    /// The target is the deployment's resolution of it rather than anything this pane derived, and both spellings of a
    /// host written in another script are shown wherever they differ — which is what a homograph looks like. Opening it
    /// hands the address to the platform, so following a link leaves the application rather than navigating anything
    /// the message is drawn in.
    /// </remarks>
    private async Task AskAsync(MailBodyLink link, string displayed, MailBodyWords words)
    {
        if (this.asking)
        {
            return;
        }

        this.asking = true;

        try
        {
            this.LinkQuestion.XamlRoot = this.XamlRoot;
            this.LinkQuestion.Title = words.LinkTitle;
            this.LinkQuestion.PrimaryButtonText = words.LinkOpen;
            this.LinkQuestion.CloseButtonText = words.LinkCancel;

            this.LinkDisplayLabel.Text = words.LinkDisplayText;
            this.LinkDisplayValue.Text = displayed;
            this.LinkTargetLabel.Text = words.LinkTarget;
            this.LinkTargetValue.Text = link.Target;

            var spelled = link.AsciiHost is { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
            this.LinkPunycodeLabel.Text = words.LinkPunycode;
            this.LinkPunycodeLabel.Visibility = spelled;
            this.LinkPunycodeValue.Text = link.AsciiHost ?? string.Empty;
            this.LinkPunycodeValue.Visibility = spelled;

            // Read off the contract rather than re-derived here: a homograph host whose link text is prose names no
            // host to disagree with, so the mismatch verdict is absent while the two spellings above are exactly what
            // the reader needs warning about — and a dialog deciding for itself is how the two come to disagree.
            this.LinkDeception.Message = words.LinkDeception;
            this.LinkDeception.IsOpen = link.IsWorthWarningAbout;

            if (await this.LinkQuestion.ShowAsync() is not ContentDialogResult.Primary)
            {
                return;
            }

            if (Followable(link.Target) is { } address)
            {
                _ = await Launcher.LaunchUriAsync(address);
            }
        }
        finally
        {
            this.asking = false;
        }
    }

    /// <summary>Judges a target again on this side, so the schemes a pane may open are stated where they are opened.</summary>
    /// <remarks>
    /// The deployment admits the same three, and stating them twice is deliberate: this is the line where an address a
    /// stranger wrote reaches the operating system, and a head reading a deployment it does not control is exactly
    /// where the second statement earns itself.
    /// </remarks>
    private static Uri? Followable(string target) =>
        Uri.TryCreate(target, UriKind.Absolute, out var address)
        && (address.Scheme == Uri.UriSchemeHttp
            || address.Scheme == Uri.UriSchemeHttps
            || address.Scheme == Uri.UriSchemeMailto)
            ? address
            : null;
}
