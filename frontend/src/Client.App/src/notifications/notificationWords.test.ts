// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { ClientNotification, NotificationStatement } from '@mailfathom/client-backend';
import { catalogues, type Locale } from '../localization/locale';
import type { Translate } from '../localization/useLocalization';
import { wordNotification } from './notificationWords';

// What the deployment sends is a condition and its numbers, so this is where it becomes a sentence — and the sentence
// is asserted as the literal words each language actually reads, in both of them. Anything less would pass for a
// client that answered a Polish reader in English, which is the defect this exists to refuse.
//
// The catalogue is read directly rather than through a rendered provider, because `wordNotification` takes the
// translation as a parameter: that is the seam, and reaching it needs no screen.

const englishArrival = {
    id: 'n-1',
    kind: 'Mail',
    title: 'New mail',
    body: '3 new messages arrived.',
    source: 'work',
    target: { kind: 'Screen', screen: 'Mail' },
    occurredAt: '2026-09-04T11:55:00+00:00',
    read: false,
} as const;

function said(statement: NotificationStatement | null, locale: Locale) {
    const notification: ClientNotification = { ...englishArrival, statement };

    return wordNotification(notification, locale, translating(locale));
}

// The digits a count is written in, asked of `Intl` the same way the sentence asks it rather than typed out here.
const digits = new Intl.NumberFormat('pl');

/** Reads the catalogue the way the provider does, which is a lookup and a hole filled. */
function translating(locale: Locale): Translate {
    return (key, values) =>
        catalogues[locale][key].replace(/\{(\w+)\}/gu, (hole: string, name: string) => values?.[name] ?? hole);
}

describe('wordNotification', () => {
    it('says arrived mail in English, counting what the run committed', () => {
        expect(said({ cause: 'MailArrived', counted: 3, outOf: null }, 'en')).toEqual({
            title: 'New mail',
            body: '3 new messages arrived.',
        });
    });

    // Polish inflects both the verb and the noun by the count, which is what `Intl.PluralRules` selects between and
    // what a hand-written plural would get wrong for every number but the one it was written for. Which form it lands
    // on is named as the entry rather than as the words that entry holds: the catalogue is the one place Polish prose
    // is written, and a copy of a sentence here would be a second place to keep it in step. The title beside it stays
    // literal, so the whole assertion is still that the Polish catalogue reached the screen rather than the English.
    it.each([
        [1, 'notifications.said.mailArrived.body.one'],
        [3, 'notifications.said.mailArrived.body.few'],
        [12, 'notifications.said.mailArrived.body.many'],
    ] as const)('says arrived mail in Polish in the form its count takes: %i', (count, form) => {
        expect(said({ cause: 'MailArrived', counted: count, outOf: null }, 'pl')).toEqual({
            title: 'Nowa poczta',
            body: translating('pl')(form, { count: digits.format(count) }),
        });
    });

    it('says an unfinished run in English, against how much it had scheduled', () => {
        expect(said({ cause: 'SynchronizationIncomplete', counted: 2, outOf: 5 }, 'en')).toEqual({
            title: 'Some mail could not be fetched',
            body: '2 of 5 folders did not finish. MailFathom will try again.',
        });
    });

    // The noun agrees with what the run scheduled rather than with what failed, so one folder out of one reads
    // differently from one folder out of five — which is the case a sentence counted by the wrong number gets wrong.
    it.each([
        [1, 1, 'notifications.said.synchronizationIncomplete.body.one'],
        [2, 5, 'notifications.said.synchronizationIncomplete.body.many'],
    ] as const)('says an unfinished run in Polish against its scheduled count: %i of %i', (failed, scheduled, form) => {
        expect(said({ cause: 'SynchronizationIncomplete', counted: failed, outOf: scheduled }, 'pl')).toEqual({
            title: 'Nie udało się pobrać części poczty',
            body: translating('pl')(form, { count: digits.format(failed), outOf: digits.format(scheduled) }),
        });
    });

    it.each([
        ['en', 'This account needs signing in again'],
        ['pl', 'To konto wymaga ponownego zalogowania'],
    ] as const)('says a refused credential in %s without counting anything', (locale, title) => {
        expect(said({ cause: 'CredentialRefused', counted: null, outOf: null }, locale).title).toBe(title);
    });

    // A record written before this deployment kept conditions, and one raised for a condition a newer deployment
    // knows, both reach a screen the same way: as the service's own English, which is better than a row nobody draws.
    it('falls back to what the deployment sent where it named no condition', () => {
        expect(said(null, 'pl')).toEqual({ title: 'New mail', body: '3 new messages arrived.' });
    });
});
