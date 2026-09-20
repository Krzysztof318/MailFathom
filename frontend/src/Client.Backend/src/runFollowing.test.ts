// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { ClientResult } from './failure';
import {
    followRun,
    silentRunPollInterval,
    type FollowedRun,
    type FollowedRunEvent,
    type RunFollowingSchedule,
    type RunTail,
    type RunTailReading,
} from './runFollowing';

interface ComposedBlock extends FollowedRunEvent {
    readonly block: string;
}

type Answer = ClientResult<RunTail<ComposedBlock>>;

function composed(running: boolean, ...sequences: readonly number[]): Answer {
    return {
        outcome: 'read',
        value: { running, events: sequences.map((sequence) => ({ sequence, block: `block ${String(sequence)}` })) },
    };
}

const unavailable: Answer = { outcome: 'failed', failure: { reason: 'unavailable', status: null } };

// Every wait in the follower is a microtask on a promise the reading answered, so draining the queue is the whole of
// what "once the read has answered" means here. Nothing in it reads a clock, so there is no duration to advance and
// nothing a loaded machine can put out of order.
async function answered(): Promise<void> {
    for (let drain = 0; drain < 16; drain += 1) {
        await Promise.resolve();
    }
}

// The last answer stands for every read after it, so a test stating one running answer describes a run that is still
// going however many times the follower comes back to it.
function readerAnswering(answers: readonly Answer[]): { read: RunTailReading<ComposedBlock>; asked: number[] } {
    const asked: number[] = [];
    let made = 0;

    return {
        asked,
        read: (since) => {
            asked.push(since);

            const answer = answers[Math.min(made, answers.length - 1)] ?? unavailable;

            made += 1;

            return Promise.resolve(answer);
        },
    };
}

// The interval arrives as a promise the test resolves rather than as a timer, which is what lets a test say that
// nothing was armed at all as easily as it says what fired.
function schedulePausing(): {
    schedule: RunFollowingSchedule;
    waits: number[];
    fire: () => Promise<void>;
} {
    const waits: number[] = [];
    let armed: (() => void)[] = [];

    return {
        waits,
        schedule: {
            wait: (milliseconds) => {
                waits.push(milliseconds);

                return new Promise<void>((resolve) => {
                    armed.push(resolve);
                });
            },
        },
        fire: async () => {
            const firing = armed;
            armed = [];

            for (const resolve of firing) {
                resolve();
            }

            await answered();
        },
    };
}

function following(
    read: RunTailReading<ComposedBlock>,
    schedule: RunFollowingSchedule,
    from = 0,
): { told: Answer[]; run: FollowedRun } {
    const told: Answer[] = [];
    const run = followRun<ComposedBlock>({
        run: 'discover',
        from,
        read,
        told: (tail) => told.push(tail),
        schedule,
    });

    return { told, run };
}

