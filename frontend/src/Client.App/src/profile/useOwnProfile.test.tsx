// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import type { PortraitExchange, PortraitRead, PortraitWrite } from '../deployment/portraitExchange';
import { useOwnProfile, type OwnProfileInForce } from './useOwnProfile';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic YWRh',
};

const somebodyElse: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic Z3JhY2U=',
};

const picture = 'data:image/png;base64,AA==';

/** A deployment naming whoever asked, so that an answer read for one credential is visibly not the other's. */
const namesWhoeverAsks: MailFathomTransport = (request) =>
    Promise.resolve({
        status: 200,
        body: JSON.stringify({
            displayName: request.headers['Authorization'] === session.authorization ? 'Ada Lovelace' : 'Grace Hopper',
            changeable: true,
        }),
        headers: {},
    });

/** A deployment answering the name route with one stored name, and the write route with what it was sent. */
function naming(displayName: string, changeable = true): MailFathomTransport {
    return () => Promise.resolve({ status: 200, body: JSON.stringify({ displayName, changeable }), headers: {} });
}

function drawing(read: PortraitRead, write: PortraitWrite = { outcome: 'stored' }): PortraitExchange {
    return {
        read: () => Promise.resolve(read),
        replace: () => Promise.resolve(write),
        remove: () => Promise.resolve(write),
    };
}

// The hook is read through a component, which is how it is used: what a test asserts is what a screen was handed.
function Reading({
    who,
    transport,
    portraits,
    held,
}: {
    readonly who: ClientSession | null;
    readonly transport: MailFathomTransport;
    readonly portraits: PortraitExchange;
    readonly held: (profile: OwnProfileInForce) => void;
}) {
    const profile = useOwnProfile(who, transport, portraits);

    held(profile);

    // Composed above the markup rather than inside it, which is where a sentence a person reads would have to be a
    // catalogue entry. Nobody reads this one: it is what the one test about the first render before any answer reads.
    const drawn = [
        profile.displayName ?? 'nobody',
        profile.changeable ? 'changeable' : 'fixed',
        profile.picture ?? 'no picture',
    ].join(' · ');

    return <p>{drawn}</p>;
}

/** Lets whatever the hook started reach the state it renders from, which is what every answer below is read after. */
function settled(): Promise<void> {
    return act(() => Promise.resolve());
}

interface Held {
    readonly latest: () => OwnProfileInForce;
    readonly askedAs: (next: ClientSession | null) => Promise<void>;
}

async function renderProfile(
    portraits: PortraitExchange,
    transport: MailFathomTransport = naming('Ada Lovelace'),
    who: ClientSession | null = session,
): Promise<Held> {
    let held: OwnProfileInForce | null = null;
    const take = (profile: OwnProfileInForce): void => {
        held = profile;
    };

    const { rerender } = render(<Reading who={who} transport={transport} portraits={portraits} held={take} />);

    await settled();

    return {
        latest: () => {
            if (held === null) {
                throw new Error('The hook rendered nothing to read.');
            }

            return held;
        },
        askedAs: async (next) => {
            rerender(<Reading who={next} transport={transport} portraits={portraits} held={take} />);
            await settled();
        },
    };
}

/** A deployment refusing the write and answering the read, which is the pair every write test needs. */
function refusingWrites(status: number): MailFathomTransport {
    return (request) =>
        request.method === 'POST'
            ? Promise.resolve({ status, body: '{}', headers: {} })
            : Promise.resolve({
                  status: 200,
                  body: JSON.stringify({ displayName: 'Ada Lovelace', changeable: true }),
                  headers: {},
              });
}

