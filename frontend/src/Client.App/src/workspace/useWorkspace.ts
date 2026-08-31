// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';

// What a person carries between the spaces. Discover, Mail, and Cases are one application rather than three, and this
// is what makes them one: the frame owns it, so moving to another space re-renders what is under the frame and leaves
// every value below untouched.
//
// The context and its hook sit apart from the provider that fills them for the reason `localization/useLocalization.ts`
// gives: a module Vite hot-reloads may export components alone.

export interface Workspace {
    /** The account every question is asked against, or `null` for all of the owner's accounts at once. */
    readonly accountId: string | null;

    /** The folder within that account, once a space offers one to choose. */
    readonly folder: string | null;

    /** What the person has open, once a space offers something to open. */
    readonly selection: string | null;

    /** What has been typed into the intent field, which the next question would be asked with. */
    readonly question: string;
}

export interface WorkspaceRevision {
    readonly workspace: Workspace;

    /** Changes the named parts and leaves the rest of the workspace as it was. */
    readonly revise: (change: Partial<Workspace>) => void;
}

export const emptyWorkspace: Workspace = {
    accountId: null,
    folder: null,
    selection: null,
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
