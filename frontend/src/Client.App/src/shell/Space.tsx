// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, type ReactNode } from 'react';
import { useLocalization } from '../localization/useLocalization';
import { spaceLabels, type Space as SpaceName } from '../routing/spaces';

// The region the address decides the contents of. What each of the three spaces actually holds is built by its own
// issue; what this owns permanently is where a space is rendered and what happens to focus when the address changes.

export function Space({
    space,
    folders,
    mail,
}: {
    readonly space: SpaceName;
    readonly folders: ReactNode;
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

    return (
        <main ref={region} tabIndex={-1} className="flex-1 overflow-y-auto px-4 py-6 workspace:px-8">
            <div className="flex max-w-3xl flex-col gap-3">
                <h1 className="text-2xl font-semibold tracking-tight">{translate(spaceLabels[space])}</h1>

                {/* The note stands above whatever a space has so far rather than instead of it: none of the three is
                    built, and Mail is the one that has anything at all — the reading pane the messages a list has not
                    been written yet would be opened in. What it holds is composed above rather than reached for here,
                    because this region owns where a space is drawn and what happens to focus, and a space that read
                    something of its own would make that true of one of the three and not the others. */}
                <p className="text-sm text-muted">{translate('space.pending')}</p>

                {/* Mail is the one space with anything in it, and what it has is the scope selector beside what the
                    scope is about: a column under a narrow window and beside the reading pane at the width the
                    workspace opens out at. The list that will stand between them is #1424's. */}
                {space === 'mail' ? (
                    <div className="flex flex-col gap-6 workspace:flex-row workspace:items-start">
                        <div className="workspace:w-64 workspace:shrink-0">{folders}</div>
                        <div className="min-w-0 flex-1">{mail}</div>
                    </div>
                ) : null}
            </div>
        </main>
    );
}
