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
    list,
    mail,
}: {
    readonly space: SpaceName;
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

    return (
        <main ref={region} tabIndex={-1} className="flex-1 overflow-y-auto px-4 py-6 workspace:px-8">
            {/* Mail is the one space laid out in columns, so it is the one that is not held to a reading width: a
                measure that keeps prose readable is the wrong bound for a scope selector beside a list beside a
                message. */}
            <div className={`flex flex-col gap-3 ${space === 'mail' ? '' : 'max-w-3xl'}`}>
                <h1 className="text-2xl font-semibold tracking-tight">{translate(spaceLabels[space])}</h1>

                {/* The note belongs to a space that holds nothing, which Mail no longer is: it reads its own mail now,
                    and a sentence saying otherwise above a working list is a screen contradicting itself. What Mail
                    holds is composed above rather than reached for here, because this region owns where a space is
                    drawn and what happens to focus, and a space that read something of its own would make that true of
                    one of the three and not the others. */}
                {space === 'mail' ? null : <p className="text-sm text-muted">{translate('space.pending')}</p>}

                {/* Mail is the one space with anything in it, and what it has is the three regions a mail client is:
                    the scope selector, the list of what is in that scope, and the message being read. Stacked under a
                    narrow window and side by side at the width the workspace opens out at, out of one tree at one
                    breakpoint rather than one composition per head. */}
                {space === 'mail' ? (
                    <div className="flex flex-col gap-6 workspace:flex-row workspace:items-start">
                        <div className="workspace:w-64 workspace:shrink-0">{folders}</div>
                        <div className="min-w-0 workspace:w-96 workspace:shrink-0">{list}</div>
                        <div className="min-w-0 flex-1">{mail}</div>
                    </div>
                ) : null}
            </div>
        </main>
    );
}
