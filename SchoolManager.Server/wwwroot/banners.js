"use strict";

// Streifen oben auf der Startseite und der Anmeldeseite - dieselben Meldungen,
// die im Admin-Panel unter „Meldungen“ für diese Seite angelegt sind. Rot für
// Fehler, grau für Hinweise, wie in School Manager. Weggeklickt bleibt eine
// Meldung weg, bis sie im Panel geändert wird.

(() => {
    const storageKey = "sm-dismissed-messages";

    function readDismissed() {
        try {
            return new Set(JSON.parse(localStorage.getItem(storageKey) ?? "[]"));
        } catch {
            return new Set();
        }
    }

    function saveDismissed(keys) {
        try {
            localStorage.setItem(storageKey, JSON.stringify([...keys]));
        } catch {
            // Ohne Speicher erscheint die Meldung beim nächsten Besuch eben wieder.
        }
    }

    async function show(container) {
        const page = container.dataset.pageMessages;
        let messages;

        try {
            const response = await fetch(`/api/public/messages?page=${encodeURIComponent(page)}`, { headers: { Accept: "application/json" } });
            if (!response.ok) return;
            messages = await response.json();
        } catch {
            return;
        }

        const dismissed = readDismissed();

        for (const message of messages) {
            const key = `${message.id}:${message.revision}`;
            if (dismissed.has(key)) continue;

            const banner = document.createElement("div");
            banner.className = `page-banner${message.kind === "Error" ? " error" : ""}`;
            banner.setAttribute("role", message.kind === "Error" ? "alert" : "status");

            const text = document.createElement("span");
            text.className = "page-banner-text";
            text.textContent = message.text;

            const close = document.createElement("button");
            close.type = "button";
            close.className = "page-banner-close";
            close.textContent = "✕";
            close.title = "Ausblenden, bis sich die Meldung ändert";
            close.setAttribute("aria-label", "Meldung ausblenden");
            close.addEventListener("click", () => {
                dismissed.add(key);
                saveDismissed(dismissed);
                banner.remove();
                container.hidden = container.children.length === 0;
            });

            banner.append(text, close);
            container.append(banner);
        }

        container.hidden = container.children.length === 0;
    }

    for (const container of document.querySelectorAll("[data-page-messages]"))
        show(container);
})();
