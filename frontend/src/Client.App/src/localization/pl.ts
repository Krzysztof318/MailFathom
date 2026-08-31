// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { Catalogue } from './en';

// The one part of this repository that is deliberately not in English, which is why `.config/typos.toml` excludes this
// file from the spell check: a dictionary of English reads a Polish word as a misspelling of the English one it
// resembles. The annotation is what holds it to `en.ts` — a key missing here, or one written here that English does not
// declare, fails `pnpm typecheck`.

export const pl: Catalogue = {
    'shell.title': 'MailFathom',
    'shell.language': 'Język',

    'accounts.reading': 'Odczytywanie kont…',
    'accounts.refreshing': 'To wdrożenie odświeża lokalną kopię tych kont.',
    'accounts.notRefreshing': 'To wdrożenie nie odświeża lokalnej kopii tych kont.',
    'accounts.failed': 'Nie udało się odczytać kont: {reason}.',

    'failure.unauthenticated': 'brak uwierzytelnienia',
    'failure.unauthorized': 'brak uprawnień',
    'failure.unavailable': 'usługa niedostępna',
    'failure.unreadable': 'odpowiedź nie do odczytania',

    'synchronization.neverSynchronized': 'nigdy nie zsynchronizowano',
    'synchronization.synchronized': 'zsynchronizowano',
    'synchronization.failing': 'niepowodzenie',
    'synchronization.unreachable': 'nieosiągalne',

    'account.stateBehind': '{state}, zaległości',
    'account.lastSynchronized': 'ostatnia synchronizacja {when}',
};
