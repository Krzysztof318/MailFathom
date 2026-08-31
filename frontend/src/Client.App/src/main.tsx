// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { adoptedDeployment } from './deployment/adoptedDeployment';
import { sendToDeployment } from './deployment/sendToDeployment';
import { LocalizationProvider } from './localization/Localization';
import { credentialStore } from './signIn/credentialStore';
import { ThemeProvider } from './theme/Theme';
import { WorkspaceProvider } from './workspace/Workspace';
import './styles.css';

const container = document.getElementById('root');

if (container === null) {
    throw new Error('index.html carries no #root element for the client to mount into.');
}

void open(container);

/**
 * The edge of the application, and the one place the deployment this run belongs to and the credential it holds are
 * resolved. Both arrive below as values, which is what keeps the difference between the two heads — a client served by
 * its deployment or pointed at one, a credential in a keychain or in the tab — out of every screen underneath.
 *
 * The credential store is asked before anything is rendered rather than while it is, because a screen that mounts
 * signed out and then swaps to the workspace is a screen somebody has already started reading.
 *
 * The three things that outlive every screen stand above it: the language it reads in, the theme it is painted in, and
 * what the person is carrying between the spaces. Each is above the frame because nothing below the frame may be what
 * decides it, and each is above the sign-in screen for the same reason — somebody who has not signed in yet reads in a
 * language and is painted in a theme exactly as somebody who has.
 */
async function open(root: HTMLElement): Promise<void> {
    const deployment = adoptedDeployment();
    const credentials = await credentialStore();
    const signedInWith = deployment === null ? null : await credentials.read(deployment.deployment);

    createRoot(root).render(
        <StrictMode>
            <LocalizationProvider>
                <ThemeProvider>
                    <WorkspaceProvider>
                        <App
                            credentials={credentials}
                            deployment={deployment}
                            send={sendToDeployment}
                            signedInWith={signedInWith}
                        />
                    </WorkspaceProvider>
                </ThemeProvider>
            </LocalizationProvider>
        </StrictMode>,
    );
}
