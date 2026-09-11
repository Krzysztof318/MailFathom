// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, type ReactNode } from 'react';
import { useLocalization } from '../localization/useLocalization';
import { MailSpace } from '../mailSpace/MailSpace';
import { implementedSpaces, spaceLabels, type Space as SpaceName } from '../routing/spaces';

// The region the address decides the contents of. What each space actually holds is built by its own issue; what this
// owns permanently is where a space is rendered and what happens to focus when the address changes.

export function Space({
    space,
    intent,
    status,
    folders,
    list,
    mail,
    tabs,
    person,
}: {
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
    const region = useRef<HTMLElement>(null);
    const mailRegion = useRef<HTMLElement>(null);
    const shown = useRef(space);

    const mailInFront = space === 'mail';

    // Navigation puts focus at the start of the new content, which is where keyboard and screen-reader use otherwise
    // silently stops working: focus would stay on the link that was activated, in navigation the reader has left.
    // Not on the first render — landing in the client is not a navigation, and moving focus there would scroll the
    // page out from under somebody who has not asked to go anywhere. What the ref holds is therefore the space that
    // was last shown rather than whether the effect has run before: the second is what StrictMode's extra invocation
    // makes true on the first mount, which would move focus on landing in every development run.
    useEffect(() => {
        if (shown.current !== space) {
            (space === 'mail' ? mailRegion : region).current?.focus();
            shown.current = space;
        }
    }, [space]);

    // Mail is the one space with anything in it, and the design project draws it without a title: the columns are what
    // it is, and a heading over them would be a word above the thing the word names. The region still carries the name,
    // because a landmark a reader moves to is announced by it.
    //
    // **It is drawn whichever space is in front, and stood aside rather than taken down.** What Mail holds is the
    // folder it was reading, the pages of it the deployment answered, the place in those pages the reader had scrolled
    // to, the messages they had picked out and what they had open — and every one of those is state of the components
    // this renders, so a Mail space taken down on the way to another space answers all five by reading the folder
    // again from its leading end, as a skeleton, with the reader back at the top of it. That is a client rebuilding the
    // screen somebody just left rather than one they are coming back to.
    //
    // Standing it aside is `visibility` rather than `display`, and that is the whole reason it is positioned: a box
    // taken out of the layout takes its scroller's offset with it — the one part of this state the platform holds
    // rather than this client, since `scrollTop` on an element with no box reads zero and cannot be written — so the
    // list would come back holding its pages and still be at the top of them. Keeping the box keeps the offset, which
    // is why the aside space is laid out over the region it stands in instead of beside it.
    //
    // It is taken out of the accessibility tree and made inert while it is aside, because `visibility` alone is a
    // statement to the engine that lays out rather than to everything that reads: a reader on another space must not
    // find a second `main` naming Mail, must not tab into a list nobody can see, and must not be told by a live region
    // in it what a screen they are not on is doing.
    return (
        <>
            <main
                ref={mailRegion}
                tabIndex={-1}
                aria-label={translate(spaceLabels.mail)}
                aria-hidden={mailInFront ? undefined : true}
                inert={!mailInFront}
                className={`flex min-h-0 flex-col overflow-hidden ${
                    mailInFront ? 'flex-1' : 'invisible absolute inset-0'
                }`}
            >
                {/* The question and the connection are the two the frame composes for every space, so they are handed
                    to the one in front and to nothing else: two of either would be two live copies of one control —
                    a second field somebody's question could be typed into, and a second region saying what the
                    deployment is doing. Neither holds anything Mail would lose, the question being the workspace's. */}
                <MailSpace
                    folders={folders}
                    list={list}
                    mail={mail}
                    tabs={tabs}
                    intent={mailInFront ? intent : null}
                    status={mailInFront ? status : null}
                    person={person}
                />
            </main>

            {mailInFront ? null : (
                <main
                    ref={region}
                    tabIndex={-1}
                    aria-label={translate(spaceLabels[space])}
                    className="flex-1 overflow-y-auto px-4 py-6 workspace:px-8"
                >
                    <div className="flex max-w-3xl flex-col gap-3">
                        <h1 className="text-4xl font-semibold tracking-tight">{translate(spaceLabels[space])}</h1>

                        {status}

                        {/* The note belongs to a space that holds nothing, which is every space but Mail today. */}
                        {implementedSpaces.includes(space) ? null : (
                            <p className="text-base text-muted">{translate('space.pending')}</p>
                        )}

                        {intent}
                    </div>
                </main>
            )}
        </>
    );
}
