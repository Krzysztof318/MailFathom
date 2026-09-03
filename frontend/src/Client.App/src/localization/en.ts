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
    'shell.mailboxes': 'Mailboxes',
    'shell.tabMode': 'Tab mode',
    'shell.tabModeTooNarrow': 'available on a wider screen',
    'control.notBuiltYet': '{control} — not built yet',
    'ai.badge': 'AI',
    'confirm.undoableFor.one': 'You can take this back for {count} second afterwards.',
    'confirm.undoableFor.few': 'You can take this back for {count} seconds afterwards.',
    'confirm.undoableFor.many': 'You can take this back for {count} seconds afterwards.',
    'confirm.undoableFor.other': 'You can take this back for {count} seconds afterwards.',
    'proposal.offered': 'MailFathom suggests this. Nothing has happened yet.',
    'proposal.reason': 'Why: {reason}',
    'proposal.impact': 'What would change: {impact}',
    'proposal.confirmed': 'You are asked to confirm this before anything changes.',
    'proposal.unconfirmed': 'This changes nothing outside this client, so it happens as soon as you agree.',
    'proposal.notNow': 'Not now',

    'theme.automatic': 'Auto',
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
        'Name your organization\u2019s own MailFathom server. Indexing and analysis happen on it — nothing reaches the cloud without your say-so.',
    'signIn.revealPassword': 'Show',
    'signIn.hidePassword': 'Hide',
    'signIn.revealPasswordControl': 'Show the password',
    'signIn.hidePasswordControl': 'Hide the password',

    'signIn.title': 'Connect your mailbox',
    'signIn.explanation': 'What you sign in with goes to the server you name, and nowhere else.',
    'signIn.userName': 'Login',
    'signIn.userNameExample': 'k.kowalska@example.com',
    'signIn.password': 'Password',
    'signIn.submit': 'Connect',
    'signIn.presenting': 'Connecting to {address}…',
    'signIn.abandon': 'Stop trying',
    'signIn.incomplete': 'Type the login and the password your deployment gave you.',
    'signIn.userNameHasColon':
        'A login cannot contain a colon, which is what separates it from the password when it is sent.',
    'signIn.tooLong': 'That login or password is longer than this client will present. Check what was pasted in.',
    'signIn.credentialRefused': 'The login or the password is not accepted by this deployment.',
    'signIn.basicNotOffered':
        'This deployment does not accept a login and a password. Whoever runs it has to enable that before you can sign in here.',
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

    'connect.address': 'Server',
    'connect.addressConfigured':
        'The server address was supplied when this client was installed, so it cannot be changed here.',
    'connect.addressExample': 'mailfathom.example.com:8443',
    'connect.addressHint': 'The port is optional — without one this client reaches {port}.',
    'connect.clearText': 'Reach this deployment over plain HTTP',
    'connect.clearTextConfigured':
        'This was set for you when the client was installed, so it is not yours to change here. Whoever configured it decides it.',
    'connect.clearTextExplanation':
        'Your password is encoded rather than encrypted, on every request. Anybody between this client and the deployment can read it. Leave this off unless the network between them is yours.',
    'connect.clearTextInForce':
        'TLS is off. The login, the password, and every message read travel in the clear. Use this only inside a network you control or over a VPN.',
    'connect.portHint': 'port {port}',
    'connect.advanced': 'Advanced',
    'connect.withoutTls': 'no TLS',
    'connect.protocol': 'Protocol',
    'connect.protocolOverTls': 'HTTPS, over TLS',
    'connect.protocolClearText': 'HTTP, unencrypted',
    'connect.host': 'Address',
    'connect.port': 'Port',
    'connect.portDefault': '{port} (default)',
    'connect.certificate': 'Certificate check',
    'connect.certificateChecked': 'Required',
    'connect.certificateNone': 'None — nothing is encrypted',
    'connect.nothingNamed': 'Nothing named yet',
    'connect.blank': 'Name the deployment that holds your mail.',
    'connect.malformed': 'That is not an address. Name the host it answers on, and a port where it uses one.',
    'connect.clearTextRefused':
        'That address is plain HTTP, which this client will not send a password over until you say it may.',
    'connect.unavailable': 'Nothing answered there. Check the address, and check that the deployment is running.',
    'connect.unreadable': 'Something answered there, but not as MailFathom.',

    'configuration.refused': 'This client is configured wrongly',
    'configuration.addressMalformed':
        'The server address it was given is not an address. It has to be the host the deployment answers on, and a port where it uses one.',
    'configuration.addressNeedsClearTextPermission':
        'The server address it was given is plain HTTP, and nothing permitted an unsecured connection. Either name an https address or permit clear text beside it.',
    'configuration.clearTextContradictsAddress':
        'It was told to permit an unsecured connection and given an https address, which are two different answers to one question. Remove whichever of the two is wrong.',
    'configuration.permissionNotABoolean':
        'The permission for an unsecured connection has to read true or false, and what it was given reads as neither.',
    'configuration.whereItIsStated':
        'Both settings are read from the arguments MailFathom was started with, from its environment, and from client.conf beside its own configuration, in that order.',

    'preferences.notStated':
        'That change was not saved to the deployment, so it holds on this machine alone until the next one succeeds.',

    'settings.title': 'Settings',
    'settings.close': 'Close settings',
    'settings.sections': 'Settings sections',
    'settings.profile': 'Profile',
    'settings.application': 'Application',
    'settings.name': 'Full name',
    'settings.nameNotYours':
        'Whoever runs this deployment keeps your name, so it is shown here rather than offered for you to change.',
    'settings.nameNotAcceptable':
        'That name was not accepted. It cannot be blank or longer than 128 characters, and it cannot be one somebody else on this deployment already goes by.',
    'settings.nameNotStored': 'That name was not saved to the deployment, so it still holds you under the old one.',
    'settings.choosePicture': 'Picture',
    'settings.removePicture': 'Remove',
    'settings.pictureBounds': 'JPG/PNG, up to 1 MB',
    'settings.pictureNotAnImageKind': 'That file is neither a JPEG nor a PNG, so it was not sent.',
    'settings.pictureTooLarge': 'That picture is larger than 1 MB, so it was not sent.',
    'settings.pictureNotStored': 'That picture was not saved to the deployment, so you are still drawn as you were.',
    'settings.profileHeld':
        'Your name and picture are held by the deployment you signed in to, so they follow you between machines. Neither is sent to your mail server.',
    'settings.messageView': 'Message view',
    'settings.expandWholeThread': 'Expand the whole thread automatically',
    'settings.expandWholeThreadExplanation':
        'Without it, a conversation opens at the message you chose and the rest sit behind a control.',
    'settings.privacy': 'Privacy',
    'settings.telemetryWithheld': 'Do not send telemetry',
    'settings.telemetryExplanation': 'Error diagnostics and usage statistics stay on this device.',
    'settings.telemetryWithheldWarning':
        'Without telemetry, support is harder — problems have to be described by hand, and some failures cannot be reproduced by whoever is helping you.',
    'settings.telemetryDestination':
        'Sent to {address}, which forwards it to whichever collector it is configured with. It carries which screens you opened and how long they took, never your mail, your addresses, your folders, or your password.',
    'settings.telemetryNotForwarded':
        'This deployment forwards no telemetry, so this client sends none and there is nothing to turn off.',
    'settings.telemetryUnanswered':
        'Waiting for this deployment to say whether it forwards telemetry. Until it answers, your own decision is what holds.',

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

    'compose.titleNew': 'New message',
    'compose.titleReply': 'Reply',
    'compose.titleReplyAll': 'Reply to everyone',
    'compose.titleForward': 'Forward',
    'compose.close': 'Close the message',
    'compose.reading': 'Reading the message you are answering…',
    'compose.from': 'From',
    'compose.to': 'To',
    'compose.cc': 'Cc',
    'compose.bcc': 'Bcc',
    'compose.showCopies': 'Write a copy or a blind copy as well',
    'compose.copyHeaders': 'Cc · Bcc',
    'compose.addRecipient': 'Add a recipient…',
    'compose.removeRecipient': 'Remove {address} from {header}',
    'compose.notAnAddress': 'That is not an address yet. An address looks like somebody@example.com.',
    'compose.alreadyAddressed': '{address} is written here already.',
    'compose.tooManyAddresses': 'One header takes at most {count} addresses.',
    'compose.subject': 'Subject',
    'compose.subjectPlaceholder': 'Message subject',
    'compose.subjectOfAnAnswer': 'Your deployment writes the subject of an answer, so it is not edited here.',
    'compose.words': 'Message',
    'compose.wordsPlaceholder': 'Write your message',
    'compose.attach': 'Attach',
    'compose.attachedFiles': 'Attached files',
    'compose.removeFile': 'Remove {name}',
    'compose.saveDraft': 'Save draft',
    'compose.shortcutSends': 'Ctrl+Enter sends',
    'compose.send': 'Send',
    'compose.sendAnyway': 'Send anyway',
    'compose.backToEditing': 'Back to writing',
    'compose.confirmQuestion': 'Send this message?',
    'compose.confirmTo': 'To {addresses}',
    'compose.confirmCc': 'Copy to {addresses}',
    'compose.confirmBcc': 'Blind copy to {addresses}',
    'compose.confirmNobody': 'It is addressed to nobody.',
    'compose.confirmSubject': 'Subject: {subject}',
    'compose.confirmNoSubject': 'none',
    'compose.confirmRecallable': 'You can take it back until your deployment has handed it to the mail server.',
    'compose.cautionNoRecipient': 'Nobody is addressed, so there is nowhere for it to go.',
    'compose.cautionNoSubject': 'It goes out without a subject.',
    'compose.cautionNoWords': 'It goes out with nothing written in it.',
    'compose.discardQuestion': 'Discard this message?',
    'compose.discardExplanation': 'What you have written goes, along with the draft your deployment is holding for it.',
    'compose.discardIsFinal': 'Nothing files it first, so there is no way back to these words.',
    'compose.discard': 'Discard',
    'compose.saving': 'Filing the draft…',
    'compose.saved': 'Draft filed in your own drafts.',
    'compose.attaching': 'Attaching {name}…',
    'compose.sending': 'Sending…',
    'compose.queued': 'Queued to go out.',
    'compose.withdraw': 'Take it back',
    'compose.withdrawn': 'Taken back before it went out.',
    'compose.alreadyBeingSent': 'It is already being sent, so it could not be taken back.',
    'compose.pastRecall': 'It has gone out, so it cannot be taken back.',
    'compose.noSuchSend': 'Your deployment no longer holds that send.',
    'compose.offline':
        'This machine is offline. What you write is kept here, and it can be filed or sent once the network is back.',
    'compose.refusedSendingNotEnabled': 'This deployment does not send mail. Whoever runs it can turn sending on.',
    'compose.refusedRecipient':
        'Your deployment refused one of the addresses. Whoever runs it decides which addresses mail may go to.',
    'compose.refusedCeiling':
        'A spending ceiling has been reached, so nothing goes out until the window it counts over turns over or whoever runs the deployment raises it.',
    'compose.refusedContent':
        'Screening refused what this message carries. Changing what it says, or what it attaches, is what would change that.',
    'compose.refusedNotScanned':
        'Part of this message could not be screened, so it was not sent. Taking off what could not be read is what would change that.',
    'compose.refusedScreeningUnavailable':
        'Screening is not answering, so nothing goes out until it does. The message is still here.',
    'compose.refusedForAnotherReason':
        'Your deployment refused to send it. Whoever runs it can say why from its own log.',
    'compose.failedUnauthenticated':
        'This client is no longer signed in. Sign in again — what you wrote is still here.',
    'compose.failedUnauthorized': 'This credential may not do that on this deployment. Whoever runs it can grant that.',
    'compose.failedUnavailable': 'Your deployment did not answer. What you wrote is kept here; try again.',
    'compose.failedUnreadable':
        'Your deployment answered, but this client could not act on the answer. That is a defect worth reporting.',

    'tabs.strip': 'Open tabs',
    'tabs.close': 'Close {title}',
    'tabs.closeAll': 'Close everything that is open',
    'tabs.closeAllQuestion': 'Close every tab?',
    'tabs.closeAllOpen.one': 'Open tab: {count}.',
    'tabs.closeAllOpen.few': 'Open tabs: {count}.',
    'tabs.closeAllOpen.many': 'Open tabs: {count}.',
    'tabs.closeAllOpen.other': 'Open tabs: {count}.',
    'tabs.closeAllDraft': 'An unsent draft will be discarded.',
    'tabs.closeAllIsFinal': 'Nothing reopens them together — each one is opened again from the list.',
    'tabs.closeAllConfirm': 'Close them all',
    'tabs.closeAllCancel': 'Cancel',
    'tabs.nothingOpen': 'Nothing is open',
    'tabs.nothingOpenExplanation': 'Pick a message from the list and it opens as a tab of its own.',
    'tabs.reopenLastRead': 'Open the last message read',

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

    'list.filters': 'Filters',
    'list.filtersInForce': 'Active filters: {count}',
    'list.noFiltersInForce': 'No active filters',
    'list.clearFilters': 'Clear filters',
    'list.dateRange': 'Date range',
    'list.rangeToday': 'Today',
    'list.rangeLastSevenDays': 'Last 7 days',
    'list.rangeLastThirtyDays': 'Last 30 days',
    'list.rangeThisYear': 'This year',
    'list.receivedFromField': 'from',
    'list.receivedToField': 'to',
    'list.rangeSelectsNothing':
        'The end of the range falls before its start, so nothing could have arrived between them.',

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
    'grant.markMailRead':
        'This credential may not change a flag on your mail server, so opening a message leaves it unread there and this client shows what the server last reported. Whoever runs the deployment can grant that.',

    'grant.composeMail':
        'This credential may not write a draft on this deployment, so writing a message is not offered. Whoever runs the deployment can grant that.',
    'grant.sendMail':
        'This credential may not send mail from this deployment, so a message can be written and filed as a draft but not sent. Whoever runs the deployment can grant that.',
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

    'fullHtml.show': 'Show the full HTML version',
    'fullHtml.question': 'Show the full HTML?',
    'fullHtml.whatItCanCarry':
        'The sender wrote this markup themselves. It can carry tracking pixels, a layout imitating a brand you know, and links that go somewhere other than they say.',
    'fullHtml.whatIsBlocked':
        'Nothing in it can run, and nothing in it reaches the sender until you ask for their pictures.',
    'fullHtml.stayReduced': 'Stay with the reduced version',
    'fullHtml.confirm': 'Show the HTML',
    'fullHtml.surface': "The sender's own version of this message",
    'fullHtml.mark': 'HTML',
    'fullHtml.frame': "The sender's own markup, drawn in isolation",
    'fullHtml.sentBy': '{author} · {when}',
    'fullHtml.close': 'Close this view',
    'fullHtml.reading': "Reading the sender's own version…",
    'fullHtml.failed': "The sender's own version could not be read: {reason}.",
    'fullHtml.noMarkup': 'The sender wrote no formatted version of this message, so there is nothing to show here.',
    'fullHtml.truncated': 'This message is longer than one read returns, so it stops here.',
    'fullHtml.picturesTruncated':
        'This message carried more pictures of its own than one view holds, so some of them are missing.',
    'fullHtml.cannotRun': 'This message cannot run anything: the frame it is drawn in permits no script at all.',
    'fullHtml.reachesNobody':
        'It carries no address that would reach the sender — every one of them was removed before this message was sent to the client.',
    'fullHtml.picturesAsked':
        'Pictures are being loaded from the sender for this message, so their servers can tell it was opened. Nothing about that is kept: leaving this message and coming back asks again.',

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
    'thread.openedFromList': 'Opened from the list',
    'thread.landedFromResult': 'Brought here from a search result',

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

    'attachments.list': 'Files this message carries',
    'attachments.downloadAll': 'Download all',
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

    'blocking.progress': 'How far the operation has got',
    'blocking.progressReading': '{percentage} — do not close this window',
    'blocking.noKnownFinish': 'No known finish time',
    'blocking.doNotClose': 'Do not close the application — the operation is still running.',
    'blocking.cancel': 'Cancel',
    'blocking.stopQuestion': 'Are you sure you want to stop?',
    'blocking.continue': 'Continue the operation',
    'blocking.stop': 'Yes, stop',

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
