// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// What the document preview shows for an attachment. The addendum and the cost spreadsheet have pages of their own;
// every other file gets its type, size and name, then the lines under `other`.
MailFathomDesign.data.documentPreviews = {
  addendum: [
    { s: true, text: "ADDENDUM NO. 3 TO THE MASTER AGREEMENT OF 14.06.2021" },
    { h: true, text: "Change of service level and remuneration" },
    { text: "§ 1. The response time for a critical-priority incident is shortened from 4 hours to 2 hours on business days, between 8:00 and 18:00." },
    { text: "§ 2. In return for the higher service level, the flat fee increases by 8% from the settlement period beginning 01.01.2027." },
    { text: "§ 3. The rules for indexing the fee to CPI remain unchanged from § 11 of the master agreement. The parties do not set an upper cap on indexation." },
    { text: "§ 4. All other provisions remain unchanged. The addendum takes effect on the date both parties sign." },
    { s: true, text: "Signatures of the parties" },
    { text: "Contoso Ltd - Anna Kowalska, Chief Operating Officer" },
    { text: "Nordwind Ltd - ......................................" },
  ],
  spreadsheet: [
    { h: true, text: "Annual cost calculation 2027" },
    { text: "Base fee 2026: €236,000" },
    { text: "Increase from the addendum (8%): €18,880" },
    { text: "CPI indexation (4.1% forecast): €10,448" },
    { text: "Annual cost after changes: €265,328" },
    { text: "Difference versus 2026: +€29,328" },
  ],
  other: [
    { text: "Document preview opened in the app - no file downloaded to disk. The content comes from the message attachment and is indexed by MailFathom for citations." },
    { text: "Passages cited in “Discover” results are highlighted where they came from." },
  ],
};
