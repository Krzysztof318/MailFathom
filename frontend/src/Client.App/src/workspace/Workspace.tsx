// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { rememberedWorkspace, rememberWorkspace } from './rememberedWorkspace';
import { WorkspaceContext, type Workspace, type WorkspaceRevision } from './useWorkspace';

// Mounted above the frame rather than inside a space, which is the whole mechanism: a space is what the address
// changes, and this is not below the address.
//
// What it opens with is what this tab was last looking at, and what it holds is written back where the next start
// reads it from. Keeping it is a browser store being synchronized rather than a value being computed, which is what an
// effect is for; the alternative — writing inside the revision — would either put a side effect in an updater React
// invokes twice under StrictMode, or make `revise` a new function on every render, which is a read restarted on every
// render for everything holding it.

// A conversation, the full-HTML surface, and a file each stand in front of the message they were opened from, so
// picking a different message is what closes any of them. Each is one decision with the selection rather than a value
// kept in step with it: left free to disagree, one would stay on the screen with nothing reacting to the click behind
// it, and the way out of it — back to the message — would return to whichever row somebody picked last rather than to
// the one they opened it from. The surface has a second reason of its own: it draws a stranger's markup for one
// message, and carrying it onto the next would draw markup nobody asked to be shown.
//
// It is decided here rather than wherever a row is clicked because this is where all three live: a screen that picks a
// message would otherwise each have to remember to close what it never knew was in front of it, and the one that
// forgets is the defect.
function withoutAStaleFront(current: Workspace, change: Partial<Workspace>): Partial<Workspace> {
    if (change.selection === undefined || change.selection === current.selection) {
        return change;
    }

    return {
        ...change,
        conversation: change.conversation ?? null,
        fullHtml: change.fullHtml ?? null,
        attachment: change.attachment ?? null,
    };
}

export function WorkspaceProvider({ children }: { readonly children: ReactNode }) {
    const [workspace, setWorkspace] = useState<Workspace>(rememberedWorkspace);

    const revise = useCallback((change: Partial<Workspace>) => {
        setWorkspace((current) => ({ ...current, ...withoutAStaleFront(current, change) }));
    }, []);

    useEffect(() => {
        rememberWorkspace(workspace);
    }, [workspace]);

    const revision = useMemo<WorkspaceRevision>(() => ({ workspace, revise }), [workspace, revise]);

    return <WorkspaceContext value={revision}>{children}</WorkspaceContext>;
}
