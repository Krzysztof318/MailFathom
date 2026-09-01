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
    'accounts.notRefreshing': 'This deployment is not refreshing the local copy of these accounts.',
    'accounts.failed': 'The accounts could not be read: {reason}.',

    'connection.current': 'Every account is up to date.',
    'connection.behind': 'Some accounts are behind.',
    'connection.failing': 'Some accounts stopped synchronizing.',
    'connection.noAccounts': 'No mail account is configured for this owner yet.',
    'connection.retry': 'Try again',

    'failure.unauthenticated': 'unauthenticated',
    'failure.unauthorized': 'unauthorized',
    'failure.unavailable': 'unavailable',
    'failure.unreadable': 'unreadable',
} as const;

/** Every message a screen may ask for. A key absent here does not compile at the call site. */
export type MessageKey = keyof typeof en;

/** What a language has to supply: exactly the keys above, no more and no fewer. */
export type Catalogue = Readonly<Record<MessageKey, string>>;
