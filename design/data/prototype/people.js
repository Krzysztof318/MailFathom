// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

/* CONTACT AVATARS — PLACEHOLDERS.
   The images below are generated for the prototype and embedded as data URIs (so the demo also works opened
   from a file / in a new tab, with no dependency on ./avatars/, where the same PNG files live).
   In the real app a contact photo comes from the server together with the contact (photo/avatarUrl field in the
   address-book response: LDAP thumbnailPhoto, vCard PHOTO, Exchange/Graph /users/{id}/photo, MailFathom's own API),
   is cached locally and refreshed on contact sync. Map key = full sender name.
   No entry = contact without a photo - the UI must work without an avatar (system senders, mailing lists, unknown addresses). */
MailFathomDesign.data.avatars = {
  "Anna Kowalska": "data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20viewBox%3D%220%200%2072%2072%22%3E%3Crect%20width%3D%2272%22%20height%3D%2272%22%20fill%3D%22%23c9d6e8%22%2F%3E%3Ccircle%20cx%3D%2236%22%20cy%3D%2229%22%20r%3D%2211.2%22%20fill%3D%22%235b7ba6%22%2F%3E%3Cpath%20d%3D%22M14.4%2072c0-13.3%209.7-20.2%2021.6-20.2S57.6%2058.7%2057.6%2072z%22%20fill%3D%22%235b7ba6%22%2F%3E%3C%2Fsvg%3E",
  "Marta Nowak": "data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20viewBox%3D%220%200%2072%2072%22%3E%3Crect%20width%3D%2272%22%20height%3D%2272%22%20fill%3D%22%23e8d9c9%22%2F%3E%3Ccircle%20cx%3D%2236%22%20cy%3D%2229%22%20r%3D%2211.2%22%20fill%3D%22%23a67f5b%22%2F%3E%3Cpath%20d%3D%22M14.4%2072c0-13.3%209.7-20.2%2021.6-20.2S57.6%2058.7%2057.6%2072z%22%20fill%3D%22%23a67f5b%22%2F%3E%3C%2Fsvg%3E",
  "Piotr Zieliński": "data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20viewBox%3D%220%200%2072%2072%22%3E%3Crect%20width%3D%2272%22%20height%3D%2272%22%20fill%3D%22%23cfe0d3%22%2F%3E%3Ccircle%20cx%3D%2236%22%20cy%3D%2229%22%20r%3D%2211.2%22%20fill%3D%22%235f8a6c%22%2F%3E%3Cpath%20d%3D%22M14.4%2072c0-13.3%209.7-20.2%2021.6-20.2S57.6%2058.7%2057.6%2072z%22%20fill%3D%22%235f8a6c%22%2F%3E%3C%2Fsvg%3E",
  "Jacek Wrona": "data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20viewBox%3D%220%200%2072%2072%22%3E%3Crect%20width%3D%2272%22%20height%3D%2272%22%20fill%3D%22%23dcd6e8%22%2F%3E%3Ccircle%20cx%3D%2236%22%20cy%3D%2229%22%20r%3D%2211.2%22%20fill%3D%22%237a6ba6%22%2F%3E%3Cpath%20d%3D%22M14.4%2072c0-13.3%209.7-20.2%2021.6-20.2S57.6%2058.7%2057.6%2072z%22%20fill%3D%22%237a6ba6%22%2F%3E%3C%2Fsvg%3E",
  "Robert Lis": "data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20viewBox%3D%220%200%2072%2072%22%3E%3Crect%20width%3D%2272%22%20height%3D%2272%22%20fill%3D%22%23e8d2d2%22%2F%3E%3Ccircle%20cx%3D%2236%22%20cy%3D%2229%22%20r%3D%2211.2%22%20fill%3D%22%23a66b6b%22%2F%3E%3Cpath%20d%3D%22M14.4%2072c0-13.3%209.7-20.2%2021.6-20.2S57.6%2058.7%2057.6%2072z%22%20fill%3D%22%23a66b6b%22%2F%3E%3C%2Fsvg%3E",
  "Ewa Sikora": "data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20viewBox%3D%220%200%2072%2072%22%3E%3Crect%20width%3D%2272%22%20height%3D%2272%22%20fill%3D%22%23d3e2e8%22%2F%3E%3Ccircle%20cx%3D%2236%22%20cy%3D%2229%22%20r%3D%2211.2%22%20fill%3D%22%235b8ea6%22%2F%3E%3Cpath%20d%3D%22M14.4%2072c0-13.3%209.7-20.2%2021.6-20.2S57.6%2058.7%2057.6%2072z%22%20fill%3D%22%235b8ea6%22%2F%3E%3C%2Fsvg%3E",
  "Tomasz Bąk": "data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20viewBox%3D%220%200%2072%2072%22%3E%3Crect%20width%3D%2272%22%20height%3D%2272%22%20fill%3D%22%23e4e0cd%22%2F%3E%3Ccircle%20cx%3D%2236%22%20cy%3D%2229%22%20r%3D%2211.2%22%20fill%3D%22%23958b57%22%2F%3E%3Cpath%20d%3D%22M14.4%2072c0-13.3%209.7-20.2%2021.6-20.2S57.6%2058.7%2057.6%2072z%22%20fill%3D%22%23958b57%22%2F%3E%3C%2Fsvg%3E",
  "Karolina Kowalska": "data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20viewBox%3D%220%200%2072%2072%22%3E%3Crect%20width%3D%2272%22%20height%3D%2272%22%20fill%3D%22%23cdd8f0%22%2F%3E%3Ccircle%20cx%3D%2236%22%20cy%3D%2229%22%20r%3D%2211.2%22%20fill%3D%22%234f66a8%22%2F%3E%3Cpath%20d%3D%22M14.4%2072c0-13.3%209.7-20.2%2021.6-20.2S57.6%2058.7%2057.6%2072z%22%20fill%3D%22%234f66a8%22%2F%3E%3C%2Fsvg%3E",
};

