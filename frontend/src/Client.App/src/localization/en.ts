// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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

    'theme.system': 'Follow the system',
    'theme.light': 'Light',
    'theme.dark': 'Dark',

    'space.discover': 'Discover',
    'space.mail': 'Mail',
    'space.cases': 'Cases',
    'space.pending':
        'This space is not built yet. What is here is the frame around it: its address, its navigation, and the scope every question is asked against.',

    'intent.label': 'Ask your mail',
    'intent.placeholder': 'What do you want to ask your mail?',

    'scope.mailbox': 'Mailbox in scope',
    'scope.allMailboxes': 'All mailboxes',

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
    'folders.stored': '{count} held here',

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