describe('useOwnProfile', () => {
    it('draws nobody until the deployment has answered', () => {
        render(
            <Reading
                who={session}
                transport={naming('Ada Lovelace')}
                portraits={drawing({ outcome: 'none' })}
                held={() => undefined}
            />,
        );

        expect(screen.getByText('nobody · fixed · no picture')).toBeDefined();
    });

    it('reads the name and whether the deployment would take a correction of it', async () => {
        const held = await renderProfile(drawing({ outcome: 'none' }), naming('Ada Lovelace', false));

        expect(held.latest().displayName).toBe('Ada Lovelace');
        expect(held.latest().changeable).toBe(false);
    });

    it('reads the picture where the person has one', async () => {
        const held = await renderProfile(drawing({ outcome: 'drawn', picture }));

        expect(held.latest().picture).toBe(picture);
    });

    it('draws no picture for somebody who has none, which is what the initials stand in for', async () => {
        const held = await renderProfile(drawing({ outcome: 'none' }));

        expect(held.latest().picture).toBeNull();
    });

    it('draws nothing for a picture the deployment would not answer for, rather than an empty frame', async () => {
        const held = await renderProfile(drawing({ outcome: 'refused', reason: 'unavailable' }));

        expect(held.latest().picture).toBeNull();
    });

    it('asks nothing at all where there is nothing to ask with', async () => {
        const read = vi.fn(() => Promise.resolve<PortraitRead>({ outcome: 'none' }));
        const transport = vi.fn(naming('Ada Lovelace'));

        await renderProfile({ ...drawing({ outcome: 'none' }), read }, transport, null);

        expect(read).not.toHaveBeenCalled();
        expect(transport).not.toHaveBeenCalled();
    });

    it('draws the next credential’s person rather than the one read for the last', async () => {
        const held = await renderProfile(drawing({ outcome: 'none' }), namesWhoeverAsks);

        expect(held.latest().displayName).toBe('Ada Lovelace');

        await held.askedAs(somebodyElse);

        expect(held.latest().displayName).toBe('Grace Hopper');
    });

    it('draws nobody once nobody is signed in, rather than whoever was before', async () => {
        const held = await renderProfile(drawing({ outcome: 'drawn', picture }));

        await held.askedAs(null);

        expect(held.latest().displayName).toBeNull();
        expect(held.latest().picture).toBeNull();
    });

    it('draws the name as the deployment stored it rather than as it was typed', async () => {
        const held = await renderProfile(drawing({ outcome: 'none' }), naming('Ada Lovelace'));

        act(() => {
            held.latest().correctName('  Ada Lovelace  ');
        });
        await settled();

        expect(held.latest().displayName).toBe('Ada Lovelace');
        expect(held.latest().nameNotAcceptable).toBe(false);
    });

    it('says the deployment would not take the name rather than pretending it landed', async () => {
        const held = await renderProfile(drawing({ outcome: 'none' }), refusingWrites(400));

        act(() => {
            held.latest().correctName('');
        });
        await settled();

        expect(held.latest().nameNotAcceptable).toBe(true);
        expect(held.latest().displayName).toBe('Ada Lovelace');
    });

    it('says a name that never reached the deployment did not, which is a different sentence', async () => {
        const held = await renderProfile(drawing({ outcome: 'none' }), refusingWrites(503));

        act(() => {
            held.latest().correctName('Grace Hopper');
        });
        await settled();

        expect(held.latest().nameNotStated).toBe(true);
        expect(held.latest().nameNotAcceptable).toBe(false);
    });

    it('draws the correction made last rather than an answer to one it replaced', async () => {
        let answerTheFirstCorrection = (): void => undefined;
        const firstCorrection = new Promise<void>((resolve) => {
            answerTheFirstCorrection = () => {
                resolve();
            };
        });
        let corrections = 0;

        // The first correction is answered last, which is the ordering nothing between two writes guarantees: its
        // answer names the name the person has since changed away from.
        const transport: MailFathomTransport = async (request) => {
            if (request.method !== 'POST') {
                return { status: 200, body: JSON.stringify({ displayName: 'Ada Lovelace', changeable: true }), headers: {} };
            }

            corrections += 1;

            if (corrections > 1) {
                return {
                    status: 200,
                    body: JSON.stringify({ displayName: 'Katherine Johnson', changeable: true }),
                    headers: {},
                };
            }

            await firstCorrection;

            return { status: 200, body: JSON.stringify({ displayName: 'Grace Hopper', changeable: true }), headers: {} };
        };

        const held = await renderProfile(drawing({ outcome: 'none' }), transport);

        act(() => {
            held.latest().correctName('Grace Hopper');
        });
        act(() => {
            held.latest().correctName('Katherine Johnson');
        });
        await settled();

        act(answerTheFirstCorrection);
        await settled();

        expect(held.latest().displayName).toBe('Katherine Johnson');
    });

    it('draws the picture the deployment stored rather than the file that was sent', async () => {
        const answers: PortraitRead[] = [{ outcome: 'none' }, { outcome: 'drawn', picture }];
        const held = await renderProfile({
            read: () => Promise.resolve(answers.shift() ?? { outcome: 'none' }),
            replace: () => Promise.resolve({ outcome: 'stored' }),
            remove: () => Promise.resolve({ outcome: 'stored' }),
        });

        act(() => {
            held.latest().choosePicture(new Blob([new Uint8Array(8)]), 'image/png');
        });
        await settled();

        expect(held.latest().picture).toBe(picture);
    });

    it('draws the replacement rather than an older answer that arrives after it', async () => {
        const replaced = 'data:image/png;base64,AQ==';
        let answerTheFirstRead = (): void => undefined;
        const firstRead = new Promise<void>((resolve) => {
            answerTheFirstRead = () => {
                resolve();
            };
        });
        let reads = 0;

        // The read started at mount answers last, which is the ordering nothing between two requests guarantees and
        // the one a screen must not draw: it holds the picture the upload replaced.
        const held = await renderProfile({
            read: async () => {
                reads += 1;

                if (reads > 1) {
                    return { outcome: 'drawn', picture };
                }

                await firstRead;

                return { outcome: 'drawn', picture: replaced };
            },
            replace: () => Promise.resolve({ outcome: 'stored' }),
            remove: () => Promise.resolve({ outcome: 'stored' }),
        });

        act(() => {
            held.latest().choosePicture(new Blob([new Uint8Array(8)]), 'image/png');
        });
        await settled();

        act(answerTheFirstRead);
        await settled();

        expect(held.latest().picture).toBe(picture);
    });

    it('says a picture that did not reach the deployment did not, and leaves what is drawn alone', async () => {
        const held = await renderProfile(
            drawing({ outcome: 'drawn', picture }, { outcome: 'refused', reason: 'unavailable' }),
        );

        act(() => {
            held.latest().choosePicture(new Blob([new Uint8Array(8)]), 'image/png');
        });
        await settled();

        expect(held.latest().pictureNotStated).toBe(true);
        expect(held.latest().picture).toBe(picture);
    });

    it('falls back to no picture once the deployment has removed one', async () => {
        const held = await renderProfile(drawing({ outcome: 'drawn', picture }));

        act(() => {
            held.latest().removePicture();
        });
        await settled();

        expect(held.latest().picture).toBeNull();
        expect(held.latest().pictureNotStated).toBe(false);
    });

    it('keeps the picture drawn where the removal did not reach the deployment', async () => {
        const held = await renderProfile(
            drawing({ outcome: 'drawn', picture }, { outcome: 'refused', reason: 'unavailable' }),
        );

        act(() => {
            held.latest().removePicture();
        });
        await settled();

        expect(held.latest().picture).toBe(picture);
        expect(held.latest().pictureNotStated).toBe(true);
    });
});
