// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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

export function WorkspaceProvider({ children }: { readonly children: ReactNode }) {
    const [workspace, setWorkspace] = useState<Workspace>(rememberedWorkspace);

    const revise = useCallback((change: Partial<Workspace>) => {
        setWorkspace((current) => ({ ...current, ...change }));
    }, []);

    useEffect(() => {
        rememberWorkspace(workspace);
    }, [workspace]);

    const revision = useMemo<WorkspaceRevision>(() => ({ workspace, revise }), [workspace, revise]);

    return <WorkspaceContext value={revision}>{children}</WorkspaceContext>;
}
