// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { adoptedDeployment } from './deployment/adoptedDeployment';
import { sendToDeployment } from './deployment/sendToDeployment';
import { LocalizationProvider } from './localization/Localization';
import { ThemeProvider } from './theme/Theme';
import { WorkspaceProvider } from './workspace/Workspace';
import './styles.css';

const container = document.getElementById('root');

if (container === null) {
    throw new Error('index.html carries no #root element for the client to mount into.');
}

// The edge of the application, and the one place the deployment this run belongs to is resolved. It arrives below as a
// value, which is what keeps the difference between a client served by its deployment and a client somebody pointed at
// one out of every screen underneath.
//
// The three things that outlive every screen stand above it: the language it reads in, the theme it is painted in, and
// what the person is carrying between the spaces. Each is above the frame because nothing below the frame may be what
// decides it, and each is above the connect screen for the same reason — somebody who has not reached a deployment yet
// reads in a language and is painted in a theme exactly as somebody who has.
createRoot(container).render(
    <StrictMode>
        <LocalizationProvider>
            <ThemeProvider>
                <WorkspaceProvider>
                    <App deployment={adoptedDeployment()} send={sendToDeployment} />
                </WorkspaceProvider>
            </ThemeProvider>
        </LocalizationProvider>
    </StrictMode>,
);
