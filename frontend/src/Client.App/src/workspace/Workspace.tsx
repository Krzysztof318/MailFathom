// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { emptyWorkspace, WorkspaceContext, type Workspace, type WorkspaceRevision } from './useWorkspace';

// Mounted above the frame rather than inside a space, which is the whole mechanism: a space is what the address
// changes, and this is not below the address.

export function WorkspaceProvider({ children }: { readonly children: ReactNode }) {
    const [workspace, setWorkspace] = useState<Workspace>(emptyWorkspace);

    const revise = useCallback((change: Partial<Workspace>) => {
        setWorkspace((current) => ({ ...current, ...change }));
    }, []);

    const revision = useMemo<WorkspaceRevision>(() => ({ workspace, revise }), [workspace, revise]);

    return <WorkspaceContext value={revision}>{children}</WorkspaceContext>;
}
