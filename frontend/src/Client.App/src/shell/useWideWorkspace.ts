// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useSyncExternalStore } from 'react';

// Whether the window is at or above the width the workspace opens out at — the one width `styles.css` declares as the
// point where a stack of screens becomes a rail beside columns. A stylesheet answers that for anything CSS can lay out
// two ways from one tree, and that is almost everything; what it cannot do is decide *which* of two screens is in the
// document, and the narrow Mail space is that case: it draws the list or the message, never both, so a row that is
// not on the screen is not in the document either.
//
// The query is built from the token rather than from a second copy of the number, so the breakpoint stays one
// decision.

function workspaceQuery(): MediaQueryList | null {
    if (typeof window.matchMedia !== 'function') {
        return null;
    }

    const width = getComputedStyle(document.documentElement).getPropertyValue('--breakpoint-workspace').trim();

    return window.matchMedia(`(min-width: ${width === '' ? '48.75rem' : width})`);
}

/** Whether the window is wide enough for the rail beside columns. A runtime that cannot answer is read as wide. */
function isWide(): boolean {
    return workspaceQuery()?.matches ?? true;
}

function watchWidth(changed: () => void): () => void {
    const watched = workspaceQuery();

    watched?.addEventListener('change', changed);

    return () => {
        watched?.removeEventListener('change', changed);
    };
}

/** Whether the workspace is laid out wide right now, kept current as the window is resized across the breakpoint. */
export function useWideWorkspace(): boolean {
    return useSyncExternalStore(watchWidth, isWide);
}
