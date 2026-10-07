// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

MailFathomDesign.data.plans = {
  contract: {
    label: "Answer + Timeline + Fact table",
    confidence: "high confidence",
    gap: "1 gap: 2022 addendum missing",
    meta: "run 7f3a · 41 documents searched",
    trace: "semantic retrieval → 41 candidates\ndeterministic rule: sort by addendum date\nmodel: summary + PresentationPlan choice",
    action: "Draft a counter-proposal capping indexation? Decision due 28.08",
    blocks: ["answer", "timeline", "table", "action"],
    parts: [
      { text: "The terms changed in three stages: the master agreement of April 2021", cite: "1" },
      { text: ", CPI indexation with no upper cap introduced in February 2023", cite: "3" },
      { text: ", the SLA cut from 4 h to 2 h in the November 2025 addendum", cite: "5" },
      { text: ". The 2027 proposal raises the price by 8% and keeps indexation uncapped", cite: "7" },
    ],
    timeline: [
      { date: "12.04.2021", title: "Master agreement", detail: "€1,200/mo · SLA 4 h", cite: "1" },
      { date: "03.02.2023", title: "CPI indexation", detail: "no upper cap", cite: "3" },
      { date: "18.11.2025", title: "SLA addendum", detail: "4 h → 2 h · +8%", cite: "5" },
      { date: "26.08.2026", title: "2027 proposal", detail: "decision due 28.08", cite: "7" },
    ],
    table: [
      { version: "Agreement 2021", versionCite: "1", price: "€1,200", priceCite: "1", sla: "4 h", slaCite: "1", index: "none", indexCite: null },
      { version: "Addendum 2023", versionCite: "3", price: "€1,261", priceCite: "1", sla: "4 h", slaCite: "1", index: "CPI, uncapped", indexCite: "3" },
      { version: "Addendum 2025", versionCite: "5", price: "€1,452", priceCite: "5", sla: "2 h", slaCite: "5", index: "CPI, uncapped", indexCite: "3" },
      { version: "Proposal 2027", versionCite: "7", price: "€1,568", priceCite: "7", sla: "2 h", slaCite: "5", index: "CPI, uncapped", indexCite: "3" },
    ],
    evidence: [
      { n: "1", title: "Master agreement.pdf", srcType: "attachment", snippet: "“Monthly remuneration is €1,200 net…”", meta: "12.04.2021 · relevance 0.94 · p. 3",
        pre: "§2.4 The scope of services covers maintenance of the production environment and support on business days.",
        quote: "§2.5 Monthly remuneration is €1,200 net. Response time for a critical incident is 4 hours.",
        post: "§2.6 The agreement is concluded for an indefinite term with three months' notice.",
        supports: "base price €1,200 · SLA 4 h", mail: "“Master agreement - version for signature”, 12.04.2021" },
      { n: "3", title: "Re: indexation from 2023", srcType: "mail", snippet: "“…indexed to CPI, with no upper cap.”", meta: "03.02.2023 · A. Kowalska · 0,91",
        pre: "Hello, coming back to the indexation of rates for next year.",
        quote: "As agreed, remuneration will be indexed to the CPI published by the statistics office, with no upper cap.",
        post: "Please confirm and we will prepare the addendum for signature.",
        supports: "CPI indexation · no upper cap", mail: "“Re: indexation from 2023”, 03.02.2023" },
      { n: "5", title: "SLA addendum.pdf", srcType: "attachment", snippet: "“Response time is shortened to 2 hours…”", meta: "18.11.2025 · relevance 0.89 · p. 2",
        pre: "§3.1 The parties confirm the scope of services under the master agreement of 12 April 2021.",
        quote: "§3.2 Response time for a critical incident is shortened from 4 hours to 2 hours on business days. In return for the higher service level, remuneration increases by 8%.",
        post: "§3.3 All other provisions, including CPI indexation, remain unchanged.",
        supports: "SLA 2 h · +8% price · indexation unchanged", mail: "“Contract addendum - signatures”, 18.11.2025" },
      { n: "7", title: "2027 proposal - Contoso", srcType: "mail", snippet: "“…an 8% rate increase from 1 January 2027.”", meta: "26.08.2026 · A. Kowalska · 0.97",
        pre: "Here is our proposal of terms for 2027, together with the calculation.",
        quote: "We propose an 8% rate increase from 1 January 2027 while keeping the 2-hour response time. CPI indexation remains uncapped.",
        post: "Please decide by 28 August so the addendum can be signed before year end.",
        supports: "price €1,568 · SLA 2 h · CPI uncapped", mail: "“Proposed terms 2027”, 26.08.2026" },
    ],
  },
  files: {
    label: "Answer + Attachments",
    confidence: "high confidence",
    gap: "",
    meta: "run 91c4 · 6 files matched",
    trace: "semantic retrieval inside attachment contents\nrule: newest version of a document first\nmodel: summary + PresentationPlan choice",
    action: "Download the latest policy and add a renewal reminder?",
    blocks: ["answer", "gallery", "action"],
    parts: [
      { text: "The current policy runs to 31.12.2026 and arrived attached to the broker's email of 14.12.2025", cite: "2" },
      { text: ". Older versions sit in the archive-2019 account", cite: "4" },
    ],
    gallery: [
      { type: "PDF", name: "Policy_2026.pdf", meta: "14.12.2025 · 640 kB · current", cite: "2" },
      { type: "PDF", name: "Policy_2025.pdf", meta: "09.12.2024 · 612 kB", cite: "4" },
      { type: "DOCX", name: "Coverage_scope.docx", meta: "14.12.2025 · 88 kB", cite: "2" },
      { type: "PNG", name: "Signature_scan.png", meta: "16.12.2025 · 1.4 MB", cite: "2" },
    ],
    evidence: [
      { n: "2", title: "Policy_2026.pdf", srcType: "attachment", snippet: "“Period of insurance: 01.01.2026 - 31.12.2026”", meta: "14.12.2025 · relevance 0.96 · p. 1",
        pre: "Policyholder: Northgate Ltd. Subject of insurance: professional indemnity.",
        quote: "Period of insurance: 01.01.2026 - 31.12.2026. Limit of indemnity: €2,000,000 per claim and in the aggregate.",
        post: "Premium payable in two instalments, the first due 15.01.2026.",
        supports: "valid to 31.12.2026 · €2m limit", mail: "“Policy for 2026 - for approval”, 14.12.2025" },
      { n: "4", title: "Policy_2025.pdf", srcType: "attachment", snippet: "“Period of insurance: 01.01.2025 - 31.12.2025”", meta: "09.12.2024 · relevance 0.72 · archive-2019",
        pre: "Previous version of the document, archive-2019 account.",
        quote: "Period of insurance: 01.01.2025 - 31.12.2025. Limit of indemnity: €1,500,000.",
        post: "Kept for comparison of indemnity limits.",
        supports: "previous limit €1.5m", mail: "“Policy 2025”, 09.12.2024" },
    ],
  },
  people: {
    label: "Answer + People",
    confidence: "medium confidence",
    gap: "no email confirmation from the board",
    meta: "run c220 · 18 messages, 3 people",
    trace: "retrieval by people and roles across threads\nrule: who replied in the decision thread\nmodel: summary + PresentationPlan choice",
    action: "Add the approval to the “Contoso renegotiation” case?",
    blocks: ["answer", "people", "action"],
    parts: [
      { text: "The 2023 indexation budget was approved by Marta Nowak in Finance, after Jacek Wrona's legal opinion", cite: "6" },
      { text: ". Anna Kowalska was Contoso's counterpart throughout the correspondence", cite: "3" },
    ],
    people: [
      { initials: "MN", name: "Marta Nowak", role: "Finance · approved the 2023 budget", last: "yesterday", cite: "6" },
      { initials: "JW", name: "Jacek Wrona", role: "Lawyer · opinion on the indexation cap", last: "24.08", cite: "6" },
      { initials: "AK", name: "Anna Kowalska", role: "Contoso · account manager", last: "today", cite: "3" },
    ],
    evidence: [
      { n: "6", title: "Re: 2023 indexation budget", srcType: "mail", snippet: "“I approve the calculation, we go with CPI.”", meta: "07.02.2023 · M. Nowak · 0.88",
        pre: "Thread: 2023 indexation budget, participants: Marta Nowak, Jacek Wrona, Chris Kasprowicz.",
        quote: "I approve the calculation, we go with CPI indexation. I am leaving the legal caveat about a cap to the next renegotiation.",
        post: "Please pass the decision on to Contoso.",
        supports: "budget approval · cap knowingly omitted", mail: "“Re: 2023 indexation budget”, 07.02.2023" },
      { n: "3", title: "Re: indexation from 2023", srcType: "mail", snippet: "“…indexed to CPI, with no upper cap.”", meta: "03.02.2023 · A. Kowalska · 0,91",
        pre: "Hello, coming back to the indexation of rates for next year.",
        quote: "As agreed, remuneration will be indexed to the CPI published by the statistics office, with no upper cap.",
        post: "Please confirm and we will prepare the addendum for signature.",
        supports: "Anna Kowalska's role in the agreement", mail: "“Re: indexation from 2023”, 03.02.2023" },
    ],
  },
};

// Answers Discover keeps for questions asked before, and how fresh each one is.
MailFathomDesign.data.savedAnswers = [
  { q: "What SLA commitments do we have to clients?", a: "4 contracts with a 4 h SLA, one being negotiated to 2 h.", fresh: "up to date" },
  { q: "Who owns Q4 renewals?", a: "Karolina (clients), Marta (budget), Jacek (wording).", fresh: "up to date" },
  { q: "Where are the insurance policies?", a: "3 files, the newest from 06.2026 in the broker thread.", fresh: "1 new mail" },
];

// The questions Discover suggests when nothing has been asked yet; `plan` names the blocks the answer is drawn in.
MailFathomDesign.data.suggestedQuestions = [
  { text: "How have the Contoso contract terms changed since 2021?", plan: "Answer · Timeline · Fact table" },
  { text: "Where is the latest version of the insurance policy?", plan: "Answer · Attachments" },
  { text: "Who approved the indexation budget in 2023?", plan: "Answer · People" },
];