describe('followRun', () => {
    it('reads the run before anything has told it about a hub', async () => {
        const reader = readerAnswering([composed(true, 1, 2)]);
        const paused = schedulePausing();
        const followed = following(reader.read, paused.schedule);

        await answered();

        expect(reader.asked).toStrictEqual([0]);
        expect(followed.told).toStrictEqual([composed(true, 1, 2)]);
    });

    it('reads from the cursor it was given rather than from the beginning', async () => {
        const reader = readerAnswering([composed(true, 5)]);
        const paused = schedulePausing();

        following(reader.read, paused.schedule, 4);
        await answered();

        expect(reader.asked).toStrictEqual([4]);
    });

    it('reads the whole of a run that has already finished without a hub ever opening', async () => {
        const reader = readerAnswering([composed(false, 1, 2, 3)]);
        const paused = schedulePausing();
        const followed = following(reader.read, paused.schedule);

        await answered();

        expect(followed.told).toStrictEqual([composed(false, 1, 2, 3)]);
        expect(paused.waits).toStrictEqual([]);
    });

    it('reads again from the cursor it holds when the run it follows advances', async () => {
        const reader = readerAnswering([composed(true, 1, 2), composed(true, 3)]);
        const paused = schedulePausing();
        const followed = following(reader.read, paused.schedule);

        await answered();
        followed.run.advanced('discover', 3);
        await answered();

        expect(reader.asked).toStrictEqual([0, 2]);
    });

    it('closes a gap by reading from its own cursor rather than from the beginning', async () => {
        const reader = readerAnswering([composed(true, 1, 2), composed(true, 3, 4, 5, 6, 7, 8, 9)]);
        const paused = schedulePausing();
        const followed = following(reader.read, paused.schedule);

        await answered();
        followed.run.advanced('discover', 9);
        await answered();

        expect(reader.asked).toStrictEqual([0, 2]);
        expect(followed.told[1]).toStrictEqual(composed(true, 3, 4, 5, 6, 7, 8, 9));
    });

    it('changes nothing when an advance names another run', async () => {
        const reader = readerAnswering([composed(true, 1)]);
        const paused = schedulePausing();
        const followed = following(reader.read, paused.schedule);

        await answered();
        followed.run.advanced('another', 9);
        await answered();

        expect(reader.asked).toStrictEqual([0]);
    });

    it.each([2, 1])(
        'changes nothing when an advance names sequence %i, which is not past its cursor',
        async (sequence) => {
            const reader = readerAnswering([composed(true, 1, 2)]);
            const paused = schedulePausing();
            const followed = following(reader.read, paused.schedule);

            await answered();
            followed.run.advanced('discover', sequence);
            await answered();

            expect(reader.asked).toStrictEqual([0]);
        },
    );

    it('makes one further read where two advances arrive while a read is in flight', async () => {
        const asked: number[] = [];
        let answer = (): void => undefined;

        const read: RunTailReading<ComposedBlock> = (since) => {
            asked.push(since);

            return new Promise<Answer>((resolve) => {
                answer = () => {
                    resolve(composed(true, since + 1));
                };
            });
        };

        const paused = schedulePausing();
        const followed = following(read, paused.schedule);

        await answered();
        followed.run.advanced('discover', 2);
        followed.run.advanced('discover', 3);
        answer();
        await answered();

        expect(asked).toStrictEqual([0, 1]);

        answer();
        await answered();

        expect(asked).toStrictEqual([0, 1]);
    });

    it('reads again on the interval while the run is in flight and nothing has advanced it', async () => {
        const reader = readerAnswering([composed(true, 1)]);
        const paused = schedulePausing();

        following(reader.read, paused.schedule);
        await answered();

        expect(paused.waits).toStrictEqual([silentRunPollInterval]);

        await paused.fire();

        expect(reader.asked).toStrictEqual([0, 1]);
    });

    it('stops reading on the interval once a read reports the run has ended', async () => {
        const reader = readerAnswering([composed(true, 1), composed(false, 2)]);
        const paused = schedulePausing();

        following(reader.read, paused.schedule);
        await answered();
        await paused.fire();

        expect(reader.asked).toStrictEqual([0, 1]);
        expect(paused.waits).toStrictEqual([silentRunPollInterval]);
    });

    it('goes on reading on the interval where a read did not answer', async () => {
        const reader = readerAnswering([unavailable]);
        const paused = schedulePausing();
        const followed = following(reader.read, paused.schedule);

        await answered();
        await paused.fire();

        expect(reader.asked).toStrictEqual([0, 0]);
        expect(followed.told).toStrictEqual([unavailable, unavailable]);
    });

    it('lets an interval that a read has already overtaken pass without reading', async () => {
        const reader = readerAnswering([composed(true, 1), composed(true, 2)]);
        const paused = schedulePausing();
        const followed = following(reader.read, paused.schedule);

        await answered();
        followed.run.advanced('discover', 2);
        await answered();
        await paused.fire();

        expect(reader.asked).toStrictEqual([0, 1, 2]);
    });

    it('reads whatever its cursor stands at when the connection opened again', async () => {
        const reader = readerAnswering([composed(true, 1)]);
        const paused = schedulePausing();
        const followed = following(reader.read, paused.schedule);

        await answered();
        followed.run.reconnected();
        await answered();

        expect(reader.asked).toStrictEqual([0, 1]);
    });

    it('tells nobody about an answer that arrived after it was closed', async () => {
        let answer = (): void => undefined;

        const read: RunTailReading<ComposedBlock> = () =>
            new Promise<Answer>((resolve) => {
                answer = () => {
                    resolve(composed(true, 1));
                };
            });

        const paused = schedulePausing();
        const followed = following(read, paused.schedule);

        await answered();
        followed.run.close();
        answer();
        await answered();

        expect(followed.told).toStrictEqual([]);
        expect(paused.waits).toStrictEqual([]);
    });

    it('reads nothing more once it is closed', async () => {
        const reader = readerAnswering([composed(true, 1)]);
        const paused = schedulePausing();
        const followed = following(reader.read, paused.schedule);

        await answered();
        followed.run.close();
        followed.run.advanced('discover', 9);
        followed.run.reconnected();
        await paused.fire();

        expect(reader.asked).toStrictEqual([0]);
    });
});
