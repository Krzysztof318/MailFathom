// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailSynchronizationState } from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';

// What a local copy's freshness is called, and which readings ask somebody to do something. It says the same thing
// about an account and about one folder of one, because the service answers both with the same four states — and two
// screens saying it in two vocabularies would be two answers to one question.

const stateLabels: Readonly<Record<MailSynchronizationState, MessageKey>> = {
    Synchronized: 'account.synchronized',
    Failing: 'account.failing',
    Unreachable: 'account.unreachable',
    NeverSynchronized: 'account.neverSynchronized',
};

// The two states in which a copy is not going to catch up on its own. They are read before the lag below, because
// something nothing is fixing says what a merely lagging one does not, and "behind" would let it wait unnoticed.
const brokenStates: readonly MailSynchronizationState[] = ['Failing', 'Unreachable'];

/** What to call the state of a local copy: what its last finished attempt did, unless it left mail behind. */
export function synchronizationStateLabel(state: MailSynchronizationState, behind: boolean): MessageKey {
    return state === 'Synchronized' && behind ? 'account.behind' : stateLabels[state];
}

/** Whether the state is one nothing is going to resolve on its own, which is what a screen says out loud. */
export function needsAttention(state: MailSynchronizationState): boolean {
    return brokenStates.includes(state);
}

/** Whether there is nothing to say about the copy: its last attempt succeeded and it left nothing behind. */
export function isCurrent(state: MailSynchronizationState, behind: boolean): boolean {
    return state === 'Synchronized' && !behind;
}
