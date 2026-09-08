// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ClientNotification, NotificationCause } from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import type { Locale } from '../localization/locale';
import type { Translate } from '../localization/useLocalization';

// What a notification says, in the language the person reading it has. The deployment sends the condition and the
// numbers it is stated with; the sentence is the client's, which is what `frontend/src/AGENTS.md` § *The package
// boundary* means by language being a rendering decision — the same row read in two languages is one record and two
// screens rather than two records.
//
// Each cause is a sentence with holes rather than fragments joined here, because where a count falls in a sentence and
// what form the noun around it takes are both the language's business. `Intl.PluralRules` selects the form, which
// Polish needs for every counted sentence below and English hides.
//
// **The English the deployment sent is the fallback and nothing more.** A record written before this deployment kept
// conditions has no statement, and so does one raised for a condition a newer deployment knows and this client does
// not. Neither is a defect: the row is still worth drawing, and the service's own two lines are what it is drawn with
// until retention has taken the first kind and an upgrade has answered the second.

/** The two lines a row is drawn with, whichever of the two sources they came from. */
export interface NotificationWords {
    readonly title: string;
    readonly body: string;
}

/** How each cause is titled, which no cause counts anything in. */
const causeTitles: Readonly<Record<NotificationCause, MessageKey>> = {
    MailArrived: 'notifications.said.mailArrived.title',
    SynchronizationIncomplete: 'notifications.said.synchronizationIncomplete.title',
    CredentialRefused: 'notifications.said.credentialRefused.title',
};

/**
 * How each cause's second line is said.
 *
 * A cause that counts nothing carries one sentence, and a cause that counts carries the forms its count takes. The
 * two shapes are a union rather than an optional field so that a cause cannot declare forms nothing selects between,
 * and so that reading one is a switch rather than a check for which field is present.
 */
type CauseBody =
    { readonly said: MessageKey } | { readonly counted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> };

const causeBodies: Readonly<Record<NotificationCause, CauseBody>> = {
    MailArrived: { counted: forms('mailArrived') },
    SynchronizationIncomplete: { counted: forms('synchronizationIncomplete') },
    CredentialRefused: { said: 'notifications.said.credentialRefused.body' },
};

/**
 * Says what one notification says, in the reader's own language where the deployment named a condition.
 *
 * @param notification What happened.
 * @param locale The language and the region the count is selected and formatted under.
 * @param translate Reads one message under that language.
 * @returns The two lines the row is drawn with.
 */
export function wordNotification(
    notification: ClientNotification,
    locale: Locale,
    translate: Translate,
): NotificationWords {
    const statement = notification.statement;

    if (statement === null) {
        return { title: notification.title, body: notification.body };
    }

    // Which number the sentence is counted by is the cause's own: mail counts what arrived, and an unfinished run
    // counts against how much it scheduled, which is the number the noun in that sentence agrees with.
    const counted = statement.cause === 'SynchronizationIncomplete' ? statement.outOf : statement.counted;
    const body = causeBodies[statement.cause];

    return {
        title: translate(causeTitles[statement.cause]),
        body:
            'said' in body
                ? translate(body.said)
                : translate(body.counted[new Intl.PluralRules(locale).select(counted ?? 0)], {
                      count: number(statement.counted, locale),
                      outOf: number(statement.outOf, locale),
                  }),
    };
}

/** The four forms one cause's second line is counted in, which every counted cause declares the same way. */
function forms(cause: 'mailArrived' | 'synchronizationIncomplete'): Readonly<Record<Intl.LDMLPluralRule, MessageKey>> {
    return {
        zero: `notifications.said.${cause}.body.other`,
        one: `notifications.said.${cause}.body.one`,
        two: `notifications.said.${cause}.body.other`,
        few: `notifications.said.${cause}.body.few`,
        many: `notifications.said.${cause}.body.many`,
        other: `notifications.said.${cause}.body.other`,
    };
}

/**
 * Writes one of a statement's numbers in the reader's own digits and grouping.
 *
 * A hole a cause does not fill is written as nothing rather than as a zero, so a sentence that names it would read as
 * an obviously missing value instead of as a count somebody could act on.
 */
function number(value: number | null, locale: Locale): string {
    return value === null ? '' : new Intl.NumberFormat(locale).format(value);
}
