// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailMutationOutcome } from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import type { ChangeAct } from './changeStandings';
import type { UndecidedChange } from './usePendingChanges';

// What a change is called on a screen. It sits apart from the provider that raises the sentences and from the block
// that draws them because both need it, and apart from `changeStandings.ts` because naming is a rendering decision
// rather than part of the rule that module states.
//
// The tables are split the way the sentences are: what did not happen is the act's own, and why is the deployment's
// answer and therefore one table for every act. That is what keeps a second act from costing a sentence per outcome,
// and it is why each reason below is written about mail rather than about a count — none of them has to agree with a
// number, so none of them needs a plural form.

/** What a message this act was refused for is called, selected by plural form for the count it happened to. */
export const refusalTitles: Readonly<Record<ChangeAct, Readonly<Record<Intl.LDMLPluralRule, MessageKey>>>> = {
    markRead: {
        zero: 'pendingChange.notMarkedRead.other',
        one: 'pendingChange.notMarkedRead.one',
        two: 'pendingChange.notMarkedRead.other',
        few: 'pendingChange.notMarkedRead.few',
        many: 'pendingChange.notMarkedRead.many',
        other: 'pendingChange.notMarkedRead.other',
    },
};

/** What the act itself is called, which is the half of an undecided change that says what was asked for. */
export const actNames: Readonly<Record<ChangeAct, MessageKey>> = {
    markRead: 'pendingChange.markRead',
};

/**
 * Why the deployment wrote nothing down, or `null` for the two outcomes nobody is told about.
 *
 * `recorded` wrote something down and is not a refusal at all. `already-in-destination` is the collision that
 * converges without a decision: the mailbox already says what was asked for, so nothing failed, the screen was right,
 * and reporting it would be the client apologising for having been correct.
 */
export const refusalReasons: Readonly<Record<MailMutationOutcome, MessageKey | null>> = {
    recorded: null,
    'already-in-destination': null,
    'message-not-found': 'pendingChange.messageNotFound',
    'destination-not-found': 'pendingChange.destinationNotFound',
    'account-no-longer-configured': 'pendingChange.accountNoLongerConfigured',
    'change-not-usable': 'pendingChange.changeNotUsable',
};

/** What a standing the deployment could not settle says, which is the other half of the two a person is shown. */
export const standingReasons: Readonly<Record<UndecidedChange['standing'], MessageKey>> = {
    exhausted: 'pendingChange.exhausted',
    unanswered: 'pendingChange.unanswered',
};

/** How many changes are still on their way, said in the form the count needs. */
export const waitingCounts: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'pendingChange.waiting.other',
    one: 'pendingChange.waiting.one',
    two: 'pendingChange.waiting.other',
    few: 'pendingChange.waiting.few',
    many: 'pendingChange.waiting.many',
    other: 'pendingChange.waiting.other',
};
