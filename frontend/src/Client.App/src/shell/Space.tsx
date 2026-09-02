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
}: {
    readonly space: SpaceName;

    /** The question the reader is composing, which every space carries somewhere. */
    readonly intent: ReactNode;

    /** What the deployment says about the connection, which every space shows somewhere. */
    readonly status: ReactNode;

    readonly folders: ReactNode;
    readonly list: ReactNode;
    readonly mail: ReactNode;
}) {
    const { translate } = useLocalization();
    const region = useRef<HTMLElement>(null);
    const shown = useRef(space);

    // Navigation puts focus at the start of the new content, which is where keyboard and screen-reader use otherwise
    // silently stops working: focus would stay on the link that was activated, in navigation the reader has left.
    // Not on the first render — landing in the client is not a navigation, and moving focus there would scroll the
    // page out from under somebody who has not asked to go anywhere. What the ref holds is therefore the space that
    // was last shown rather than whether the effect has run before: the second is what StrictMode's extra invocation
    // makes true on the first mount, which would move focus on landing in every development run.
    useEffect(() => {
        if (shown.current !== space) {
            region.current?.focus();
            shown.current = space;
        }
    }, [space]);

    // Mail is the one space with anything in it, and the design project draws it without a title: the columns are what
    // it is, and a heading over them would be a word above the thing the word names. The region still carries the name,
    // because a landmark a reader moves to is announced by it.
    if (space === 'mail') {
        return (
            <main
                ref={region}
                tabIndex={-1}
                aria-label={translate(spaceLabels[space])}
                className="flex min-h-0 flex-1 flex-col overflow-hidden"
            >
                <MailSpace folders={folders} list={list} mail={mail} intent={intent} status={status} />
            </main>
        );
    }

    return (
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
    );
}
