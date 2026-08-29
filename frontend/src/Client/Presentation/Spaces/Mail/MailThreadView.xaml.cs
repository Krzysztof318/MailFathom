// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Specialized;
using MailFathom.Client.Presentation.Threads;

namespace MailFathom.Client.Presentation.Spaces.Mail;

/// <summary>The conversation shared by the wide companion column and the phone route.</summary>
public sealed partial class MailThreadView : UserControl
{
    /// <summary>Identifies the route opened when the conversation's selection changes.</summary>
    public static readonly DependencyProperty MessageRouteProperty = DependencyProperty.Register(
        nameof(MessageRoute),
        typeof(string),
        typeof(MailThreadView),
        new PropertyMetadata(string.Empty));

    private string scrolledTo = string.Empty;
    private ListView? threadMessageRows;

    /// <summary>Initializes the conversation.</summary>
    public MailThreadView() => this.InitializeComponent();

    /// <summary>The route one selected message opens, or empty where the conversation remains the wide companion.</summary>
    public string MessageRoute
    {
        get => (string)this.GetValue(MessageRouteProperty);
        set => this.SetValue(MessageRouteProperty, value);
    }

    private void OnThreadLoaded(object sender, RoutedEventArgs args)
    {
        this.threadMessageRows = (ListView)sender;

        if (this.threadMessageRows.ItemsSource is INotifyCollectionChanged watchable)
        {
            watchable.CollectionChanged += this.OnThreadMessagesChanged;
        }

        this.BringOpenedMessageIntoView();
    }

    private void OnThreadUnloaded(object sender, RoutedEventArgs args)
    {
        if (this.threadMessageRows is { } list && list.ItemsSource is INotifyCollectionChanged watchable)
        {
            watchable.CollectionChanged -= this.OnThreadMessagesChanged;
        }

        this.threadMessageRows = null;
    }

    private void OnThreadMessagesChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        this.BringOpenedMessageIntoView();

    private void BringOpenedMessageIntoView()
    {
        if (this.threadMessageRows?.ItemsSource is not IEnumerable<object> rows)
        {
            return;
        }

        var opened = rows.OfType<ThreadMessageRow>().FirstOrDefault(static row => row.IsOpenedAt);

        if (opened is null)
        {
            this.scrolledTo = string.Empty;

            return;
        }

        if (string.Equals(opened.Key, this.scrolledTo, StringComparison.Ordinal))
        {
            return;
        }

        this.scrolledTo = opened.Key;
        this.threadMessageRows.ScrollIntoView(opened);
    }
}
