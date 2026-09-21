// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { DiscoveryRunAsk } from '@mailfathom/client-backend';
import type { AskedQuestion } from '../workspace/askScope';
import type { MailScope } from '../workspace/mailScope';

// What a question the intent field recorded becomes on the wire. The field keeps the pair — the words and the scope
// they were asked under — and the run takes five fields, so this is the one place the two meet.
//
// **A scope is stated rather than defaulted.** Every field the deployment requires is sent, and what widens a question
// is the empty list rather than an absent one: a run asked with a field left out would read whatever the service
// defaults to, which is mail the person never chose.
//
// **A passage narrows to the message it was cut from.** The route carries a conversation and a list of messages and
// nothing finer, so the paragraph somebody highlighted reaches the run as its own message. That is a difference
// between the field's scope and the run's rather than something lost here: what a narrower scope would need is a field
// on the route, which is service work with an issue of its own.

/** The run one recorded question would be asked as. */
export function runAskedFor(asked: AskedQuestion): DiscoveryRunAsk {
    return { question: asked.question, ...scopedTo(asked) };
}

/** Everything but the words: which mail the question may be answered from. */
function scopedTo(asked: AskedQuestion): Omit<DiscoveryRunAsk, 'question'> {
    const nothingNarrower = { accounts: [], folders: [], thread: null, emails: [] } as const;

    switch (asked.scope.kind) {
        case 'mail':
            return { ...nothingNarrower, ...mailboxesOf(asked.scope.scope) };

        case 'thread':
            return { ...nothingNarrower, thread: asked.scope.threadId };

        case 'selection':
            return { ...nothingNarrower, emails: asked.scope.messages };

        case 'message':
            return { ...nothingNarrower, emails: [asked.scope.messageId] };

        case 'fragment':
            return { ...nothingNarrower, emails: [asked.scope.messageId] };
    }
}

/**
 * The accounts and folders one mailbox scope names, in the spelling the service reads them in.
 *
 * A role is a folder reference rather than a scope of its own — `role:Inbox` is how every inbox at once is named on
 * this surface, which is the same spelling the timeline is read under — so the four scopes the client offers become two
 * lists without anything here having to know which folders play which role.
 */
function mailboxesOf(scope: MailScope): { readonly accounts: readonly string[]; readonly folders: readonly string[] } {
    switch (scope.kind) {
        case 'everything':
            return { accounts: [], folders: [] };

        case 'role':
            return { accounts: [], folders: [`role:${scope.role}`] };

        case 'account':
            return { accounts: [scope.accountId], folders: [] };

        case 'folder':
            return { accounts: [scope.accountId], folders: [scope.alias] };
    }
}
