// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useState, type ReactNode } from 'react';
import {
    changeMailFlags,
    mostMessagesPerMutation,
    moveMail,
    readMailFolders,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailFathomTransport,
    type MailFolderDirectory,
    type MailMutationResult,
} from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useToasts } from '../toasts/useToasts';
import { destinationsFor, filingFor, refusalFor, type MoveDestination } from './mailboxDestinations';
import {
    MailboxActsContext,
    nothingActed,
    type ActedMessage,
    type MailboxAct,
    type MailboxActs,
} from './useMailboxActs';

// Performing the five acts, which is the one place in this client that changes somebody's mailbox from the Mail space.
// Nothing here reaches a mail server: each act writes a durable record through `/api/client` and answers, and the
// account's own convergence pass is what issues the IMAP command. So an account nobody can connect to leaves the act
// pending rather than failing it, and what is held below is what was asked for rather than what has been observed.
//
// **Every act reports what it came to**, in the toast surface rather than on the control that was pressed, and the
// three that file a message elsewhere offer the way back as that toast's single action. Taking one back is the reverse
// move rather than a withdrawal of the first: a change already on its way to a mail server cannot be unsaid, and
// pretending otherwise would leave the mailbox and the screen disagreeing.
//
// **What each act may do at all is answered before it is offered**, which is `mailboxDestinations.ts`. An account with
// no archive folder is a control that says so rather than one that fails once it has been pressed.

/** What the toast reporting a finished act is titled, exhaustive by its own type. */
const actReported: Readonly<Record<MailboxAct, MessageKey>> = {
    flag: 'act.flagged',
    markUnread: 'act.markedUnread',
    archive: 'act.archived',
    delete: 'act.deleted',
    move: 'act.filed',
};

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
};

// How many messages are counted in each of the forms a language has for the noun. Selected rather than spelled, for
// the reason `mailSpace/TabStrip.tsx` gives: Polish needs three forms and English hides that it needs two.
const messagesCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'act.messages.other',
    one: 'act.messages.one',
    two: 'act.messages.other',
    few: 'act.messages.few',
    many: 'act.messages.many',
    other: 'act.messages.other',
};

/** What is held, and whose it is, so one person's pending acts never outlive the credential they were made under. */
interface Held {
    readonly session: ClientSession | null;
    readonly directory: MailFolderDirectory | null;
    readonly asked: ReadonlyMap<string, MailboxAct>;
}

const heldForNobody: Held = { session: null, directory: null, asked: new Map() };

/**
 * The messages a submission was actually written down for, which a batch that answered does not say by itself.
 *
 * A message the deployment did not record is mail that has moved on since the list drew it, an account it no longer
 * serves, or a message already where it was asked to go. Each is that message's own answer rather than the request's,
 * which is what lets the rest of a batch stand — and it is read the same way whichever direction the act was going in.
 */
function writtenDown(batches: readonly Submitted[]): ReadonlySet<string> {
    return new Set(
        batches.flatMap(({ answer }) =>
            answer.outcome === 'read'
                ? answer.value.filter((result) => result.outcome === 'recorded').map((result) => result.storedEmailId)
                : [],
        ),
    );
}

/** The messages a batch answered for without writing them down, which is not a batch that never answered at all. */
function refusedBy(batches: readonly Submitted[], written: ReadonlySet<string>): readonly string[] {
    return batches
        .filter(({ answer }) => answer.outcome === 'read')
        .flatMap(({ messages }) => messages)
        .filter((message) => !written.has(message.storedEmailId))
        .map((message) => message.storedEmailId);
}

/** Why a batch never reached the deployment, or `null` where every one of them did. */
function failureAmong(batches: readonly Submitted[]): ClientFailureReason | null {
    const failed = batches.find(({ answer }) => answer.outcome === 'failed')?.answer;

    return failed?.outcome === 'failed' ? failed.failure.reason : null;
}

/**
 * One batch as it went out: the messages it carried, beside what the deployment answered about them.
 *
 * Paired rather than answered alone, because a submission over the bound is several batches and they do not answer
 * together — one may be written down while the next never reaches the deployment at all. Only the pairing says which
 * messages that failure was about, and everything the screen reports afterwards turns on the difference.
 */
interface Submitted {
    readonly messages: readonly ActedMessage[];
    readonly answer: ClientResult<readonly MailMutationResult[]>;
}

