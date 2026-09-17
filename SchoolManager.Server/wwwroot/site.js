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
        for (const node of document.querySelectorAll("[data-latest-meta]"))
            node.textContent = "Die neueste Version liegt auf GitHub bereit.";
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

    for (const link of document.querySelectorAll("[data-release-link]"))
        link.href = latest.releaseUrl.replace(/\/tag\/[^/]+$/, "");
}

showLatest();
