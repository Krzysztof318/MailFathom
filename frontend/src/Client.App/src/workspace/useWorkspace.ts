// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import { everything, type MailScope } from './mailScope';

// What a person carries between the spaces. Discover, Mail, and Cases are one application rather than three, and this
// is what makes them one: the frame owns it, so moving to another space re-renders what is under the frame and leaves
// every value below untouched.
//
// The context and its hook sit apart from the provider that fills them for the reason `localization/useLocalization.ts`
// gives: a module Vite hot-reloads may export components alone.

export interface Workspace {
    /** What the list, the search, and the next question are all asked against, owned here rather than per screen. */
    readonly scope: MailScope;

    /**
     * The rows of the folder tree somebody has folded away, by the key each row is identified with.
     *
     * Folded rather than unfolded, so a tree nobody has touched shows what is in it: an owner who opens the client and
     * sees a column of closed mailboxes has to open every one of them before the client says anything.
     */
    readonly collapsed: readonly string[];

    /** What the person has open, once a space offers something to open. */
    readonly selection: string | null;

    /**
     * The part of what is open that a question would be asked about, or `null` where the whole of it is.
     *
     * It is the words a person selected rather than a position in anything, because what the intent field does with it
     * is quote it: a range would have to be resolved against a document that is drawn again on every read, and against
     * the same message read a second time under a different ask.
     */
    readonly fragment: string | null;

    /** What has been typed into the intent field, which the next question would be asked with. */
    readonly question: string;
}

export interface WorkspaceRevision {
    readonly workspace: Workspace;

    /** Changes the named parts and leaves the rest of the workspace as it was. */
    readonly revise: (change: Partial<Workspace>) => void;
}

export const emptyWorkspace: Workspace = {
    scope: everything,
    collapsed: [],
    selection: null,
    fragment: null,
    question: '',
};

export const WorkspaceContext = createContext<WorkspaceRevision | null>(null);

export function useWorkspace(): WorkspaceRevision {
    const revision = useContext(WorkspaceContext);

    if (revision === null) {
        throw new Error('A component read the workspace outside the WorkspaceProvider that main.tsx mounts.');
    }

    return revision;
}