export function MailboxActsProvider({
    session,
    transport,
    online,
    flags,
    moves,
    children,
}: {
    /** Who is asking and where, or `null` where there is nobody to act for. */
    readonly session: ClientSession | null;
    readonly transport: MailFathomTransport;
    readonly online: boolean;

    /** Whether this credential may write the two flags a mail server keeps. */
    readonly flags: boolean;

    /** Whether this credential may file mail in another folder, which is a grant of its own. */
    readonly moves: boolean;

    readonly children: ReactNode;
}) {
    const { locale, translate } = useLocalization();
    const toasts = useToasts();
    const [kept, setKept] = useState<Held>(heldForNobody);

    // How many times the folders have been asked for, which is what the second attempt is: the read is an effect, so
    // asking again is a value it depends on rather than a call from the toast that offered it.
    const [attempts, setAttempts] = useState(0);

    // Derived rather than cleared, for the reason `readMarking/ReadMarking.tsx` gives: signing out and back in on one
    // tab keeps this component mounted, and the previous person's pending acts would otherwise be drawn over the next
    // person's mail — and their folders read as this one's.
    const held = kept.session === session ? kept : heldForNobody;

    // The folders, because three of the five acts are folder moves and none of them can name a destination without
    // them. Read only where the credential may file mail at all: without that grant those three acts are refused before
    // a destination is looked for, so asking would be a request every session pays for and no screen reads.
    //
    // It is a read of its own rather than the tree's, which the mailbox column performs for what it draws: the two
    // answer the same route and neither is derived from the other, so a shared read would be one more thing to own than
    // either surface needs today.
    //
    // ponytail: a second reader of `/folders`. One read the whole client shares is the upgrade, and the moment to take
    // it is when a third surface needs the tree.
    //
    // A read that does not answer is said rather than dropped, and it is said once with the way out on it: without the
    // folders the three acts that file a message are refused as `foldersUnknown`, which is a sentence nobody can act on
    // unless the client offers them the second attempt.
    useEffect(() => {
        if (session === null || !online || !moves) {
            return;
        }

        let listening = true;

        void readMailFolders(session, transport).then((answer) => {
            if (!listening) {
                return;
            }

            if (answer.outcome === 'read') {
                setKept((current) => ({
                    session,
                    directory: answer.value,
                    asked: current.session === session ? current.asked : new Map(),
                }));

                return;
            }

            toasts.raise({
                kind: 'warning',
                title: translate('act.foldersNotRead', {
                    reason: translate(failureLabels[answer.failure.reason]),
                }),
                action: {
                    label: translate('act.readFoldersAgain'),
                    take: () => {
                        setAttempts((made) => made + 1);
                    },
                },
            });
        });

        return () => {
            listening = false;
        };
    }, [session, transport, online, moves, attempts, toasts, translate]);

    function refusalOf(act: MailboxAct, messages: readonly ActedMessage[]) {
        return refusalFor(act, messages, held.directory, { flags, moves });
    }

    /** Writes down what was asked for, so the rows say so from the press rather than from the next read of the folder. */
    function remember(act: MailboxAct, messages: readonly ActedMessage[]): void {
        setKept((current) => {
            const asked = new Map(current.session === session ? current.asked : []);

            for (const message of messages) {
                asked.set(message.storedEmailId, act);
            }

            return { session, directory: current.session === session ? current.directory : null, asked };
        });
    }

    /** Takes back what a message was asked for, which is what an act the deployment did not write down leaves behind. */
    function forget(storedEmailIds: readonly string[]): void {
        setKept((current) => {
            const asked = new Map(current.asked);

            for (const storedEmailId of storedEmailIds) {
                asked.delete(storedEmailId);
            }

            return { ...current, asked };
        });
    }

    function counted(messages: number): string {
        return translate(messagesCounted[new Intl.PluralRules(locale).select(messages)], {
            count: new Intl.NumberFormat(locale).format(messages),
        });
    }

    /** Puts one act on the wire as batches the submission bound admits, each answered beside what it carried. */
    async function submitted(
        asking: ClientSession,
        act: MailboxAct,
        messages: readonly ActedMessage[],
        destination: MoveDestination | undefined,
    ): Promise<readonly Submitted[]> {
        const changesFlags = act === 'flag' || act === 'markUnread';
        const filing = changesFlags ? [] : filingFor(act, messages, held.directory, destination?.alias ?? null);
        const filed = new Set(filing.map((one) => one.storedEmailId));

        // `filingFor` keeps the order it was given and drops only a message the act files nowhere, so the messages
        // below and the filings above are one list read twice rather than two lists that have to agree.
        const carried = changesFlags ? messages : messages.filter((message) => filed.has(message.storedEmailId));

        // Split rather than truncated: the route refuses a longer batch whole, so a message silently dropped here
        // would be a row drawn as filed against a mailbox nobody told.
        const batches: Promise<Submitted>[] = [];

        for (let from = 0; from < carried.length; from += mostMessagesPerMutation) {
            const batch = carried.slice(from, from + mostMessagesPerMutation);
            const answering = changesFlags
                ? changeMailFlags(
                      asking,
                      transport,
                      batch.map((message) =>
                          act === 'flag'
                              ? { storedEmailId: message.storedEmailId, flagged: true }
                              : { storedEmailId: message.storedEmailId, seen: false },
                      ),
                  )
                : moveMail(asking, transport, filing.slice(from, from + mostMessagesPerMutation));

            batches.push(answering.then((answer) => ({ messages: batch, answer })));
        }

        return Promise.all(batches);
    }

    /**
     * Reports what an act came to, and offers the way back where the act has one.
     *
     * All three at once where a submission came to all three: a batch that was written down is reported as written
     * down however the batch after it ended, because what the deployment holds does not turn on what it was asked
     * next. The two that did not land are said apart, a message the deployment answered about being a different thing
     * from one it never answered for at all.
     */
    function report(
        act: MailboxAct,
        recorded: readonly ActedMessage[],
        refused: readonly string[],
        destination: MoveDestination | undefined,
        failure: ClientFailureReason | null,
    ): void {
        if (recorded.length > 0) {
            // The way back is the toast's single action, which is the design project's own: the two acts that change a
            // flag offer none, because a flag is what the control that set it takes off again.
            const wayBack =
                act === 'flag' || act === 'markUnread'
                    ? {}
                    : {
                          action: {
                              label: translate('act.undo'),
                              take: () => {
                                  takeBack(recorded);
                              },
                          },
                      };

            toasts.raise({
                kind: 'neutral',
                title: translate(actReported[act], { folder: destination?.name ?? '' }),
                body: counted(recorded.length),
                ...wayBack,
            });
        }

        if (refused.length > 0) {
            toasts.raise({ kind: 'warning', title: translate('act.someNotChanged') });
        }

        if (failure !== null) {
            toasts.raise({
                kind: 'error',
                title: translate('act.failed', { reason: translate(failureLabels[failure]) }),
            });
        }
    }

    /**
     * Files the named messages back where each of them was, which is what taking a move back is.
     *
     * The reverse mutation rather than a withdrawal of the first: what was asked for may already be on its way to a
     * mail server, and a screen that merely stopped saying so would leave the mailbox somewhere the reader was told it
     * was not.
     */
    function takeBack(messages: readonly ActedMessage[]): void {
        if (session === null || !moves) {
            return;
        }

        const batches: Promise<Submitted>[] = [];

        for (let from = 0; from < messages.length; from += mostMessagesPerMutation) {
            const batch = messages.slice(from, from + mostMessagesPerMutation);

            batches.push(
                moveMail(
                    session,
                    transport,
                    batch.map((message) => ({
                        storedEmailId: message.storedEmailId,
                        destinationFolder: message.folder,
                    })),
                ).then((answer) => ({ messages: batch, answer })),
            );
        }

        void Promise.all(batches).then((answered) => {
            // Each message's own answer, exactly as the act itself is read: a mailbox that moved on between the act
            // and the press has messages the reverse move cannot write down either, and a row whose way back was not
            // recorded is still on its way to where the act put it — so it goes on saying so rather than being
            // forgotten on the strength of a batch that answered for something else.
            const written = writtenDown(answered);
            const returned = messages.filter((message) => written.has(message.storedEmailId));

            forget(returned.map((message) => message.storedEmailId));

            if (returned.length > 0) {
                toasts.raise({ kind: 'neutral', title: translate('act.undone'), body: counted(returned.length) });
            }

            if (refusedBy(answered, written).length > 0) {
                toasts.raise({ kind: 'warning', title: translate('act.someNotChanged') });
            }

            const failure = failureAmong(answered);

            if (failure !== null) {
                toasts.raise({
                    kind: 'error',
                    title: translate('act.failed', { reason: translate(failureLabels[failure]) }),
                });
            }
        });
    }

    function perform(act: MailboxAct, messages: readonly ActedMessage[], destination?: MoveDestination): void {
        if (session === null || refusalOf(act, messages) !== null) {
            return;
        }

        remember(act, messages);

        void submitted(session, act, messages, destination).then((answered) => {
            // A batch that was written down stands whatever the batch beside it came to: two hundred messages the
            // deployment holds are two hundred messages it holds, and forgetting them because the next batch never
            // reached it would leave every one of those rows saying nothing while the mailbox says otherwise.
            const written = writtenDown(answered);
            const recorded = messages.filter((message) => written.has(message.storedEmailId));

            forget(messages.filter((message) => !written.has(message.storedEmailId)).map((one) => one.storedEmailId));
            report(act, recorded, refusedBy(answered, written), destination, failureAmong(answered));
        });
    }

    const acts: MailboxActs =
        session === null || !(flags || moves)
            ? nothingActed
            : {
                  asked: held.asked,
                  refusalOf,
                  destinationsOf: (messages) => destinationsFor(held.directory, messages),
                  perform,
              };

    return <MailboxActsContext value={acts}>{children}</MailboxActsContext>;
}
