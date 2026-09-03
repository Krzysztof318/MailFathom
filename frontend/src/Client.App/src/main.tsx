// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { adoptedDeployment } from './deployment/adoptedDeployment';
import { AttachmentDeliveryContext, deliverAttachment } from './deployment/attachmentDelivery';
import { AttachmentUploadContext, uploadAttachment } from './deployment/attachmentUpload';
import { portraitExchange } from './deployment/portraitExchange';
import { sendToDeployment } from './deployment/sendToDeployment';
import { LocalizationProvider } from './localization/Localization';
import { configuredConnection } from './shellOperations/configuredConnection';
import { LinkOpenerContext, linkOpenerForThisApplication } from './shellOperations/linkOpener';
import { credentialStore } from './signIn/credentialStore';
import { clientTelemetryForThisApplication, TelemetryContext } from './telemetry/clientTelemetry';
import { ThemeProvider } from './theme/Theme';
import { WorkspaceProvider } from './workspace/Workspace';
import './styles.css';

// The one place a head is asked about. Everything below receives the operation rather than the answer to that
// question, which is what keeps one client one client across both heads.
const openLink = linkOpenerForThisApplication();

// The one place the client's traces, metrics, and logs are composed. It is here rather than beside a screen for the
// reason the link opener is: what this client reports about itself is the application's decision and not one screen's,
// and everything below receives it as a value rather than registering anything of its own.
const telemetry = clientTelemetryForThisApplication();

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
 * signed out and then swaps to the workspace is a screen somebody has already started reading. What a deployment
 * configured is asked first, for the same reason and one more: the address it may carry is what the credential kept on
 * this machine is filed under, so a client that read the store before it knew where it was pointed would read back the
 * credential of whichever deployment it was pointed at last.
 *
 * The four things that outlive every screen stand above it: the language it reads in, the theme it is painted in, what
 * the person is carrying between the spaces, and how a link they follow leaves the application. Each is above the frame
 * because nothing below the frame may be what decides it, and each is above the sign-in screen for the same reason —
 * somebody who has not signed in yet reads in a language and is painted in a theme exactly as somebody who has.
 */
async function open(root: HTMLElement): Promise<void> {
    const deployment = adoptedDeployment(await configuredConnection());
    const adopted = deployment.outcome === 'resolved' ? deployment.adopted : null;
    const credentials = await credentialStore();
    const signedInWith = adopted === null ? null : await credentials.read(adopted.deployment);

    createRoot(root).render(
        <StrictMode>
            <LocalizationProvider>
                <ThemeProvider>
                    <WorkspaceProvider>
                        <LinkOpenerContext value={openLink}>
                            <AttachmentDeliveryContext value={deliverAttachment}>
                                <AttachmentUploadContext value={uploadAttachment}>
                                    <TelemetryContext value={telemetry}>
                                        <App
                                            credentials={credentials}
                                            deployment={deployment}
                                            portraits={portraitExchange}
                                            send={sendToDeployment}
                                            signedInWith={signedInWith}
                                        />
                                    </TelemetryContext>
                                </AttachmentUploadContext>
                            </AttachmentDeliveryContext>
                        </LinkOpenerContext>
                    </WorkspaceProvider>
                </ThemeProvider>
            </LocalizationProvider>
        </StrictMode>,
    );
}
