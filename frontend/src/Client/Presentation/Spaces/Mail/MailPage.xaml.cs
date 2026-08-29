// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Presentation.Messages;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;

namespace MailFathom.Client.Presentation.Spaces.Mail;

/// <summary>Mail: the mailbox itself, read the way a mail client is read.</summary>
/// <remarks>
/// The handlers below are the list controls' own behaviour rather than the space's: which selection mode a selector is
/// in is a fact about this window and this input device, and where a list is scrolled to is a fact about a viewport.
/// Neither is about the mail somebody is reading. What comes <em>out</em> of a selection is the model's, and it goes
/// into the workspace scope — so nothing here reads the selection, it only writes it.
/// </remarks>
public sealed partial class MailPage : Page
{
    /// <summary>Identifies <see cref="ThreadNavigationRequest"/>.</summary>
    public static readonly DependencyProperty ThreadNavigationRequestProperty = DependencyProperty.Register(
        nameof(ThreadNavigationRequest),
        typeof(string),
        typeof(MailPage),
        new PropertyMetadata("MailThread"));

    private ListView? messageRows;

    /// <summary>Initializes the space.</summary>
    public MailPage()
    {
        this.InitializeComponent();
    }

    /// <summary>The route a selected row opens, or empty where the wide companion already holds the conversation.</summary>
    public string ThreadNavigationRequest
    {
        get => (string)this.GetValue(ThreadNavigationRequestProperty);
        set => this.SetValue(ThreadNavigationRequestProperty, value);
    }

    /// <summary>Puts the list into the selection a touch head reaches by pressing and holding a row.</summary>
    /// <param name="sender">The list the gesture happened on.</param>
    /// <param name="args">The gesture, which is acted on once it has completed rather than while it is being made.</param>
    /// <remarks>
    /// The gesture sets the control that shows the mode rather than the list itself, so a person who arrived by holding
    /// a row and a person who arrived by pressing the control are in one state with one way out of it. A pointer head
    /// raises no holding gesture at all, which is why the modifiers stay the whole of what a mouse needs.
    /// </remarks>
    private void OnRowHeld(object sender, HoldingRoutedEventArgs args)
    {
        if (args is { HoldingState: HoldingState.Completed })
        {
            this.SelectingToggle.IsChecked = true;
        }
    }

    /// <summary>Offers the selection several rows at a time, which is what a tap does in this mode.</summary>
    private void OnSelectingMany(object sender, RoutedEventArgs args)
    {
        if (this.messageRows is { } list)
        {
            list.SelectionMode = ListViewSelectionMode.Multiple;
        }
    }

    /// <summary>Returns the list to the selection a pointer expects, keeping nothing that was chosen in the other one.</summary>
    /// <remarks>
    /// The selection is cleared rather than carried across, because the two modes mean different things by a tap: rows
    /// gathered one at a time would otherwise stay selected under a mode where the next click replaces them, which is a
    /// scope somebody would ask a question against without having meant to.
    /// </remarks>
    private void OnSelectingOne(object sender, RoutedEventArgs args)
    {
        if (this.messageRows is not { } list)
        {
            return;
        }

        list.SelectedItems.Clear();
        list.SelectionMode = ListViewSelectionMode.Extended;
    }

    private void OnMessageRowsLoaded(object sender, RoutedEventArgs args) =>
        this.messageRows = (ListView)sender;

    private void OnMessageRowsUnloaded(object sender, RoutedEventArgs args)
    {
        if (ReferenceEquals(this.messageRows, sender))
        {
            this.messageRows = null;
        }
    }

    private async void OnMessageRowsSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (sender is not ListView list || this.DataContext is not MailViewModel model)
        {
            return;
        }

        IImmutableList<MessageRow> chosen = [.. list.SelectedItems.OfType<MessageRow>()];

        await model.Model.ChooseAsync(chosen, CancellationToken.None);
    }
}
