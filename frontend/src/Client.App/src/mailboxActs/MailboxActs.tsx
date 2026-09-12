// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState, type ReactNode } from 'react';
import {
    changeMailFlags,
    deleteMail,
    mostMessagesPerMutation,
    moveMail,
    readMailFolders,
    releaseMailDeletes,
    withdrawMailDeletes,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailFathomTransport,
    type MailFolderDirectory,
    type MailFolderRole,
    type MailMutationResult,
} from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import type { ChangeAct, ChangeSubmission } from '../pendingChanges/changeStandings';
import { usePendingChanges } from '../pendingChanges/usePendingChanges';
import { useTelemetry } from '../telemetry/clientTelemetry';
import { useToasts, type Toast } from '../toasts/useToasts';
import {
    deletesPermanently,
    destinationName,
    destinationsFor,
    filingFor,
    refusalFor,
    type MoveDestination,
} from './mailboxDestinations';
import {
    changesAFlag,
    MailboxActsContext,
    nothingActed,
    type ActedMessage,
    type AskedAct,
    type FilingAct,
    type MailboxAct,
    type MailboxActs,
} from './useMailboxActs';

// Performing the acts, which is the one place in this client that changes somebody's mailbox from the Mail space.
// Nothing here reaches a mail server: each act writes a durable record through `/api/client` and answers, and the
// account's own convergence pass is what issues the IMAP command. So an account nobody can connect to leaves the act
// pending rather than failing it, and what is held below is what was asked for rather than what has been observed.
//
// **Every act is followed in the pending-changes queue**, which is `pendingChanges/`: what the deployment wrote down is
// waited on until the mailbox agrees or somebody has to decide, and what it refused or never answered is said there, in
// the words it says them in for every act — so which sentence a person meets never turns on which surface asked. What
// stays here is the half the queue has no notion of: the report of what an act was written down for, in the toast
// surface rather than on the control that was pressed, and the way back the three that file a message elsewhere offer
// as that toast's single action. Taking one back is the reverse move rather than a withdrawal of the first: a change
// already on its way to a mail server cannot be unsaid, and pretending otherwise would leave the mailbox and the screen
// disagreeing. The one act that offers no way back is the
// delete that destroys the mail, which is what *delete* means for a message already in the trash — there is no message
// left to move back, which is why the question in front of it says so before it is performed.
//
// **What each act may do at all is answered before it is offered**, which is `mailboxDestinations.ts`. An account with
// no archive folder is a control that says so rather than one that fails once it has been pressed.

// The three acts that file a message elsewhere are reported and the four that write a flag are not, which is the
// design project's own and is a statement about what a report is for rather than about how much each act matters. A
// message filed somewhere else has left the screen it was on, so the toast is where somebody learns where it went and
// the only place the way back is offered; a flag is a mark the row draws the moment it is asked for, and a card in the
// corner saying the mark that just appeared has appeared is the client narrating itself.

/** What the toast reporting a finished act is titled, exhaustive by the acts that raise one. */
const actReported: Readonly<Record<FilingAct, MessageKey>> = {
    archive: 'act.archived',
    delete: 'act.deleted',
    move: 'act.filed',
};

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
    missing: 'failure.missing',
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
    readonly asked: ReadonlyMap<string, AskedAct>;
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

/**
 * The records a submission wrote down, which is what taking a delete back and ending its wait each name.
 *
 * Records rather than messages, because a record is the unit the deployment holds, cancels, and takes in hand — and a
 * message it answered `recorded` for is exactly a message whose changes carry one.
 */
