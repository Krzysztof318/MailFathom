// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Specialized;
using MailFathom.Client.Presentation.Threads;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;

namespace MailFathom.Client.Presentation.Spaces.Mail;

/// <summary>Mail: the mailbox itself, read the way a mail client is read.</summary>
/// <remarks>
/// The handlers below are the list controls' own behaviour rather than the space's: which selection mode a selector is
/// in is a fact about this window and this input device, and where a list is scrolled to is a fact about a viewport.
/// Neither is about the mail somebody is reading. What comes <em>out</em> of a selection is the model's, and it goes
/// into the workspace scope — so nothing here reads or writes what is selected.
/// </remarks>
public sealed partial class MailPage : Page
{
    private string scrolledTo = string.Empty;

    /// <summary>Initializes the space.</summary>
    public MailPage()
    {
        this.InitializeComponent();
    }

    /// <summary>Brings the message a conversation was opened at into view, once the conversation has arrived.</summary>
    /// <param name="sender">The conversation's list.</param>
    /// <param name="args">The load, which is where the collection behind the list becomes watchable.</param>
    /// <remarks>
    /// Scrolling is the one part of opening a conversation at a message that a model cannot do: which message is opened
    /// is the model's answer and is asserted as one, and where a viewport sits is the control's. The collection is
    /// watched rather than the items bound once, because the rows are rebuilt whenever anything is expanded and the
    /// conversation itself arrives after the list is loaded.
    /// </remarks>
    private void OnThreadLoaded(object sender, RoutedEventArgs args)
    {
        if (this.ThreadMessageRows.ItemsSource is INotifyCollectionChanged watchable)
        {
            watchable.CollectionChanged += this.OnThreadMessagesChanged;
        }

        this.BringOpenedMessageIntoView();
    }

    /// <summary>Stops watching the conversation's rows when the space goes away.</summary>
    private void OnThreadUnloaded(object sender, RoutedEventArgs args)
    {
        if (this.ThreadMessageRows.ItemsSource is INotifyCollectionChanged watchable)
        {
            watchable.CollectionChanged -= this.OnThreadMessagesChanged;
        }
    }

    private void OnThreadMessagesChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        this.BringOpenedMessageIntoView();

    /// <summary>Scrolls to the opened message, and only the first time each one becomes the opened one.</summary>
    /// <remarks>
    /// Which message was last scrolled to is kept here rather than answered by the model, because the model's answer
    /// stays true for as long as the conversation is open: expanding another message rebuilds the rows without changing
    /// which one was arrived at, and scrolling again on each of those would drag the viewport away from whatever the
    /// reader had just pressed.
    /// </remarks>
    private void BringOpenedMessageIntoView()
    {
        if (this.ThreadMessageRows.ItemsSource is not IEnumerable<object> rows)
        {
            return;
        }

        var opened = rows.OfType<ThreadMessageRow>().FirstOrDefault(static row => row.IsOpenedAt);

        if (opened is null || string.Equals(opened.Key, this.scrolledTo, StringComparison.Ordinal))
        {
            return;
        }

        this.scrolledTo = opened.Key;
        this.ThreadMessageRows.ScrollIntoView(opened);
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
    private void OnSelectingMany(object sender, RoutedEventArgs args) =>
        this.MessageRows.SelectionMode = ListViewSelectionMode.Multiple;

    /// <summary>Returns the list to the selection a pointer expects, keeping nothing that was chosen in the other one.</summary>
    /// <remarks>
    /// The selection is cleared rather than carried across, because the two modes mean different things by a tap: rows
    /// gathered one at a time would otherwise stay selected under a mode where the next click replaces them, which is a
    /// scope somebody would ask a question against without having meant to.
    /// </remarks>
    private void OnSelectingOne(object sender, RoutedEventArgs args)
    {
        this.MessageRows.SelectedItems.Clear();
        this.MessageRows.SelectionMode = ListViewSelectionMode.Extended;
    }
}
