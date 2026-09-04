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
    useEffect(() => {
        if (session === null || !online || !moves) {
            return;
        }

        let listening = true;

        void readMailFolders(session, transport).then((answer) => {
            if (listening && answer.outcome === 'read') {
                setKept((current) => ({
                    session,
                    directory: answer.value,
                    asked: current.session === session ? current.asked : new Map(),
                }));
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport, online, moves]);

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

    /** Puts one act on the wire as batches the submission bound admits, and answers with one result per message. */
    async function submitted(
        asking: ClientSession,
        act: MailboxAct,
        messages: readonly ActedMessage[],
        destination: MoveDestination | undefined,
    ): Promise<readonly ClientResult<readonly MailMutationResult[]>[]> {
        const filing =
            act === 'flag' || act === 'markUnread'
                ? []
                : filingFor(act, messages, held.directory, destination?.alias ?? null);

        // Split rather than truncated: the route refuses a longer batch whole, so a message silently dropped here
        // would be a row drawn as filed against a mailbox nobody told.
        const batches: Promise<ClientResult<readonly MailMutationResult[]>>[] = [];

        if (act === 'flag' || act === 'markUnread') {
            const changes = messages.map((message) =>
                act === 'flag'
                    ? { storedEmailId: message.storedEmailId, flagged: true }
                    : { storedEmailId: message.storedEmailId, seen: false },
            );

            for (let from = 0; from < changes.length; from += mostMessagesPerMutation) {
                batches.push(changeMailFlags(asking, transport, changes.slice(from, from + mostMessagesPerMutation)));
            }
        } else {
            for (let from = 0; from < filing.length; from += mostMessagesPerMutation) {
                batches.push(moveMail(asking, transport, filing.slice(from, from + mostMessagesPerMutation)));
            }
        }

        return Promise.all(batches);
    }

    /** Reports what an act came to, and offers the way back where the act has one. */
    function report(
        act: MailboxAct,
        recorded: readonly ActedMessage[],
        refused: readonly string[],
        destination: MoveDestination | undefined,
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

        const back = messages.map((message) => ({
            storedEmailId: message.storedEmailId,
            destinationFolder: message.folder,
        }));

        forget(messages.map((message) => message.storedEmailId));

        const batches: Promise<ClientResult<readonly MailMutationResult[]>>[] = [];

        for (let from = 0; from < back.length; from += mostMessagesPerMutation) {
            batches.push(moveMail(session, transport, back.slice(from, from + mostMessagesPerMutation)));
        }

        void Promise.all(batches).then((answers) => {
            const failed = answers.find((answer) => answer.outcome === 'failed');

            toasts.raise(
                failed?.outcome === 'failed'
                    ? {
                          kind: 'error',
                          title: translate('act.failed', {
                              reason: translate(failureLabels[failed.failure.reason]),
                          }),
                      }
                    : { kind: 'neutral', title: translate('act.undone'), body: counted(back.length) },
            );
        });
    }

    function perform(act: MailboxAct, messages: readonly ActedMessage[], destination?: MoveDestination): void {
        if (session === null || refusalOf(act, messages) !== null) {
            return;
        }

        remember(act, messages);

        void submitted(session, act, messages, destination).then((answers) => {
            const failed = answers.find((answer) => answer.outcome === 'failed');

            if (failed?.outcome === 'failed') {
                forget(messages.map((message) => message.storedEmailId));
                toasts.raise({
                    kind: 'error',
                    title: translate('act.failed', { reason: translate(failureLabels[failed.failure.reason]) }),
                });

                return;
            }

            // A message the deployment did not write the act down for is one the row must stop claiming: mail that has
            // moved on since the list drew it, an account it no longer serves, or a message already where it was asked
            // to go. Each is that message's own answer rather than the request's, which is what lets the rest stand.
            const written = new Set(
                answers.flatMap((answer) =>
                    answer.outcome === 'read'
                        ? answer.value.filter((result) => result.outcome === 'recorded').map((r) => r.storedEmailId)
                        : [],
                ),
            );

            const recorded = messages.filter((message) => written.has(message.storedEmailId));
            const refused = messages
                .filter((message) => !written.has(message.storedEmailId))
                .map((message) => message.storedEmailId);

            forget(refused);
            report(act, recorded, refused, destination);
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