function recordsWritten(batches: readonly Submitted[]): readonly string[] {
    return batches.flatMap(({ answer }) =>
        answer.outcome === 'read'
            ? answer.value
                  .filter((result) => result.outcome === 'recorded')
                  .flatMap((result) => result.changes.map((change) => change.recordId))
            : [],
    );
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
    deletes,
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

    /** Whether this credential may delete mail from the mail server, which is the grant with no way back. */
    readonly deletes: boolean;

    readonly children: ReactNode;
}) {
    const { locale, translate } = useLocalization();
    const toasts = useToasts();
    const telemetry = useTelemetry();
    const pending = usePendingChanges();
    const [kept, setKept] = useState<Held>(heldForNobody);

    // How many times the folders have been asked for, which is what the second attempt is: the read is an effect, so
    // asking again is a value it depends on rather than a call from the toast that offered it.
    const [attempts, setAttempts] = useState(0);

    // Derived rather than cleared, for the reason `readMarking/ReadMarking.tsx` gives: signing out and back in on one
    // tab keeps this component mounted, and the previous person's pending acts would otherwise be drawn over the next
    // person's mail — and their folders read as this one's.
    const held = kept.session === session ? kept : heldForNobody;

    // Who is signed in now, for the reason `readMarking/ReadMarking.tsx` gives about its own: an act answers, and a
    // question the queue holds about it is answered, after the render that asked has gone — and neither may be acted
    // on under a credential that has since left. Nothing reads it while rendering, so it drives nothing on the screen.
    const signedIn = useRef(session);

    useEffect(() => {
        signedIn.current = session;
    }, [session]);

    // The folders, because three of the acts are folder moves and none of them can name a destination without
    // them. Read where the credential may file mail or delete it: without either grant those three acts are refused
    // before a destination is looked for, so asking would be a request every session pays for and no screen reads.
    // Deleting needs them for a second reason — which folder an account calls its trash is what says whether *delete*
    // files a message or destroys it.
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
        if (session === null || !online || !(moves || deletes)) {
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
    }, [session, transport, online, moves, deletes, attempts, toasts, translate]);

    function refusalOf(act: MailboxAct, messages: readonly ActedMessage[]) {
        return refusalFor(act, messages, held.directory, { flags, moves, deletes });
    }

    /** Whether asking to delete these messages destroys them, which is true where every one is already in the trash. */
    function destroys(messages: readonly ActedMessage[]): boolean {
        return deletesPermanently(held.directory, messages);
    }

    /**
     * Writes down what was asked for, so the rows say so from the press rather than from the next read of the folder.
     *
     * The folder each message was in is written down with it, because an act is about a message *in a place*: it is
     * what the sentence a row wears is drawn against, and what says the row is to leave this list and no other.
     */
    function remember(act: MailboxAct, messages: readonly ActedMessage[], leaves: boolean, destroys: boolean): void {
        setKept((current) => {
            const asked = new Map(current.session === session ? current.asked : []);

            for (const message of messages) {
                asked.set(message.storedEmailId, { act, from: message.folder, leaves, destroys });
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
        destroying: boolean,
    ): Promise<readonly Submitted[]> {
        // A delete in the trash files nothing, so it names no destination and takes the route of its own rather than
        // the move route with the folder the message is already in.
        if (destroying) {
            const batches: Promise<Submitted>[] = [];

            for (let from = 0; from < messages.length; from += mostMessagesPerMutation) {
                const batch = messages.slice(from, from + mostMessagesPerMutation);

                batches.push(
                    deleteMail(
                        asking,
                        transport,
                        batch.map((message) => message.storedEmailId),
                    ).then((answer) => ({ messages: batch, answer })),
                );
            }

            return Promise.all(batches);
        }

        const changesFlags = changesAFlag(act);
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
                          act === 'markUnread' || act === 'markRead'
                              ? { storedEmailId: message.storedEmailId, seen: act === 'markRead' }
                              : { storedEmailId: message.storedEmailId, flagged: act === 'flag' },
                      ),
                  )
                : moveMail(asking, transport, filing.slice(from, from + mostMessagesPerMutation));

            batches.push(answering.then((answer) => ({ messages: batch, answer })));
        }

        return Promise.all(batches);
    }

    /**
     * Reports what an act was written down for, and offers the way back where the act has one.
     *
     * Only the half the queue has no notion of. A batch that was written down is reported as written down however the
     * batch beside it ended, because what the deployment holds does not turn on what it was asked next — and what it
     * refused, or never answered at all, is the queue's to say, which `handOver` gives it.
     *
     * **An act that writes a flag reports nothing at all**, for the reason stated beside `actReported`: what it did is
     * the mark the row is already drawing. The queue still follows it, so a refusal is still said — what is silent is
     * the success.
     */
    function report(
        act: MailboxAct,
        recorded: readonly ActedMessage[],
        destination: MoveDestination | undefined,
        destroying: boolean,
        records: readonly string[],
    ): void {
        if (recorded.length === 0 || changesAFlag(act)) {
            return;
        }

        toasts.raise(
            destroying
                ? deleting(recorded, records)
                : {
                      kind: 'neutral',
                      title: translate(actReported[act], {
                          folder: destination === undefined ? '' : destinationName(destination, translate),
                      }),
                      body: counted(recorded.length),

                      // The way back is the toast's single action, which is the design project's own. A delete that
                      // destroys the mail offers a different one — the wait in front of it rather than a reverse
                      // move — which is why it is composed apart above rather than folded in here.
                      action: {
                          label: translate('act.undo'),
                          take: () => {
                              takeBack(recorded);
                          },
                      },
                  },
        );
    }

    /**
     * Hands the queue what an act's batches came to, which is where a refusal is said and a written-down change is
     * waited on until the mailbox agrees or somebody has to decide.
     *
     * Two submissions at most rather than one per batch: the batches that answered are one answer, so a reason is said
     * once however many batches it happened in, and the batches that never reached the deployment are one silence with
     * the way to ask again on it. Both ways out are the producer's, because only it knows what performing its act afresh,
     * or no longer claiming it, means — and neither is taken unless the credential it was performed under is still the
     * one signed in.
     */
    function handOver(
        asking: ClientSession,
        act: ChangeAct,
        answered: readonly Submitted[],
        askAgain: (messages: readonly ActedMessage[]) => void,
        letGo: (storedEmailIds: readonly string[]) => void,
    ): void {
        function submission(
            batches: readonly Submitted[],
            results: readonly MailMutationResult[] | null,
        ): ChangeSubmission {
            const carried = batches.flatMap(({ messages }) => messages);

            return {
                act,
                asked: carried.map((message) => message.storedEmailId),
                results,
                askAgain: (storedEmailIds) => {
                    const named = new Set(storedEmailIds);

                    if (signedIn.current === asking) {
                        askAgain(carried.filter((message) => named.has(message.storedEmailId)));
                    }
                },
                letGo: (storedEmailIds) => {
                    if (signedIn.current === asking) {
                        letGo(storedEmailIds);
                    }
                },
            };
        }

        const reached = answered.filter(({ answer }) => answer.outcome === 'read');
        const lost = answered.filter(({ answer }) => answer.outcome === 'failed');

        if (reached.length > 0) {
            pending.follow(
                submission(
                    reached,
                    reached.flatMap(({ answer }) => (answer.outcome === 'read' ? answer.value : [])),
                ),
            );
        }

        if (lost.length > 0) {
            pending.follow(submission(lost, null));
        }
    }

    /** Splits records into the batches the two record routes admit, which are bounded exactly as a submission is. */
    function batchesOf(records: readonly string[]): readonly (readonly string[])[] {
        const batches: (readonly string[])[] = [];

        for (let from = 0; from < records.length; from += mostMessagesPerMutation) {
            batches.push(records.slice(from, from + mostMessagesPerMutation));
        }

        return batches;
    }

    /**
     * The toast a permanent delete stands behind, which is also the whole of how long its way back is open.
     *
     * The deployment holds the delete for as long as this toast stands and no longer, so the card going is what closes
     * the offer: taken back, the records are cancelled and nothing reaches the mail server; left alone, the client says
     * so and the deployment stops waiting rather than sitting out a window nobody is watching any more. Both are the
     * same moment read two ways, which is why one toast carries both rather than a timer somewhere else agreeing with
     * a card somewhere else.
     */
    function deleting(messages: readonly ActedMessage[], records: readonly string[]): Toast {
        let takenBack = false;

        return {
            kind: 'neutral',
            title: translate('act.deletingPermanently'),
            body: counted(messages.length),
            action: {
                label: translate('act.undo'),
                take: () => {
                    takenBack = true;
                    withdraw(records);
                },
            },
            whenGone: () => {
                if (!takenBack) {
                    release(messages, records);
                }
            },
        };
    }

    /** Takes a permanent delete back, which cancels the records it was written down as before anything goes out. */
    function withdraw(records: readonly string[]): void {
        if (session === null) {
            return;
        }

        const asking = session;

        void Promise.all(batchesOf(records).map((batch) => withdrawMailDeletes(asking, transport, batch))).then(
            (answered) => {
                // Each record's own answer, exactly as the act itself is read: one the deployment has already taken in
                // hand is refused rather than cancelled, and that message goes on saying it is being deleted, which is
                // the truth about it.
                const cancelled = answered.flatMap((answer) =>
                    answer.outcome === 'read' ? answer.value.filter((record) => record.state === 'cancelled') : [],
                );

                forget(cancelled.map((record) => record.storedEmailId));

                if (cancelled.length > 0) {
                    toasts.raise({
                        kind: 'neutral',
                        title: translate('act.deleteWithdrawn'),
                        body: counted(cancelled.length),
                    });
                }

                const failed = answered.find((answer) => answer.outcome === 'failed');

                if (failed?.outcome === 'failed') {
                    toasts.raise({
                        kind: 'error',
                        title: translate('act.failed', {
                            reason: translate(failureLabels[failed.failure.reason]),
                        }),
                    });
                } else if (cancelled.length < records.length) {
                    toasts.raise({ kind: 'warning', title: translate('act.someNotChanged') });
                }
            },
        );
    }

    /**
     * Says the way back has closed, so the deployment stops holding the delete and takes it in hand at once.
     *
     * **The rows go with the offer.** While the card stood there was still a message to put back, so the row stayed
     * where it was and said what was about to happen to it; the moment the card goes there is nothing left of that
     * message to draw, and a row that went on standing would be a row claiming a message the deployment is destroying.
     * It is the same claim the three filing acts write from the press — `leaves` rather than a second kind of state —
     * so the queue that follows this delete lets go of it exactly as it lets go of an archive, and a delete the
     * deployment ends up refusing puts the row back rather than leaving a gap nobody can account for.
     */
    function release(messages: readonly ActedMessage[], records: readonly string[]): void {
        if (session === null) {
            return;
        }

        remember('delete', messages, true, true);

        for (const batch of batchesOf(records)) {
            // Nothing is reported and nothing is waited for: what this asks for is what would have happened anyway
            // once the window ran out, so a client that never asks costs the delete a wait rather than an outcome.
            void releaseMailDeletes(session, transport, batch);
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

        const asking = session;
        const batches: Promise<Submitted>[] = [];

        for (let from = 0; from < messages.length; from += mostMessagesPerMutation) {
            const batch = messages.slice(from, from + mostMessagesPerMutation);

            batches.push(
                moveMail(
                    asking,
                    transport,
                    batch.map((message) => ({
                        storedEmailId: message.storedEmailId,
                        destinationFolder: message.folder,
                    })),
                ).then((answer) => ({ messages: batch, answer })),
            );
        }

        void Promise.all(batches).then((answered) => {
            if (signedIn.current !== asking) {
                return;
            }

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

            // Followed like any act, because it is one: a way back the account stopped retrying is mail somebody was
            // told is back where it was. Letting one go claims nothing, because there is nothing left to stop
            // claiming — the act it reversed still stands for every message it did not return, and the ones it did
            // return are already drawn wherever the deployment lists them.
            handOver(asking, 'putBack', answered, takeBack, () => undefined);
        });
    }

    // Which role a message's own folder plays, read out of the same tree the destinations are read from. Matched by
    // the account as well as the alias, because an alias names a folder inside one account and two accounts may spell
    // one the same way.
    function folderRoleOf(message: ActedMessage): MailFolderRole | null {
        return (
            held.directory?.accounts
                .find((entry) => entry.account.id === message.account)
                ?.folders.find((folder) => folder.alias === message.folder)?.role ?? null
        );
    }

    function perform(act: MailboxAct, messages: readonly ActedMessage[], destination?: MoveDestination): void {
        if (session === null || refusalOf(act, messages) !== null) {
            return;
        }

        // Read before anything is submitted, so the act reported afterwards is the act that went out: the folders could
        // be re-read while the batch is in flight, and a message that left the trash in between must not turn a delete
        // somebody was told was permanent into one reported as a move.
        const destroying = act === 'delete' && destroys(messages);

        // Whether the message is leaving the list it was acted in, which the three filing acts do and a delete that
        // destroys the mail does not: there is nowhere left for it to go, so it stays where it is and says so until
        // the wait in front of it is over.
        const leaves = act === 'archive' || act === 'move' || (act === 'delete' && !destroying);

        const asking = session;

        // The act and how many messages it was asked over. Which messages is a list of stored identities and is not
        // written down; how many is what separates somebody pressing archive on one row from a select-all across two
        // hundred, which is the difference an operator reading a mail server complaining about write volume needs.
        telemetry.happened('act_asked', {
            'mailfathom.client.act': act,
            'mailfathom.client.messages': messages.length,
        });

        remember(act, messages, leaves, destroying);

        void submitted(asking, act, messages, destination, destroying).then((answered) => {
            // An answer arriving after somebody else has signed in is neither theirs to be told about nor their queue's
            // to follow.
            if (signedIn.current !== asking) {
                return;
            }

            // A batch that was written down stands whatever the batch beside it came to: two hundred messages the
            // deployment holds are two hundred messages it holds, and forgetting them because the next batch never
            // reached it would leave every one of those rows saying nothing while the mailbox says otherwise.
            const written = writtenDown(answered);
            const recorded = messages.filter((message) => written.has(message.storedEmailId));

            // Everything that was not written down stops being claimed here, rather than only where the queue lets go
            // of it: a message already where it was asked to go is a refusal nobody is told about, and an act filing
            // it into the folder it is already in has not taken it out of the list it is drawn in either.
            forget(messages.filter((message) => !written.has(message.storedEmailId)).map((one) => one.storedEmailId));

            // The one thing this client does that a deployment's own records do not already show: the screen had drawn
            // the act as done and has just put itself back, which somebody using it experiences as the client undoing
            // their work. It is above the default floor because it is a deployment refusing writes it accepted the
            // request for, which is a thing to look at rather than a thing to read.
            //
            // Counted over the batches the deployment actually answered, and never over one that failed to reach it.
            // A dropped connection or a credential that expired leaves `writtenDown` with nothing from that batch,
            // which is indistinguishable here from a deployment declining every message in it — so counting both
            // would put every transport failure into the one record an operator reads to find a deployment refusing
            // writes. `request_failed` already reports that half, at the level a transport failure belongs to.
            const declined = answered
                .filter(({ answer }) => answer.outcome === 'read')
                .flatMap(({ messages: batch }) => batch)
                .filter((message) => !written.has(message.storedEmailId));

            if (declined.length > 0) {
                telemetry.happened('act_refused', {
                    'mailfathom.client.act': act,
                    'mailfathom.client.messages': declined.length,
                });
            }

            report(act, recorded, destination, destroying, recordsWritten(answered));

            // Asking again is the same act performed afresh over the same messages, naming the folder a move named, so
            // it travels the path the first attempt took and is followed again from its own answer. The claim the
            // first attempt left is taken back before it goes: an act the folders no longer allow is refused before
            // anything is submitted, and a row must not go on saying the first attempt is still on its way.
            handOver(
                asking,
                act,
                answered,
                (again) => {
                    forget(again.map((message) => message.storedEmailId));
                    perform(act, again, destination);
                },
                forget,
            );
        });
    }

    const acts: MailboxActs =
        session === null || !(flags || moves || deletes)
            ? nothingActed
            : {
                  asked: held.asked,
                  refusalOf,
                  folderRoleOf,
                  destinationsOf: (messages) => destinationsFor(held.directory, messages),
                  deletesPermanently: destroys,
                  perform,
              };

    return <MailboxActsContext value={acts}>{children}</MailboxActsContext>;
}
