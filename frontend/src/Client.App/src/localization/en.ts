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

    'accounts.reading': 'Reading accounts…',
    'accounts.refreshing': 'This deployment refreshes the local copy of these accounts.',
    'accounts.notRefreshing': 'This deployment is not refreshing the local copy of these accounts.',
    'accounts.failed': 'The accounts could not be read: {reason}.',

    'failure.unauthenticated': 'unauthenticated',
    'failure.unauthorized': 'unauthorized',
    'failure.unavailable': 'unavailable',
    'failure.unreadable': 'unreadable',

    'synchronization.neverSynchronized': 'never synchronized',
    'synchronization.synchronized': 'synchronized',
    'synchronization.failing': 'failing',
    'synchronization.unreachable': 'unreachable',

    'account.stateBehind': '{state}, behind',
    'account.lastSynchronized': 'last synchronized {when}',
} as const;

/** Every message a screen may ask for. A key absent here does not compile at the call site. */
export type MessageKey = keyof typeof en;

/** What a language has to supply: exactly the keys above, no more and no fewer. */
export type Catalogue = Readonly<Record<MessageKey, string>>;