MailFathomDesign.data.contactInsights = {
  anna: { reply: "≈ 3 h", peak: "mornings 8-11", style: "Specifics, numbers, short paragraphs", next: "Send the 5% CPI cap counter-proposal and confirm you accept the 2 h SLA.", wait: "waiting on us for 2 days" },
  marta: { reply: "≈ 1 h", peak: "midday", style: "Numbers and spreadsheets, no generalities", next: "Confirm the negotiating line so she can close the 2027 budget.", wait: "waiting on us for 1 day" },
  piotr: { reply: "≈ 5 h", peak: "afternoons", style: "Bullet points and dates", next: "Ask for a firm UAT date - it is the only open item.", wait: "" },
  jacek: { reply: "≈ 1 day", peak: "mornings", style: "Formal, with clause references", next: "Ask for ready wording of the indexation cap to paste into the addendum.", wait: "" },
  tomasz: { reply: "≈ 4 h", peak: "mornings", style: "To the point, with dates", next: "Confirm or challenge the 12.11 date - deadline 02.09.", wait: "waiting on us for 1 day" },
  ewa: { reply: "≈ 2 h", peak: "mornings", style: "Direct questions, expects a clear answer", next: "Answer whether weekend support is possible and on what terms.", wait: "waiting on us for 1 day" },
};

MailFathomDesign.data.contacts = [
  { id: "anna", name: "Anna Kowalska", role: "Chief Operating Officer", org: "Contoso", mail: "a.kowalska@contoso.example",
    last: "today 09:14", owed: "Addendum decision by 28.08", cases: "Contoso contract renegotiation 2027",
    note: "Leads the addendum renegotiation. Usually replies the same day; expects our decision before the end of the week.",
    threads: [{ id: "contoso", label: "Contract addendum - signatures", meta: "6 messages" }, { id: "anna2", label: "Re: proposed terms for 2027", meta: "3 messages" }],
    docs: [{ type: "PDF", name: "SLA addendum.pdf", size: "248 kB" }, { type: "XLSX", name: "Calculation_2027.xlsx", size: "42 kB" }] },
  { id: "marta", name: "Marta Nowak", role: "Controlling", org: "Finance · internal", mail: "m.nowak@company.example",
    last: "yesterday 11:20", owed: "Our reply by 27.08", cases: "Contoso contract renegotiation 2027",
    note: "Calculated the impact of the increase on annual cost. Waiting on a decision to update the 2027 budget.",
    threads: [{ id: "marta", label: "CPI calculation 2027", meta: "4 messages" }],
    docs: [{ type: "XLSX", name: "CPI_2027.xlsx", size: "34 kB" }] },
  { id: "piotr", name: "Piotr Zieliński", role: "Head of Delivery", org: "Delivery · internal", mail: "p.zielinski@company.example",
    last: "yesterday 16:41", owed: "Piotr: UAT test plan", cases: "-",
    note: "Confirmed phase two of the schedule. The UAT window is still unset.",
    threads: [{ id: "piotr", label: "Re: rollout schedule", meta: "9 messages" }],
    docs: [] },
  { id: "jacek", name: "Jacek Wrona", role: "Attorney at law", org: "Wrona Law Office", mail: "j.wrona@lawoffice.example",
    last: "24.08.2026", owed: "None", cases: "Contoso renegotiation · IT contract audit",
    note: "Confirmed that an indexation cap is permissible. Preparing the Northwind NDA.",
    threads: [{ id: "jacek", label: "Legal opinion - indexation cap", meta: "2 messages" }, { id: "prawnik2", label: "Draft NDA - Northwind", meta: "2 messages" }],
    docs: [{ type: "DOCX", name: "NDA_Northwind.docx", size: "72 kB" }] },
  { id: "tomasz", name: "Tomasz Bąk", role: "Account manager", org: "Fabrikam", mail: "t.bak@fabrikam.example",
    last: "yesterday 17:05", owed: "Our confirmation by 02.09", cases: "Fabrikam Q4 deliveries",
    note: "Reported a 15-day delivery slip caused by a stoppage at the manufacturer.",
    threads: [{ id: "fabrikam1", label: "Q4 delivery dates", meta: "7 messages" }],
    docs: [] },
  { id: "ewa", name: "Ewa Sikora", role: "IT Manager", org: "Adventure Works", mail: "e.sikora@adventure.example",
    last: "yesterday 10:15", owed: "Weekend SLA answer by 01.09", cases: "-",
    note: "Asking about extending support to weekends - the current contract covers business days.",
    threads: [{ id: "klient3", label: "Question about weekend SLA", meta: "4 messages" }],
    docs: [] },
];
