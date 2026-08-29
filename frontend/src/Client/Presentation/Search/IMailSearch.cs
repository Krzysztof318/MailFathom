// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Presentation.Messages;

namespace MailFathom.Client.Presentation.Search;

/// <summary>The ranked mail and recent searches one client run holds.</summary>
public interface IMailSearch
{
    /// <summary>Whether search is shown instead of the timeline.</summary>
    IState<bool> IsOpen { get; }

    /// <summary>The query text being edited.</summary>
    IState<string> Query { get; }

    /// <summary>The account constraint being edited.</summary>
    IState<string> Account { get; }

    /// <summary>The folder constraint being edited.</summary>
    IState<string> Folder { get; }

    /// <summary>The sender constraint being edited.</summary>
    IState<string> Sender { get; }

    /// <summary>The recipient constraint being edited.</summary>
    IState<string> Recipient { get; }

    /// <summary>The inclusive received-date constraint being edited.</summary>
    IState<DateTimeOffset> ReceivedOnOrAfter { get; }

    /// <summary>The exclusive received-date constraint being edited.</summary>
    IState<DateTimeOffset> ReceivedBefore { get; }

    /// <summary>The read-state constraint being edited.</summary>
    IState<bool> Unread { get; }

    /// <summary>The flag-state constraint being edited.</summary>
    IState<bool> Flagged { get; }

    /// <summary>The attachment constraint being edited.</summary>
    IState<bool> HasAttachments { get; }

    /// <summary>The rows loaded for the current search.</summary>
    IListFeed<MessageRow> Results { get; }

    /// <summary>The searches kept for this run.</summary>
    IListFeed<RecentMailSearch> Recent { get; }

    /// <summary>What the current list says about scope and semantic capability.</summary>
    IFeed<MailSearchReading> Reading { get; }

    /// <summary>Whether the last attempt to take another page failed.</summary>
    IFeed<bool> PagingFailed { get; }

    /// <summary>Shows search and takes the current mailbox scope where no search is held.</summary>
    ValueTask OpenAsync(CancellationToken cancellationToken);

    /// <summary>Returns to the timeline without discarding the search.</summary>
    ValueTask CloseAsync(CancellationToken cancellationToken);

    /// <summary>Takes the current mailbox-tree place as the account and folder constraints.</summary>
    ValueTask UseCurrentScopeAsync(CancellationToken cancellationToken);

    /// <summary>Runs the edited search from its leading page.</summary>
    ValueTask SearchAsync(CancellationToken cancellationToken);

    /// <summary>Takes the next page onto the current result list.</summary>
    ValueTask ShowMoreAsync(CancellationToken cancellationToken);

    /// <summary>Removes account and folder constraints and immediately searches all mail.</summary>
    ValueTask WidenAsync(CancellationToken cancellationToken);

    /// <summary>Opens one result's conversation at that message.</summary>
    ValueTask OpenResultAsync(MessageRow result, CancellationToken cancellationToken);

    /// <summary>Sets or removes the read-state constraint.</summary>
    ValueTask SetUnreadAsync(bool? value, CancellationToken cancellationToken);

    /// <summary>Sets or removes the flag-state constraint.</summary>
    ValueTask SetFlaggedAsync(bool? value, CancellationToken cancellationToken);

    /// <summary>Sets or removes the attachment constraint.</summary>
    ValueTask SetHasAttachmentsAsync(bool? value, CancellationToken cancellationToken);

    /// <summary>Removes one named constraint.</summary>
    ValueTask ClearFilterAsync(MailSearchFilter filter, CancellationToken cancellationToken);

    /// <summary>Restores and runs one recent search.</summary>
    ValueTask RepeatAsync(RecentMailSearch recent, CancellationToken cancellationToken);
}
