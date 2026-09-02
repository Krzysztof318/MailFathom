// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// English is the source catalogue: it declares the keys every other language is then required to carry, which is what
// makes a key added here without its Polish counterpart a type error rather than a string missing from a screen. It is
// also the fallback, so a locale that resolves to nothing and an entry that resolves to nothing both land here.
//
// A `{name}` in a message is a hole the caller fills. Dates, numbers, and relative times never appear as one: those are
// formatted with `Intl` under the active locale, so no catalogue holds a month name, a decimal separator, or a
// word for "yesterday" that would have to be maintained in parallel with the one the platform already knows.

export const en = {
    'shell.title': 'MailFathom',
    'shell.language': 'Language',
    'shell.theme': 'Theme',
    'shell.spaces': 'Spaces',
    'shell.signOut': 'Sign out',
    'shell.clientVersion': 'Client {client}',
    'shell.versions': 'Client {client}, deployment {deployment}',
    'shell.account': 'Account and preferences',
    'shell.accountMenu': 'Account',
    'control.notBuiltYet': '{control} — not built yet',
    'ai.badge': 'AI',

    'theme.system': 'Follow the system',
    'theme.light': 'Light',
    'theme.dark': 'Dark',

    'space.discover': 'Discover',
    'space.mail': 'Mail',
    'space.cases': 'Cases',
    'space.agent': 'Agent',
    'space.tasks': 'Tasks',
    'space.calendar': 'Calendar',
    'space.people': 'People',
    'space.notBuiltYet': '{space} — not built yet',
    'space.pending':
        'This space is not built yet. What is here is the frame around it: its address, its navigation, and the scope every question is asked against.',

    'intent.label': 'Ask your mail',
    'intent.placeholder': 'What do you want to ask your mail?',
    'intent.ask': 'Ask',

    'scope.mailbox': 'Mailbox in scope',
    'scope.allMailboxes': 'All mailboxes',

    'signIn.claim': 'Your mail stays on your own server.',
    'signIn.claimExplanation':
        'Name the MailFathom deployment your organization runs. Indexing and analysis happen on it — nothing reaches anybody else without your say-so.',
    'signIn.revealPassword': 'Show',
    'signIn.hidePassword': 'Hide',
    'signIn.revealPasswordControl': 'Show the password',
    'signIn.hidePasswordControl': 'Hide the password',

    'signIn.title': 'Sign in to your MailFathom',
    'signIn.explanation':
        'Everything this client reads and everything you type into it goes to the deployment that holds your mail, and nowhere else.',
    'signIn.userName': 'User name',
    'signIn.password': 'Password',
    'signIn.submit': 'Sign in',
    'signIn.presenting': 'Signing in…',
    'signIn.abandon': 'Stop trying',
    'signIn.incomplete': 'Type the user name and the password your deployment gave you.',
    'signIn.userNameHasColon':
        'A user name cannot contain a colon, which is what separates it from the password when it is sent.',
    'signIn.tooLong': 'That user name or password is longer than this client will present. Check what was pasted in.',
    'signIn.credentialRefused': 'The user name or the password is not accepted by this deployment.',
    'signIn.basicNotOffered':
        'This deployment does not accept a user name and a password. Whoever runs it has to enable that before you can sign in here.',
    'signIn.grantMissing': 'This deployment accepted the credential, but it is allowed to read no mail.',
    'signIn.deploymentSilent': 'The deployment did not answer. Try again in a moment.',
    'signIn.noLongerAccepted': 'This deployment has stopped accepting the password that was kept. Sign in again.',
    'signIn.notRemoved':
        'Signing out did not remove the password from this machine’s credential store, so it is still kept there. Remove it in the store itself, or sign in and out again.',
    'signIn.notKept':
        'Your password could not be stored on this machine, so you will be asked for it again the next time you open MailFathom. You are signed in either way.',
    'signIn.keptUntilSignedOut':
        'Your password is kept in this machine’s keychain until you sign out. Signing out is what removes it.',
    'signIn.keptUntilTheTabCloses':
        'Your password is kept until you close this tab, and you will be asked for it again — a password left in a browser can be read by anything that reaches this page.',
    'signIn.keptUntilTheClientCloses':
        'Your password is kept until you close MailFathom, and you will be asked for it again — this machine offers no keychain to keep it in safely.',

    'connect.address': 'Deployment address',
    'connect.addressHint':
        'The host it answers on, and a port where it uses one — for example mailfathom.example.com or mailfathom.example.com:8443.',
    'connect.clearText': 'Reach this deployment over plain HTTP',
    'connect.clearTextExplanation':
        'Your password is encoded rather than encrypted, on every request. Anybody between this client and the deployment can read it. Leave this off unless the network between them is yours.',
    'connect.clearTextInForce':
        'TLS is off. The user name, the password, and every message read travel in the clear. Use this only inside a network you control or over a VPN.',
    'connect.portHint': 'port {port}',
    'connect.portDefaultNote': 'The port is optional — without one this client reaches {port}.',
    'connect.details': 'Connection details',
    'connect.hideDetails': 'Hide connection details',
    'connect.protocol': 'Protocol',
    'connect.protocolOverTls': 'HTTPS, over TLS',
    'connect.protocolClearText': 'HTTP, unencrypted',
    'connect.host': 'Address',
    'connect.port': 'Port',
    'connect.portDefault': '{port} (default)',
    'connect.encryption': 'Encryption',
    'connect.encryptionInForce': 'In force',
    'connect.encryptionNone': 'None',
    'connect.nothingNamed': 'Nothing named yet',
    'connect.blank': 'Name the deployment that holds your mail.',
    'connect.malformed': 'That is not an address. Name the host it answers on, and a port where it uses one.',
    'connect.clearTextRefused':
        'That address is plain HTTP, which this client will not send a password over until you say it may.',
    'connect.unavailable': 'Nothing answered there. Check the address, and check that the deployment is running.',
    'connect.unreadable': 'Something answered there, but not as MailFathom.',

    'deployment.reachedAt': 'Reading from {address}',
    'deployment.change': 'Point somewhere else',

    'accounts.reading': 'Reading accounts…',
    'accounts.notRefreshing':
        'This deployment is not refreshing the local copy of these accounts, so what you see is as current as its last run left it. That is a setting on the deployment rather than a permission you are missing.',
    'accounts.failed': 'The accounts could not be read: {reason}.',
    'accounts.oldest': 'The oldest of these was last refreshed {age}.',
    'accounts.noneDeclared':
        'Whoever runs this deployment declares which mailboxes it reads for you, and none is declared yet.',

    'account.synchronized': 'Up to date',
    'account.behind': 'Catching up',
    'account.failing': 'Stopped synchronizing',
    'account.unreachable': 'The mail server did not answer',
    'account.neverSynchronized': 'Nothing taken in yet',
    'account.lastRefreshed': 'Last refreshed {age}',
    'account.neverRefreshed': 'Never refreshed',

    'folders.label': 'Mailboxes and folders',
    'folders.reading': 'Reading mailboxes and folders…',
    'folders.failed': 'The mailboxes and folders could not be read: {reason}.',
    'folders.unread': '{count} unread',

    'mailboxes.heading': 'Folders',
    'mailboxes.fold': 'Collapse the mailbox column',
    'mailboxes.unfold': 'Expand the mailbox column',
    'mailboxes.open': 'Folders and filters',
    'mailboxes.close': 'Close the folders',

    'aiFilters.heading': 'AI filters',
    'aiFilters.needsDecision': 'Needs a decision',
    'aiFilters.commitments': 'Commitments',
    'aiFilters.deadlinesThisWeek': 'Deadlines this week',

    'mail.toolbar': 'Mail actions',
    'mail.compose': 'New message',
    'mail.reply': 'Reply',
    'mail.replyAll': 'Reply all',
    'mail.forward': 'Forward',
    'mail.archive': 'Archive',
    'mail.delete': 'Delete',
    'mail.flag': 'Flag',
    'mail.markUnread': 'Mark unread',
    'mail.move': 'Move',
    'mail.backToList': 'Back to the list',
    'mail.listColumn': 'Message list',
    'mail.readingColumn': 'What is open',
    'mail.listWidth': 'Message list width',
    'mail.listWidthHint':
        'Drag to change the list width. Double-click, or press Home, to return it to where it started.',

    'folder.inbox': 'Inbox',
    'folder.drafts': 'Drafts',
    'folder.sent': 'Sent',
    'folder.archive': 'Archive',
    'folder.junk': 'Junk',
    'folder.trash': 'Trash',
    'folder.flagged': 'Flagged',
    'folder.important': 'Important',
    'folder.all': 'All mail',
    'folder.outbox': 'Outbox',

    'list.label': 'Messages',
    'list.reading': 'Reading your mail…',
    'list.readingMore': 'Reading more…',
    'list.rowArriving': 'Reading this message again…',
    'list.wholeFolderRead': 'That is the whole of this folder.',
    'list.failed': 'This folder could not be read: {reason}.',
    'list.partiallyFailed': 'Part of this folder could not be read: {reason}.',
    'list.emptyFolder': 'There is no mail in this folder.',
    'list.nothingMatches': 'No message in this folder matches what the list is narrowed to.',
    'list.notSynchronizedYet':
        'Nothing has been taken into this deployment from this mailbox yet, so there is nothing to show. The folder is not empty — it has not been read.',
    'list.emptyWhileFailing':
        'This mailbox stopped synchronizing, so what is here may be less than the mail server holds.',

    'list.order': 'Order',
    'list.newestFirst': 'Newest first',
    'list.oldestFirst': 'Oldest first',
    'list.onlyUnread': 'Only unread',
    'list.onlyFlagged': 'Only flagged',
    'list.onlyWithAttachments': 'Only with attachments',
    'list.includeJunk': 'Include junk',
    'list.selectSeveral': 'Select several',
    'list.selectedCount': '{count} selected',

    'list.unread': 'Unread',
    'list.flagged': 'Flagged',
    'list.answered': 'Answered',
    'list.attachments': '{count} attached',
    'list.noSubject': 'No subject',
    'list.senderUnknown': 'No sender',

    'search.label': 'Find a message',
    'search.placeholder': 'Words from the message you are looking for',
    'search.blank': 'Type something to look for.',
    'search.submit': 'Search',
    'search.stop': 'Stop searching',
    'search.tooLong': 'That is longer than a search this deployment runs, which is {longest} characters.',
    'search.recent': 'Searched for before',
    'search.forgetRecent': 'Forget these',
    'search.everywhere': 'Searching every mailbox and folder.',
    'search.filters': 'Filters this search is under',
    'search.remove': 'Remove the filter {filter}',
    'search.narrow': 'Narrow this search',
    'search.addFilter': 'Add',
    'search.everyAccount': 'Every mailbox',
    'search.everyFolder': 'Every folder',
    'search.notAnAddress': 'That is not an address. Write the whole of it, as somebody@example.com.',
    'search.rangeSelectsNothing':
        'The last day falls before the first one, so nothing could have arrived between them.',

    'search.narrowing.account': 'Mailbox: {value}',
    'search.narrowing.folder': 'Folder: {value}',
    'search.narrowing.sender': 'From {value}',
    'search.narrowing.recipient': 'To {value}',
    'search.narrowing.receivedFrom': 'Arrived on or after {value}',
    'search.narrowing.receivedTo': 'Arrived on or before {value}',
    'search.narrowing.unread': 'Only unread',
    'search.narrowing.flagged': 'Only flagged',
    'search.narrowing.hasAttachments': 'Only with attachments',
    'search.narrowing.includeJunk': 'Including junk',

    'search.narrowing.accountField': 'In this mailbox',
    'search.narrowing.folderField': 'In this folder',
    'search.narrowing.senderField': 'From this address',
    'search.narrowing.recipientField': 'To this address',
    'search.narrowing.receivedFromField': 'Arrived on or after',
    'search.narrowing.receivedToField': 'Arrived on or before',

    'search.resultsLabel': 'What this search found',
    'search.searching': 'Searching your mail…',
    'search.readingMore': 'Reading more results…',
    'search.failed': 'This search could not be run: {reason}.',
    'search.partiallyFailed': 'Part of this search could not be read: {reason}.',
    'search.nothingFound': 'No message matches this search.',
    'search.widen': 'Search all your mail instead',
    'search.wholeSearchRead': 'That is everything this search found.',
    'search.mostResultsRead': 'That is as far as one search reaches. Narrow it to find what lies past here.',
    'search.whyItMatched': 'Why this matched:',
    'search.matchedByMeaning': 'Found by what it means rather than by these words.',
    'search.matchedInMail': 'Matched what this message is about rather than anything in its text.',
    'search.matchedBothWays': 'Matched these words and what this message is about.',
    'search.wordsOnlyInactive':
        'This deployment does not search by meaning, so these results carry the words you typed and nothing found by meaning alone.',
    'search.wordsOnlyDegraded':
        'Searching by meaning is not working on this deployment at the moment, so these results carry the words you typed only. Whoever runs it can look into that.',

    'connection.current': 'Every account is up to date.',
    'connection.behind': 'Some accounts are behind.',
    'connection.failing': 'Some accounts stopped synchronizing.',
    'connection.noAccounts': 'No mail account is configured for this owner yet.',
    'connection.retry': 'Try again',
    'connection.connecting': 'Reaching your deployment…',
    'connection.reconnecting': 'Your deployment did not answer. Trying again — attempt {attempt} of {total}.',
    'connection.lost': 'Your deployment has not answered after {total} attempts.',
    'connection.unreadable': 'Your deployment answered, but this client could not act on the answer: {reason}.',
    'connection.offline': 'This machine is offline. The client reconnects on its own when the network comes back.',

    'grant.heading': 'What this credential may not do here',
    'grant.readMail':
        'This credential may not read mail on this deployment, so no mailbox and no message is shown. Whoever runs the deployment can grant that.',
    'grant.askMail':
        'This credential may not ask questions of your mail on this deployment, so asking is not offered. Whoever runs the deployment can grant that.',

    'failure.unauthenticated': 'unauthenticated',
    'failure.unauthorized': 'unauthorized',
    'failure.unavailable': 'unavailable',
    'failure.unreadable': 'unreadable',

    'body.reading': 'Reading the message…',
    'body.failed': 'The message could not be read: {reason}.',

    'body.encryptedNotReadable': 'This message is encrypted and this deployment cannot read it.',
    'body.notStoredExceededSizeLimit':
        'This message was larger than this deployment keeps, so its body was not stored.',
    'body.notStoredAwaitingStorageHeadroom': 'This message is waiting for storage room before its body is kept.',

    'body.refusedNoHtmlPart': 'The sender wrote no formatted version of this message, so it is shown as words.',
    'body.refusedReductionFailed':
        'This deployment could not read the formatted version of this message, so it is shown as words.',
    'body.refusedNothingRenderable':
        'The formatted version of this message held nothing to draw, so it is shown as words.',
    'body.notReduced': 'This deployment sent no drawable version of this message, so it is shown as words.',

    'body.truncated': 'This message is longer than a reading pane draws, so it stops here.',
    'body.textTruncated': 'The words of this message were cut short by a limit this deployment applies.',
    'body.blockNotDrawn': 'A part of this message was written for a newer client than this one, so it is not drawn.',
    'body.tableRegion': 'A table in this message, scrollable sideways',
    'body.preformattedRegion': 'Preformatted text in this message, scrollable sideways',
    'body.pictureWithoutDescription': 'A picture the sender did not describe',

    'body.remoteContentRemoved':
        'This message asked to load content from another server. It was removed, so opening it reported nothing to the sender.',
    'body.remoteContentRemovedCount': 'References removed: {count}',
    'body.showRemotePictures': 'Load pictures from the sender',
    'body.showRemotePicturesReveals':
        'Loading them tells the sender that you opened this message. It is asked for this message alone and remembered nowhere.',
    'body.remotePicturesLoading': 'Loading them…',
    'body.showWithoutRemotePictures': 'Show the message without them',
    'body.remotePicturesShown': 'Pictures are being loaded from the sender for this message.',
    'body.remotePicturesShownCount': 'Pictures loaded from the sender: {count}',
    'body.undrawnPicturesCount': 'Pictures too large to draw: {count}',
    'body.quotedHistory': 'The conversation this message quoted',

    'thread.label': 'Conversation',
    'thread.open': 'Show the whole conversation',
    'thread.close': 'Back to the message',
    'thread.reading': 'Reading this conversation…',
    'thread.readingMore': 'Reading more of this conversation…',
    'thread.readMore': 'Read more of this conversation',
    'thread.wholeConversationRead': 'That is the whole of this conversation.',
    'thread.failed': 'This conversation could not be read: {reason}.',
    'thread.partiallyFailed': 'Part of this conversation could not be read: {reason}.',
    'thread.offline':
        'This machine is offline, so this conversation cannot be opened. It opens on its own once the network comes back.',
    'thread.empty': 'There is no message in this conversation that you are allowed to see.',
    'thread.messages': 'Messages in this conversation: {count}',
    'thread.wroteHere': 'Written by {names}',
    'thread.moreParticipants': 'More people wrote in this conversation than are named here.',
    'thread.moreNotAssembled':
        'This conversation is longer than one read assembles, so what is shown is the beginning of it.',
    'thread.storedIn': 'In {account}, {folder}',
    'thread.openOnItsOwn': 'Open this message on its own',
    'thread.messageBy': 'Message from {sender}',
    'thread.showEarlier': 'Show earlier messages ({count})',
    'thread.hideEarlier': 'Hide earlier messages',

    'message.nothingOpen': 'Open a message to read it here.',
    'message.reading': 'Reading this message…',
    'message.offline':
        'This machine is offline, so this message cannot be opened. It opens on its own once the network comes back.',
    'message.failed': 'This message could not be opened: {reason}.',
    'message.noSubject': 'No subject',
    'message.noAuthor': 'This message names nobody as its author.',
    'message.sentAt': 'Sent {when}',
    'message.sentAtUnknown': 'The sender wrote no date this client can read.',
    'message.receivedAt': 'Received {when}',
    'message.otherParticipants': 'Everybody else this message names ({count})',

    'participant.sender': 'Submitted by',
    'participant.replyTo': 'Reply to',
    'participant.to': 'To',
    'participant.cc': 'Copy to',
    'participant.bcc': 'Blind copy to',

    'sender.failed':
        'A receiving mail server checked who actually sent this message and reported that the author it displays did not hold.',
    'sender.recognized': 'This deployment recognizes the sender of this message.',
    'sender.authenticatedBy': 'Authenticated by {domain}, which is who actually sent it rather than the name above.',
    'sender.authenticatedByNobody': 'Nothing authenticated a sender for this message.',

    'attachments.heading': 'Files this message carries',
    'attachment.unnamed': 'Unnamed file',
    'attachment.download': 'Download {name}',
    'attachment.nameWasRewritten':
        'The sender wrote a file name this deployment would not use, so what is shown is the name it was given instead.',
    'attachment.arriving': 'How much of the file has arrived',
    'attachment.arrivingOf': '{arrived} of {whole}',
    'attachment.stop': 'Stop downloading',
    'attachment.saved': '{name} was downloaded.',
    'attachment.abandoned': 'The download was stopped, so nothing was saved.',
    'attachment.refusedUnauthenticated':
        'This deployment no longer accepts the credential, so the file was not downloaded. Sign in again.',
    'attachment.refusedUnauthorized':
        'This credential may not read mail on this deployment, so the file was not downloaded.',
    'attachment.refusedUnavailable': 'The deployment did not answer, so the file was not downloaded. Try again.',
    'attachment.refusedLargerThanDescribed':
        'The deployment sent more than this message said the file holds, so nothing was saved. Report this as a defect.',

    'carried.total': 'Everything attached comes to {size}.',
    'carried.encrypted': 'This message carries encrypted content somewhere.',
    'carried.unverifiedSignature': 'This message carries a signature, and nothing here has verified it.',
    'carried.unexpandedTnefPart':
        'This message carries a winmail.dat part, which was recorded without being opened, so whatever it holds is not listed above.',

    'scope.fragment': 'Asking about the part of this message you selected: “{fragment}”',
    'scope.wholeMessage': 'Ask about the whole message instead',

    'link.goesTo': 'goes to {host}',
    'link.warningDisplayedHostDiffers': 'This link does not go where its words say. It goes to {host}.',
    'link.warningAsciiHost': 'This link goes to {host}, which is written {asciiHost}.',
    'link.warningWorthChecking': 'This link is worth checking before you follow it. It goes to {host}.',
    'link.couldNotOpen': 'This link could not be opened.',
} as const;

/** Every message a screen may ask for. A key absent here does not compile at the call site. */
export type MessageKey = keyof typeof en;

/** What a language has to supply: exactly the keys above, no more and no fewer. */
export type Catalogue = Readonly<Record<MessageKey, string>>;
