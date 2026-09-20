// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ClientResult } from './failure';

// How this client watches an answer that is composed while somebody is looking at it. Two surfaces produce one — the
// Discover run and the Agent conversation — and the rule is written here once so both follow a run through the same
// code rather than through two readings of the same decision.
//
// ADR 0035 is that decision and this is its client half: the durable record is the guarantee and the signal is an
// optimization. What follows from it is the whole of the behaviour below. **The first read is unconditional**, so a
// screen draws with no hub at all and a run that has already finished needs one for nothing. An advance says only how
// far the run has got, so what carries the answer is this client's own read from the cursor it holds.
//
// **The fallback is armed by an observation this client owns rather than by the hub's reported state.** A hub can be
// connected and silent — ADR 0032 states that the backplane buffers nothing while it is unreachable and that losing it
// takes no replica out of rotation — so a poll armed on a connection reporting itself down would stay disarmed through
// exactly the outage it exists for. What arms it here is that a run is in flight and nothing has advanced it since the
// last read, which is true whether the hub is absent, connected and silent, or dropping.
//
// The route is the surface's rather than this module's: Discover reads its run from one address and the Agent reads
// its conversation from another, and neither is a shape to decide here. What is decided here is when a read happens,
// what cursor it carries, and what happens to an answer — so the reading arrives as a function, exactly as the
// transport does.

/**
 * The bounded interval a run in flight is re-read at while nothing has advanced it, in milliseconds.
 *
 * It is what somebody watching an answer assemble would accept as the worst case for a block appearing, against what
 * a run costs the routes and the database when every watching client is reading on the interval alone. Nothing reaches
 * it while advances arrive, because each read re-arms it: a run that is being announced is a run this never fires for.
 */
export const silentRunPollInterval = 3_000;

/** What following a run needs of one of its events, which is where it sits in the run. */
export interface FollowedRunEvent {
    /** The event's place in the run, counted from one, which is what the cursor is read from. */
    readonly sequence: number;
}

/** One read of a run: where the run stands, and everything it has written past the cursor the read carried. */
export interface RunTail<TEvent extends FollowedRunEvent> {
    /** Whether the run is still executing, which is what says more is coming. */
    readonly running: boolean;

    /** Everything after the cursor, in sequence order, and empty where the reader was already caught up. */
    readonly events: readonly TEvent[];
}

/**
 * How a surface reads its own run's tail from a cursor.
 *
 * It is a reading like every other in this package, so what it answers has already been checked and bounded against
 * the route's own contract: nothing here reads a body, and nothing here is in a position to refuse one.
 *
 * @param since The last sequence the follower holds, and zero to read the run from its beginning.
 */
export type RunTailReading<TEvent extends FollowedRunEvent> = (since: number) => Promise<ClientResult<RunTail<TEvent>>>;

/** What waiting takes, which is not in this package's `lib` and so arrives from the application as the transport does. */
export interface RunFollowingSchedule {
    /** Resolves after roughly that many milliseconds. */
    readonly wait: (milliseconds: number) => Promise<void>;
}

/** The run to follow, and everything following one needs that this package does not own. */
export interface RunToFollow<TEvent extends FollowedRunEvent> {
    /** The run being followed, which is what an advance is matched against so a second run's signal is not this one's. */
    readonly run: string;

    /** The last sequence the caller already holds, and zero where it holds nothing. */
    readonly from: number;

    /** How the tail is read, which is the route the surface following the run owns. */
    readonly read: RunTailReading<TEvent>;

    /** Called once per read with what it answered, in the order the reads were made. */
    readonly told: (tail: ClientResult<RunTail<TEvent>>) => void;

    readonly schedule: RunFollowingSchedule;
}

/** A run being followed, which the caller tells about signals and closes when the screen showing it goes away. */
export interface FollowedRun {
    /**
     * Tells the follower that a signal said a run advanced.
     *
     * Every advance this client hears may be handed over: one naming another run, and one whose sequence the follower
     * already holds, each change nothing.
     */
    advanced: (run: string, sequence: number) => void;

    /** Tells the follower that the signal connection opened again, which is a read whatever the cursor stands at. */
    reconnected: () => void;

    /** Stops it: nothing is read after this and no wait outlives it. */
    close: () => void;
}

/**
 * Follows a run, reading its tail on mount, on every advance that is past the cursor, after every reconnect, and on a
 * bounded interval while the run is in flight and nothing has advanced it.
 *
 * The first read is made before anything is told about a hub, so a caller with no connection at all is a caller that
 * sees the whole of a finished run and everything a running one has composed so far.
 *
 * @param following The run, the cursor to read from, how to read it, and who to tell.
 * @returns The subscription, which the caller closes when it stops drawing the run.
 */
export function followRun<TEvent extends FollowedRunEvent>(following: RunToFollow<TEvent>): FollowedRun {
    const { run, read, told, schedule } = following;

    let cursor = following.from;
    let closed = false;

    // The run has answered that it is no longer executing, after which nothing further is read: a finished run writes
    // no more events, so a late advance for it names a sequence that no read would return.
    let settled = false;

    let reading = false;

    // Something asked for a read while one was in flight. It is a flag rather than a count because a read answers with
    // everything after the cursor — so any number of advances during one read is one further read, which is what makes
    // a burst cost one round trip.
    let asked = false;

    // How many reads have been started, which is what a waiting poll checks itself against. A poll that finds the
    // count moved has been overtaken by a read an advance or a reconnect caused, and that read has already armed the
    // next wait.
    let reads = 0;

    // Both are asked through a function rather than read off the variable, for the reason the signal stream asks the
    // same question that way: a check made before an await would otherwise decide for every check after it, and what
    // changes them in between is a read answering that the run has ended and the caller closing the follower.
    const hasClosed = (): boolean => closed;
    const hasSettled = (): boolean => settled;
    const hasBeenAsked = (): boolean => asked;

    const readTail = async (): Promise<void> => {
        if (hasClosed() || hasSettled()) {
            return;
        }

        if (reading) {
            asked = true;

            return;
        }

        reading = true;

        do {
            asked = false;
            reads += 1;

            const answered = await read(cursor);

            if (hasClosed()) {
                reading = false;

                return;
            }

            if (answered.outcome === 'read') {
                for (const event of answered.value.events) {
                    cursor = Math.max(cursor, event.sequence);
                }

                settled = !answered.value.running;
            }

            told(answered);
        } while (hasBeenAsked() && !hasSettled());

        reading = false;

        // A read that failed leaves the run's state unknown, which is read as still in flight rather than as finished:
        // a client that stopped following because a deployment blinked would need somebody to reload the screen to
        // find out that the answer had been waiting.
        if (!hasSettled()) {
            armPoll();
        }
    };

    const armPoll = (): void => {
        const armedAfter = reads;

        void schedule.wait(silentRunPollInterval).then(() => {
            if (!hasClosed() && !hasSettled() && reads === armedAfter) {
                void readTail();
            }
        });
    };

    void readTail();

    return {
        advanced: (advancedRun, sequence) => {
            if (advancedRun === run && sequence > cursor) {
                void readTail();
            }
        },
        reconnected: () => {
            void readTail();
        },
        close: () => {
            closed = true;
        },
    };
}
