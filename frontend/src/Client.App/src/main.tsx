// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { LocalizationProvider } from './localization/Localization';
import './styles.css';

const container = document.getElementById('root');

if (container === null) {
    throw new Error('index.html carries no #root element for the client to mount into.');
}

createRoot(container).render(
    <StrictMode>
        <LocalizationProvider>
            <App />
        </LocalizationProvider>
    </StrictMode>,
);
