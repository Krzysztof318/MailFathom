// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, type ReactNode } from 'react';
import { useLocalization } from '../localization/useLocalization';
import { MailSpace } from '../mailSpace/MailSpace';
import { implementedSpaces, spaceLabels, type Space as SpaceName } from '../routing/spaces';

// The region the address decides the contents of. What each space actually holds is built by its own issue; what this
// owns permanently is that a space is mounted once and then only moved in front of or behind the others, and what
// happens to focus when the address changes.

export function Space({
    offered,
    space,
    intent,
    status,
    folders,
    list,
    mail,
    tabs,
    person,
}: {
    /** Every space this deployment offers, which is every space that is mounted. */
    readonly offered: readonly SpaceName[];

    readonly space: SpaceName;

    /** The question the reader is composing, which every space carries somewhere. */
    readonly intent: ReactNode;

    /** What the deployment says about the connection, which every space shows somewhere. */
    readonly status: ReactNode;

    readonly folders: ReactNode;
    readonly list: ReactNode;
    readonly mail: ReactNode;

    /** The strip naming everything the Mail space has open, which only that space has anywhere to put. */
    readonly tabs: ReactNode;

    /** Who is signed in, which the Mail space keeps its own division of the width under. */
    readonly person: string | null;
}) {
    const { translate } = useLocalization();
    const inFrontRegion = useRef<HTMLElement>(null);
    const shown = useRef(space);

    // Navigation puts focus at the start of the new content, which is where keyboard and screen-reader use otherwise
    // silently stops working: focus would stay on the link that was activated, in navigation the reader has left.
    // Not on the first render — landing in the client is not a navigation, and moving focus there would scroll the
    // page out from under somebody who has not asked to go anywhere. What the ref holds is therefore the space that
    // was last shown rather than whether the effect has run before: the second is what StrictMode's extra invocation
    // makes true on the first mount, which would move focus on landing in every development run.
    //
    // One ref for the one space in front, handed to that space's region and to no other. React detaches every ref it
    // is taking away before it attaches the ones it is adding, so by the time this runs the ref holds the region
    // arrived at rather than the one left.
    useEffect(() => {
        if (shown.current !== space) {
            inFrontRegion.current?.focus();
            shown.current = space;
        }
    }, [space]);

    // **Every space is drawn whichever one is in front, and the others are stood aside rather than taken down.** What
    // a space holds is the thing it was reading, the pages of it the deployment answered, the place in those pages the
    // reader had scrolled to, what they had picked out and what they had open — and every one of those is state of the
    // components it renders, so a space taken down on the way to another answers all of them by reading again from the
    // leading end, as a skeleton, with the reader back at the top. That is a client rebuilding the screen somebody just
    // left rather than one they are coming back to.
    //
    // Two consequences are intended. A space that reads when it mounts reads once the frame is reached rather than at
    // the first visit to it, which is what buys the switch; and a space that draws a live region goes on drawing one
    // while it is aside, which is what `inert` and `aria-hidden` below are there to contain.
    //
    // Standing a space aside is `visibility` rather than `display`, and that is the whole reason it is positioned: a box
    // taken out of the layout takes its scroller's offset with it — the one part of this state the platform holds
    // rather than this client, since `scrollTop` on an element with no box reads zero and cannot be written — so the
    // list would come back holding its pages and still be at the top of them. Keeping the box keeps the offset, which
    // is why an aside space is laid out over the region it stands in instead of beside it.
    //
    // It is taken out of the accessibility tree and made inert while it is aside, because `visibility` alone is a
    // statement to the engine that lays out rather than to everything that reads: a reader on one space must not find a
    // second `main`, must not tab into a list nobody can see, and must not be told by a live region what a screen they
    // are not on is doing.
    return (
        <>
            {offered.map((name) => {
                const inFront = name === space;
                const isMail = name === 'mail';

                return (
                    <main
                        key={name}
                        ref={inFront ? inFrontRegion : null}
                        tabIndex={-1}
                        aria-label={translate(spaceLabels[name])}
                        aria-hidden={inFront ? undefined : true}
                        inert={!inFront}
                        className={`${
                            isMail
                                ? 'flex min-h-0 flex-col overflow-hidden'
                                : 'overflow-y-auto px-4 py-6 workspace:px-8'
                        } ${inFront ? 'flex-1' : 'invisible absolute inset-0'}`}
                    >
                        {/* Mail is the one space with anything in it, and the design project draws it without a title:
                            the columns are what it is, and a heading over them would be a word above the thing the word
                            names. Every region still carries its name, because a landmark a reader moves to is
                            announced by it. */}
                        {isMail ? (
                            /* The question and the connection are the two the frame composes for every space, so they
                               are handed to the one in front and to nothing else: two of either would be two live
                               copies of one control — a second field somebody's question could be typed into, and a
                               second region saying what the deployment is doing. Neither holds anything a space would
                               lose, the question being the workspace's. */
                            <MailSpace
                                folders={folders}
                                list={list}
                                mail={mail}
                                tabs={tabs}
                                intent={inFront ? intent : null}
                                status={inFront ? status : null}
                                person={person}
                            />
                        ) : (
                            <div className="flex max-w-3xl flex-col gap-3">
                                <h1 className="text-4xl font-semibold tracking-tight">
                                    {translate(spaceLabels[name])}
                                </h1>

                                {inFront ? status : null}

                                {/* The note belongs to a space that holds nothing, which is every space but Mail
                                    today. */}
                                {implementedSpaces.includes(name) ? null : (
                                    <p className="text-base text-muted">{translate('space.pending')}</p>
                                )}

                                {inFront ? intent : null}
                            </div>
                        )}
                    </main>
                );
            })}
        </>
    );
}
