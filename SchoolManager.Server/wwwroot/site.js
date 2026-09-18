"use strict";

// Füllt den Download-Bereich mit der neuesten öffentlichen Version. Klappt das
// nicht, bleiben die Links auf der Release-Seite von GitHub stehen - der
// Download geht also immer, nur ohne Nummer und Grösse.

const dateFormat = new Intl.DateTimeFormat("de-CH", { dateStyle: "long" });

function megabytes(bytes) {
    return bytes ? `${(bytes / 1024 / 1024).toFixed(0)} MB` : "";
}

async function showLatest() {
    let latest;

    try {
        const response = await fetch("/api/public/latest", { headers: { Accept: "application/json" } });
        if (!response.ok) throw new Error(String(response.status));
        latest = await response.json();
    } catch {
        // Ohne Auskunft vom Server gibt es keine Datei zum Laden - dann das sagen,
        // statt auf einen Knopf zu führen, der ins Leere greift.
        for (const node of document.querySelectorAll("[data-latest-meta], [data-latest-title]"))
            node.textContent = "Der Download ist gerade nicht erreichbar. Bitte später nochmals versuchen.";

        for (const link of document.querySelectorAll("[data-download]"))
            link.hidden = true;

        return;
    }

    const date = latest.publishedAt ? dateFormat.format(new Date(latest.publishedAt)) : "";

    for (const link of document.querySelectorAll("[data-download='installer']"))
        link.href = latest.installer.url;

    for (const link of document.querySelectorAll("[data-download='exe']")) {
        if (latest.exe) link.href = latest.exe.url;
        else link.hidden = true;
    }

    for (const node of document.querySelectorAll("[data-size='installer']"))
        node.textContent = megabytes(latest.installer.size);

    for (const node of document.querySelectorAll("[data-size='exe']"))
        node.textContent = latest.exe ? megabytes(latest.exe.size) : "";

    for (const node of document.querySelectorAll("[data-latest-meta]"))
        node.textContent = `Version ${latest.version}${date ? ` vom ${date}` : ""} · Windows 10 und 11`;

    for (const node of document.querySelectorAll("[data-latest-badge]")) {
        node.textContent = `Version ${latest.version}`;
        node.hidden = false;
    }

    for (const node of document.querySelectorAll("[data-latest-title]"))
        node.textContent = `Veröffentlicht${date ? ` am ${date}` : ""} – für Windows 10 und 11 (64 Bit).`;

    // Die Update-Info aus dem Admin-Panel, falls eine geschrieben wurde.
    for (const node of document.querySelectorAll("[data-latest-note]")) {
        node.textContent = latest.note;
        node.hidden = !latest.note;
    }

}

/** Das Changelog: je Version ihre Punkte, die neueste zuoberst. */
async function showChangelog() {
    const container = document.querySelector("[data-changelog]");

    if (!container) return;

    let versions;

    try {
        const response = await fetch("/api/public/changelog", { headers: { Accept: "application/json" } });
        if (!response.ok) throw new Error(String(response.status));
        versions = await response.json();
    } catch {
        container.replaceChildren(el("p", { class: "faint" }, "Die Änderungen sind gerade nicht erreichbar."));
        return;
    }

    if (versions.length === 0) {
        container.replaceChildren(el("p", { class: "faint" }, "Noch keine Version veröffentlicht."));
        return;
    }

    container.replaceChildren(...versions.map((version, index) => {
        const head = el("div", { class: "release-head" },
            el("h3", {}, `Version ${version.version}`),
            el("span", { class: "release-date" }, version.publishedAt ? dateFormat.format(new Date(version.publishedAt)) : ""),
            index === 0 ? el("span", { class: "badge" }, "neueste") : null);

        const points = version.changes.length > 0
            ? el("ul", { class: "release-changes" }, ...version.changes.map((change) => el("li", {}, change)))
            : el("p", { class: "faint" }, version.note || "Keine Angaben zu dieser Version.");

        return el("article", { class: "release" }, head, points);
    }));
}

/** Kleiner Helfer: Element mit Klassen und Text - alles als Text, nie als HTML. */
function el(tag, props, ...children) {
    const node = document.createElement(tag);

    if (props?.class) node.className = props.class;

    for (const child of children) {
        if (child === null || child === undefined) continue;
        node.append(child instanceof Node ? child : document.createTextNode(String(child)));
    }

    return node;
}

showLatest();
showChangelog();
