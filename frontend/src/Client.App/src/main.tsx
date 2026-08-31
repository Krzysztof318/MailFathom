// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { adoptedDeployment } from './deployment/adoptedDeployment';
import { sendToDeployment } from './deployment/sendToDeployment';
import { LocalizationProvider } from './localization/Localization';
import './styles.css';

const container = document.getElementById('root');

if (container === null) {
    throw new Error('index.html carries no #root element for the client to mount into.');
}

// The edge of the application, and the one place the deployment this run belongs to is resolved. It arrives below as a
// value, which is what keeps the difference between a client served by its deployment and a client somebody pointed at
// one out of every screen underneath.
createRoot(container).render(
    <StrictMode>
        <LocalizationProvider>
            <App deployment={adoptedDeployment()} send={sendToDeployment} />
        </LocalizationProvider>
    </StrictMode>,
);
