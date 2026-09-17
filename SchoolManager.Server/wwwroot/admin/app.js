"use strict";

// Admin-Panel von School Manager. Alles, was vom Server kommt, landet als
// Text im Dokument (textContent), nie als HTML - so kann kein Meldungstext
// oder Benutzername Code in die Seite schleusen.

const state = {
    token: sessionStorage.getItem("sm-token"),
    meta: null,
    view: null
};

const $ = (id) => document.getElementById(id);

// ==== Hilfen ====

function el(tag, props = {}, ...children) {
    const node = document.createElement(tag);

    for (const [key, value] of Object.entries(props)) {
        if (value === undefined || value === null || value === false) continue;
        if (key === "class") node.className = value;
        else if (key === "text") node.textContent = value;
        else if (key.startsWith("on")) node.addEventListener(key.slice(2), value);
        else if (key in node && typeof value !== "string") node[key] = value;
        else node.setAttribute(key, value === true ? "" : value);
    }

    for (const child of children.flat()) {
        if (child === null || child === undefined || child === false) continue;
        node.append(child instanceof Node ? child : document.createTextNode(String(child)));
    }

    return node;
}

class ApiError extends Error {
    constructor(status, message, body) {
        super(message);
        this.status = status;
        this.body = body;
    }
}

async function api(method, path, body) {
    const headers = { "Accept": "application/json" };
    if (state.token) headers.Authorization = `Bearer ${state.token}`;
    if (body !== undefined) headers["Content-Type"] = "application/json";

    let response;
    try {
        response = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
    } catch {
        throw new ApiError(0, "Der Server ist nicht erreichbar.");
    }

    const text = await response.text();
    let data = null;

    try {
        data = text ? JSON.parse(text) : null;
    } catch {
        // Keine JSON-Antwort, etwa von einem vorgeschalteten Proxy.
    }

    if (response.status === 401 && state.token && path !== "/api/auth/login") {
        signedOut("Die Anmeldung ist abgelaufen. Bitte neu anmelden.");
        throw new ApiError(401, "Bitte anmelden.");
    }

    if (!response.ok) throw new ApiError(response.status, data?.error ?? `Fehler ${response.status}`, data);

    return data;
}

let toastTimer;

function toast(message, kind = "") {
    const node = $("toast");
    node.textContent = message;
    node.className = `toast ${kind}`;
    node.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => (node.hidden = true), kind === "error" ? 7000 : 3500);
}

const dateFormat = new Intl.DateTimeFormat("de-CH", { dateStyle: "medium", timeStyle: "short" });

function formatDate(value) {
    return value ? dateFormat.format(new Date(value)) : "–";
}

function formatSize(bytes) {
    return bytes ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : "";
}

/** Für input type=datetime-local: lokale Zeit ohne Zeitzone. */
function toLocalInput(value) {
    if (!value) return "";
    const date = new Date(value);
    date.setMinutes(date.getMinutes() - date.getTimezoneOffset());
    return date.toISOString().slice(0, 16);
}

function can(permission) {
    return state.meta?.me.permissions.includes(permission) ?? false;
}

function canAny(...permissions) {
    return permissions.some(can);
}

function permissionLabel(name) {
    return state.meta?.permissions.find((p) => p.name === name)?.label ?? name;
}

function levelPermissions(level) {
    return state.meta?.levels.find((l) => l.name === level)?.permissions ?? [];
}

const levelLabels = {
    Tester: "Tester – Entwicklermodus, nur freigegebene Dev-Versionen",
    Entwickler: "Entwickler – Entwicklermodus, alle Dev-Versionen",
    Administrator: "Administrator – darf alles"
};

function badge(text, kind = "") {
    return el("span", { class: `badge ${kind}`, text });
}

// ==== Dialog ====

/**
 * Öffnet einen Dialog. `actions` sind Knöpfe; gibt ein `run` true zurück oder
 * wirft nichts, schliesst sich der Dialog. Fehler erscheinen im Dialog.
 */
function openDialog(title, body, actions) {
    const dialog = $("dialog");
    $("dialog-title").textContent = title;
    $("dialog-body").replaceChildren(...[body].flat());
    $("dialog-error").textContent = "";

    const buttons = actions.map((action) => {
        const button = el("button", {
            type: action.submit ? "submit" : "button",
            class: action.kind ?? "",
            text: action.label
        });

        button.addEventListener("click", async (event) => {
            event.preventDefault();

            if (action.submit && !$("dialog-form").reportValidity()) return;

            if (!action.run) {
                dialog.close();
                return;
            }

            for (const b of buttons) b.disabled = true;
            $("dialog-error").textContent = "";

            try {
                const keepOpen = await action.run();
                if (keepOpen !== true) dialog.close();
            } catch (error) {
                $("dialog-error").textContent = error.message;
            } finally {
                for (const b of buttons) b.disabled = false;
            }
        });

        return button;
    });

    $("dialog-actions").replaceChildren(...buttons);
    dialog.showModal();

    const first = $("dialog-body").querySelector("input:not([type=checkbox]), textarea, select");
    first?.focus();
}

function closeDialog() {
    $("dialog").close();
}

function confirmDialog(title, text, label, kind = "danger") {
    return new Promise((resolve) => {
        const dialog = $("dialog");

        // Das close-Ereignis kommt verzögert: Schloss sich kurz vorher ein
        // anderer Dialog, trifft seines erst ein, wenn dieser schon offen ist.
        const onClose = () => {
            if (dialog.open) return;
            dialog.removeEventListener("close", onClose);
            resolve(false);
        };

        openDialog(title, el("p", { class: "muted", text }), [
            { label: "Abbrechen" },
            {
                label, kind, submit: true,
                run: async () => {
                    dialog.removeEventListener("close", onClose);
                    resolve(true);
                }
            }
        ]);

        dialog.addEventListener("close", onClose);
    });
}

function showSecret(title, text, secret) {
    const code = el("code", { class: "mono", text: secret });
    const copy = el("button", {
        type: "button",
        text: "Kopieren",
        onclick: async () => {
            try {
                await navigator.clipboard.writeText(secret);
                copy.textContent = "Kopiert";
            } catch {
                copy.textContent = "Bitte markieren und kopieren";
            }
        }
    });

    openDialog(title, [
        el("p", { class: "muted", text }),
        el("div", { class: "secret" }, code, copy),
        el("p", { class: "notice warn", text: "Dieses Passwort wird nur jetzt angezeigt." })
    ], [{ label: "Fertig", kind: "primary", submit: true }]);
}

// ==== Anmelden ====

const viewTitles = {
    "login-view": "Anmelden · School Manager",
    "password-view": "Neues Passwort · School Manager",
    "app-view": "Admin-Panel · School Manager"
};

function showOnly(id) {
    for (const view of Object.keys(viewTitles)) $(view).hidden = view !== id;
    document.title = viewTitles[id];
}

function signedOut(message) {
    state.token = null;
    state.meta = null;
    sessionStorage.removeItem("sm-token");
    if ($("dialog").open) closeDialog();
    showOnly("login-view");
    $("login-error").textContent = message ?? "";
    $("login-password").value = "";
    $("login-user").focus();
}

$("login-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const button = event.submitter;
    button.disabled = true;
    $("login-error").textContent = "";

    try {
        const result = await api("POST", "/api/auth/login", {
            userName: $("login-user").value,
            password: $("login-password").value,
            client: "panel"
        });

        state.token = result.token;
        sessionStorage.setItem("sm-token", result.token);
        $("login-password").value = "";
        await start();
    } catch (error) {
        $("login-error").textContent = error.message;
    } finally {
        button.disabled = false;
    }
});

$("forced-password-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    $("forced-error").textContent = "";

    if ($("forced-new").value !== $("forced-repeat").value) {
        $("forced-error").textContent = "Die beiden neuen Passwörter stimmen nicht überein.";
        return;
    }

    try {
        await api("POST", "/api/me/password", {
            currentPassword: $("forced-current").value,
            newPassword: $("forced-new").value
        });

        for (const id of ["forced-current", "forced-new", "forced-repeat"]) $(id).value = "";
        toast("Das neue Passwort gilt.", "success");
        await start();
    } catch (error) {
        $("forced-error").textContent = error.message;
    }
});

async function logout() {
    try { await api("POST", "/api/auth/logout"); } catch { /* ohnehin weg */ }
    signedOut();
}

$("logout").addEventListener("click", logout);
$("forced-logout").addEventListener("click", logout);

async function start() {
    if (!state.token) {
        signedOut();
        return;
    }

    try {
        state.meta = await api("GET", "/api/admin/meta");
    } catch (error) {
        if (error.status !== 401) signedOut(error.message);
        return;
    }

    if (state.meta.me.mustChangePassword) {
        showOnly("password-view");
        $("forced-current").focus();
        return;
    }

    showOnly("app-view");
    const me = state.meta.me;
    $("whoami").textContent = `${me.displayName || me.userName} · ${me.level}`;
    buildNav();
}

// ==== Navigation ====

const views = [
    { id: "messages", label: "Meldungen", visible: () => can("Messages"), render: renderMessages },
    { id: "releases", label: "Versionen", visible: () => canAny("UpdateNotes", "ApproveUpdates", "Release"), render: renderReleases },
    { id: "users", label: "Benutzer", visible: () => can("Users"), render: renderUsers },
    { id: "groups", label: "Gruppen", visible: () => can("Groups"), render: renderGroups },
    { id: "audit", label: "Verlauf", visible: () => can("Audit"), render: renderAudit },
    { id: "settings", label: "Einstellungen", visible: () => can("Settings"), render: renderSettings },
    { id: "account", label: "Mein Konto", visible: () => true, render: renderAccount }
];

function buildNav() {
    const available = views.filter((view) => view.visible());
    const wanted = location.hash.slice(1);

    $("nav").replaceChildren(...available.map((view) =>
        el("button", { type: "button", "data-view": view.id, text: view.label, onclick: () => show(view.id) })));

    show(available.some((v) => v.id === wanted) ? wanted : available[0].id);
}

async function show(id) {
    const view = views.find((v) => v.id === id && v.visible());
    if (!view) return;

    state.view = id;
    history.replaceState(null, "", `#${id}`);

    for (const button of $("nav").children) {
        if (button.dataset.view === id) button.setAttribute("aria-current", "page");
        else button.removeAttribute("aria-current");
    }

    const content = $("content");
    content.replaceChildren(el("p", { class: "faint", text: "Wird geladen …" }));

    try {
        const nodes = await view.render();
        if (state.view === id) content.replaceChildren(...[nodes].flat());
    } catch (error) {
        if (state.view === id) content.replaceChildren(el("div", { class: "notice error", text: error.message }));
    }
}

function refresh() {
    return show(state.view);
}

function pageHead(title, subtitle, ...actions) {
    return el("div", { class: "page-head" },
        el("div", {}, el("h1", { text: title }), subtitle ? el("p", { class: "muted", text: subtitle }) : null),
        actions.length ? el("div", { class: "actions" }, actions) : null);
}

// ==== Meldungen ====

const kindLabels = { Error: "Fehler (rot)", Info: "Hinweis (grau)" };
const audienceLabels = { Everyone: "Alle", Developers: "Nur angemeldete Entwickler" };

const placementLabels = {
    App: ["In der App", "Oben im Fenster von School Manager"],
    StartPage: ["Auf der Startseite", "Oben auf der Webseite mit Funktionen und Download"],
    SignInPage: ["Auf der Anmeldeseite", "Oben auf der Seite, auf der man sich hier anmeldet"]
};

/** Wo eine Meldung erscheint, in Worten - Meldungen von früher stehen in der App. */
function describePlacements(message) {
    const placements = message.placements ?? ["App"];
    return placements.map((p) => (p === "App" ? `App (${audienceLabels[message.audience]})` : placementLabels[p][0].replace(/^Auf der /, ""))).join(" · ");
}

async function renderMessages() {
    const messages = await api("GET", "/api/admin/messages");

    const list = messages.length === 0
        ? el("div", { class: "card empty", text: "Noch keine Meldung. Was hier aktiv ist, erscheint oben in School Manager." })
        : el("div", { class: "stack" }, messages.map((message) => {
            const expired = message.expiresAt && new Date(message.expiresAt) < new Date();
            const shown = message.isActive && !expired;

            return el("div", { class: "card item" },
                el("div", { class: `banner ${message.kind === "Error" ? "error" : ""} ${shown ? "" : "off"}`, text: message.text }),
                el("div", { class: "item-head" },
                    el("div", { class: "meta" },
                        shown ? badge("wird angezeigt", "success") : badge(expired ? "abgelaufen" : "ausgeschaltet"),
                        el("span", { text: describePlacements(message) }),
                        message.expiresAt ? el("span", { text: `bis ${formatDate(message.expiresAt)}` }) : null,
                        el("span", { text: `geändert ${formatDate(message.updatedAt)} von ${message.updatedBy}` })),
                    el("div", { class: "actions" },
                        el("button", { type: "button", text: "Bearbeiten", onclick: () => editMessage(message) }),
                        el("button", {
                            type: "button",
                            text: message.isActive ? "Ausschalten" : "Einschalten",
                            onclick: async () => {
                                try {
                                    await api("PUT", `/api/admin/messages/${message.id}`, { ...message, isActive: !message.isActive });
                                    await refresh();
                                } catch (error) { toast(error.message, "error"); }
                            }
                        }),
                        el("button", {
                            type: "button", class: "danger", text: "Löschen",
                            onclick: async () => {
                                if (!await confirmDialog("Meldung löschen", `„${message.text.slice(0, 120)}“ endgültig löschen?`, "Löschen")) return;
                                try {
                                    await api("DELETE", `/api/admin/messages/${message.id}`);
                                    toast("Meldung gelöscht.");
                                    await refresh();
                                } catch (error) { toast(error.message, "error"); }
                            }
                        }))));
        }));

    return [
        pageHead("Meldungen", "Streifen oben in School Manager, auf der Startseite oder auf der Anmeldeseite. Die App holt sie beim Start und alle 10 Minuten.",
            el("button", { type: "button", class: "primary", text: "Neue Meldung", onclick: () => editMessage(null) })),
        list
    ];
}

function editMessage(message) {
    const text = el("textarea", { maxLength: 1000, required: true, rows: 4 });
    text.value = message?.text ?? "";

    const kind = el("select", {}, Object.entries(kindLabels).map(([value, label]) => el("option", { value, text: label })));
    kind.value = message?.kind ?? "Error";

    const audience = el("select", {}, Object.entries(audienceLabels).map(([value, label]) => el("option", { value, text: label })));
    audience.value = message?.audience ?? "Everyone";

    const active = el("input", { type: "checkbox", checked: message?.isActive ?? true });
    const expires = el("input", { type: "datetime-local", value: toLocalInput(message?.expiresAt) });

    const chosen = message?.placements ?? ["App"];
    const placementBoxes = Object.entries(placementLabels).map(([name, [label, hint]]) => ({
        name,
        box: el("input", { type: "checkbox", checked: chosen.includes(name) }),
        label,
        hint
    }));

    const audienceLabel = el("label", {}, "Wer sieht sie in der App", audience);

    const counter = el("div", { class: "counter" });
    const preview = el("div", { class: "banner" });

    const update = () => {
        counter.textContent = `${text.value.length} / 1000`;
        preview.textContent = text.value || "Vorschau";
        preview.className = `banner ${kind.value === "Error" ? "error" : ""}`;

        // Auf den Webseiten sieht jeder die Meldung; die Wahl gilt nur für die App.
        audienceLabel.hidden = !placementBoxes.find((p) => p.name === "App").box.checked;
    };

    text.addEventListener("input", update);
    kind.addEventListener("change", update);
    for (const p of placementBoxes) p.box.addEventListener("change", update);
    update();

    openDialog(message ? "Meldung bearbeiten" : "Neue Meldung", [
        el("label", {}, "Text", text, counter),
        el("fieldset", {}, el("legend", { text: "Wo erscheint sie" }),
            el("div", { class: "stack" }, placementBoxes.map((p) =>
                el("label", { class: "check" }, p.box, el("span", {}, p.label, el("small", { text: p.hint })))))),
        el("div", { class: "two" }, el("label", {}, "Art", kind), audienceLabel),
        el("div", { class: "two" },
            el("label", {}, "Läuft ab (leer = bis zum Ausschalten)", expires),
            el("label", { class: "check" }, active, el("span", {}, "Aktiv", el("small", { text: "Nur aktive Meldungen erscheinen." })))),
        el("p", { class: "faint small-text", text: "Auf der Startseite und der Anmeldeseite sieht jeder die Meldung, auch ohne Anmeldung." }),
        el("div", { class: "stack" }, el("span", { class: "muted small-text", text: "So sieht der Streifen aus:" }), preview)
    ], [
        { label: "Abbrechen" },
        {
            label: "Speichern", kind: "primary", submit: true,
            run: async () => {
                const body = {
                    text: text.value,
                    kind: kind.value,
                    audience: audience.value,
                    isActive: active.checked,
                    expiresAt: expires.value ? new Date(expires.value).toISOString() : null,
                    placements: placementBoxes.filter((p) => p.box.checked).map((p) => p.name)
                };

                if (message) await api("PUT", `/api/admin/messages/${message.id}`, body);
                else await api("POST", "/api/admin/messages", body);

                toast("Meldung gespeichert.", "success");
                await refresh();
            }
        }
    ]);
}

// ==== Versionen ====

async function renderReleases() {
    const [data, directory] = await Promise.all([
        api("GET", "/api/admin/releases"),
        can("ApproveUpdates") ? api("GET", "/api/admin/directory") : Promise.resolve({ users: [], groups: [] })
    ]);

    const nodes = [
        pageHead("Versionen",
            `Öffentliche Releases aus ${data.publicRepo}, Dev-Versionen aus ${data.devRepo} (Zweig ${data.devBranch}).`,
            el("button", { type: "button", text: "Neu laden", onclick: refresh }),
            can("Release") ? el("button", { type: "button", text: "Dev-Version veröffentlichen", onclick: () => publish("dev") }) : null,
            can("Release") ? el("button", { type: "button", class: "primary", text: "Release veröffentlichen", onclick: () => publish("public") }) : null)
    ];

    if (!data.hasToken)
        nodes.push(el("div", { class: "notice warn", text:
            "Auf dem Server ist kein GitHub-Token hinterlegt: Dev-Versionen sind nicht zu sehen, und Veröffentlichen geht nicht. Siehe README des Servers (GitHub__Token)." }));

    if (data.building.length) {
        nodes.push(el("h2", { class: "section-title", text: "Bauläufe ohne Release" }));
        nodes.push(el("div", { class: "stack" }, data.building.map((run) =>
            el("div", { class: "card item-head" },
                el("div", { class: "item-title" }, el("strong", { text: run.tag }), el("span", { class: "faint", text: run.repo }), buildBadge(run)),
                el("a", { href: run.url, target: "_blank", rel: "noopener noreferrer", text: "Auf GitHub ansehen" })))));
    }

    nodes.push(el("h2", { class: "section-title", text: "Veröffentlicht" }));

    if (data.releases.length === 0) {
        nodes.push(el("div", { class: "card empty", text: "Keine Versionen gefunden." }));
        return nodes;
    }

    nodes.push(el("div", { class: "stack" }, data.releases.map((release) => releaseCard(release, directory))));
    return nodes;
}

function buildBadge(build) {
    if (!build) return null;
    if (build.status !== "completed") return badge("wird gebaut", "warn");
    if (build.conclusion === "success") return badge("Bau grün", "success");
    return badge(`Bau ${build.conclusion ?? "fehlgeschlagen"}`, "error");
}

function releaseCard(release, directory) {
    const kindBadge = release.isLegacyDev
        ? badge("alte Vorabversion, öffentlich", "warn")
        : release.isDev ? badge("Dev", "info") : badge("öffentlich", "success");

    const files = release.hasInstaller && release.hasExe
        ? badge(`beide Dateien · ${formatSize(release.size)}`, "plain")
        : badge("Dateien fehlen", "error");

    const note = el("div", { class: "stack" },
        el("span", { class: "muted small-text", text: "Update-Info in der App" }),
        release.note
            ? el("div", { class: "banner", text: release.note })
            : el("span", { class: "faint small-text", text: "Keine – die App zeigt nur „Version … steht bereit“." }));

    const parts = [
        el("div", { class: "item-head" },
            el("div", { class: "item-title" },
                el("h2", { text: release.tag }), kindBadge, files, buildBadge(release.build)),
            el("div", { class: "actions" },
                can("UpdateNotes") ? el("button", { type: "button", text: "Update-Info", onclick: () => editNote(release) }) : null,
                release.isDev && !release.isLegacyDev && can("ApproveUpdates")
                    ? el("button", { type: "button", text: "Freigabe", onclick: () => editApproval(release, directory) }) : null,
                !release.isDev && !release.isLegacyDev && can("Release")
                    ? el("button", { type: "button", text: "Dev-Versionen aufräumen", onclick: () => cleanup(release) }) : null)),
        el("div", { class: "meta" },
            el("span", { text: formatDate(release.publishedAt) }),
            el("span", { text: release.repo }),
            el("a", { href: release.htmlUrl, target: "_blank", rel: "noopener noreferrer", text: "GitHub" })),
        note
    ];

    if (release.isDev && !release.isLegacyDev)
        parts.push(el("div", { class: "row" }, el("span", { class: "muted small-text", text: "Freigegeben für:" }), approvalSummary(release.approval, directory)));

    return el("div", { class: "card item" }, parts);
}

function approvalSummary(approval, directory) {
    if (!approval) return el("span", { class: "faint small-text", text: "nur Konten mit „Alle Dev-Versionen“" });
    if (approval.forAllDevelopers) return badge("alle Entwickler", "success");

    const names = [
        ...approval.userIds.map((id) => directory.users.find((u) => u.id === id)?.userName ?? `#${id}`),
        ...approval.groupIds.map((id) => `Gruppe ${directory.groups.find((g) => g.id === id)?.name ?? id}`)
    ];

    return el("span", { class: "chips" }, names.map((name) => badge(name, "plain")));
}

function editNote(release) {
    const text = el("textarea", { maxLength: 1000, rows: 4 });
    text.value = release.note;
    const counter = el("div", { class: "counter" });
    const preview = el("div", { class: "banner" });

    const update = () => {
        counter.textContent = `${text.value.length} / 1000`;
        preview.textContent = `Version ${release.version} steht bereit.${text.value ? " " + text.value : ""}`;
    };

    text.addEventListener("input", update);
    update();

    openDialog(`Update-Info zu ${release.tag}`, [
        el("p", { class: "muted", text: "Ein kurzer Satz, was diese Version bringt. Die App zeigt ihn im Hinweis auf das Update und in den Einstellungen. Leer lassen entfernt ihn." }),
        el("label", {}, "Text", text, counter),
        el("div", { class: "stack" }, el("span", { class: "muted small-text", text: "Vorschau:" }), preview)
    ], [
        { label: "Abbrechen" },
        {
            label: "Speichern", kind: "primary", submit: true,
            run: async () => {
                await api("PUT", `/api/admin/releases/${encodeURIComponent(release.tag)}/note`, { text: text.value });
                toast("Update-Info gespeichert.", "success");
                await refresh();
            }
        }
    ]);
}

function editApproval(release, directory) {
    const approval = release.approval ?? { forAllDevelopers: false, userIds: [], groupIds: [] };
    const all = el("input", { type: "checkbox", checked: approval.forAllDevelopers });

    const userBoxes = directory.users.map((user) => ({
        id: user.id,
        box: el("input", { type: "checkbox", checked: approval.userIds.includes(user.id) }),
        label: user.displayName ? `${user.displayName} (${user.userName})` : user.userName
    }));

    const groupBoxes = directory.groups.map((group) => ({
        id: group.id,
        box: el("input", { type: "checkbox", checked: approval.groupIds.includes(group.id) }),
        label: group.name
    }));

    const detail = el("div", { class: "stack" },
        el("fieldset", {}, el("legend", { text: "Gruppen" }),
            groupBoxes.length
                ? el("div", { class: "check-grid" }, groupBoxes.map((g) => el("label", { class: "check" }, g.box, g.label)))
                : el("span", { class: "faint small-text", text: "Noch keine Gruppen." })),
        el("fieldset", {}, el("legend", { text: "Einzelne Benutzer" }),
            el("div", { class: "check-grid" }, userBoxes.map((u) => el("label", { class: "check" }, u.box, u.label)))));

    const sync = () => (detail.hidden = all.checked);
    all.addEventListener("change", sync);
    sync();

    openDialog(`Freigabe von ${release.tag}`, [
        el("p", { class: "muted", text: "Wer diese Dev-Version beziehen darf. Konten mit dem Recht „Alle Dev-Versionen“ (Stufe Entwickler und Administrator) bekommen sie ohnehin, Konten mit „nur öffentliche Versionen“ nie." }),
        el("label", { class: "check" }, all, el("span", {}, "Für alle Entwickler freigeben", el("small", { text: "Jedes Konto, das den Entwicklermodus öffnen darf." }))),
        detail
    ], [
        { label: "Abbrechen" },
        {
            label: "Speichern", kind: "primary", submit: true,
            run: async () => {
                await api("PUT", `/api/admin/releases/${encodeURIComponent(release.tag)}/approval`, {
                    forAllDevelopers: all.checked,
                    userIds: all.checked ? [] : userBoxes.filter((u) => u.box.checked).map((u) => u.id),
                    groupIds: all.checked ? [] : groupBoxes.filter((g) => g.box.checked).map((g) => g.id)
                });
                toast("Freigabe gespeichert.", "success");
                await refresh();
            }
        }
    ]);
}

async function publish(channel) {
    let prepare;

    try {
        prepare = await api("GET", "/api/admin/releases/prepare");
    } catch (error) {
        toast(error.message, "error");
        return;
    }

    const dev = channel === "dev";
    const target = dev ? prepare.dev : prepare.public;
    const version = el("input", { required: true, pattern: "\\d+\\.\\d+\\.\\d+", value: target.suggestedVersion ?? "", class: "mono" });
    const approve = el("input", { type: "checkbox" });

    const body = [
        el("p", { class: "muted", text: dev
            ? `Setzt den Tag vX.Y.Z-dev auf den neuesten Stand von ${target.branch} im privaten Repository ${target.repo}. Der Workflow dort baut und veröffentlicht die Dev-Version – öffentlich ist sie nicht zu sehen.`
            : `Setzt den Tag vX.Y.Z auf den neuesten Stand von ${target.branch} im öffentlichen Repository ${target.repo}. Der Workflow baut und veröffentlicht – danach bekommt jeder die Version über die Update-Suche.` })
    ];

    if (!prepare.hasToken)
        body.push(el("div", { class: "notice error", text: "Kein GitHub-Token auf dem Server – Veröffentlichen geht nicht." }));

    if (target.error) {
        body.push(el("div", { class: "notice error", text: target.error }));
    } else {
        body.push(el("div", { class: "notice" },
            el("div", {}, el("strong", { text: `${target.repo} · ${target.branch}` })),
            el("div", { class: "mono small-text", text: `${target.commit.sha.slice(0, 7)} ${target.commit.message}` }),
            el("div", { class: "faint small-text", text: `${formatDate(target.commit.date)} · Version in der Projektdatei: ${target.projectVersion ?? "nicht gefunden"}` })));
    }

    body.push(el("div", { class: "meta" },
        el("span", { text: `Neuestes Release: ${prepare.newestPublic ?? "–"}` }),
        el("span", { text: `Neueste Dev-Version: ${prepare.newestDev ?? "–"}` })));

    body.push(el("label", {}, dev ? "Versionsnummer (ohne -dev)" : "Versionsnummer", version));

    if (dev && can("ApproveUpdates"))
        body.push(el("label", { class: "check" }, approve, el("span", {}, "Gleich für alle Entwickler freigeben", el("small", { text: "Sonst bekommen sie nur Konten mit „Alle Dev-Versionen“, bis eine Freigabe gesetzt ist." }))));

    if (dev)
        body.push(el("p", { class: "faint small-text", text: "Vorher gehört der Changelog-Eintrag dieser Version (Logging/Changelog.cs) auf den Zweig." }));
    else
        body.push(el("div", { class: "notice warn", text: `Der Code muss vorher auf ${target.branch} im öffentlichen Repository liegen – das Panel verschiebt keinen Code. Danach „Dev-Versionen aufräumen“, sobald der Bau grün ist.` }));

    const send = async (confirmVersionMismatch) => {
        try {
            const result = await api("POST", "/api/admin/releases", {
                channel,
                version: version.value.trim(),
                confirmVersionMismatch,
                approveForAllDevelopers: approve.checked
            });

            closeDialog();
            toast(`${result.tag} ist getaggt – der Bau läuft.`, "success");
            window.open(result.actionsUrl, "_blank", "noopener");
            await refresh();
        } catch (error) {
            if (error.status === 409 && error.body?.code === "version-mismatch") {
                closeDialog();
                if (await confirmDialog("Versionsnummer weicht ab", `${error.message} Trotzdem veröffentlichen?`, "Trotzdem veröffentlichen", "primary"))
                    await send(true);
                return;
            }

            throw error;
        }
    };

    openDialog(dev ? "Dev-Version veröffentlichen" : "Öffentliches Release veröffentlichen", body, [
        { label: "Abbrechen" },
        {
            label: dev ? "Dev-Version taggen" : "Release taggen", kind: "primary", submit: true,
            run: async () => {
                const tag = `v${version.value.trim()}${dev ? "-dev" : ""}`;
                closeDialog();

                if (!await confirmDialog("Wirklich veröffentlichen?",
                    `${tag} wird jetzt auf ${target.repo}/${target.branch} getaggt, und GitHub baut die Version. Ein Tag lässt sich nicht einfach zurücknehmen.`,
                    "Veröffentlichen", "primary"))
                    return;

                try {
                    await send(false);
                } catch (error) {
                    toast(error.message, "error");
                }
            }
        }
    ]);
}

async function cleanup(release) {
    let preview;

    try {
        preview = await api("GET", `/api/admin/releases/${encodeURIComponent(release.tag)}/cleanup`);
    } catch (error) {
        toast(error.message, "error");
        return;
    }

    const body = [
        el("p", { class: "muted", text: `Löscht die Dev-Versionen, die in ${release.tag} aufgegangen sind – jeweils Release und Tag bei GitHub. Freigaben und Update-Infos dazu fallen mit weg.` })
    ];

    if (preview.problem) body.push(el("div", { class: "notice warn", text: preview.problem }));

    body.push(preview.candidates.length
        ? el("div", { class: "chips" }, preview.candidates.map((c) => badge(`${c.tag} · ${c.repo}`, "plain")))
        : el("p", { class: "faint", text: "Es gibt nichts aufzuräumen." }));

    body.push(el("p", { class: "faint small-text", text: "Lokale Tags auf dem eigenen Rechner löscht das nicht: git tag -d …, oder git fetch --prune --prune-tags." }));

    openDialog(`Aufräumen nach ${release.tag}`, body, [
        { label: "Abbrechen" },
        preview.ready && preview.candidates.length ? {
            label: `${preview.candidates.length} löschen`, kind: "danger", submit: true,
            run: async () => {
                const result = await api("POST", `/api/admin/releases/${encodeURIComponent(release.tag)}/cleanup`);
                toast(`Gelöscht: ${result.deleted.join(", ") || "nichts"}`, "success");
                await refresh();
            }
        } : null
    ].filter(Boolean));
}

// ==== Benutzer ====

async function renderUsers() {
    const [users, groups] = await Promise.all([
        api("GET", "/api/admin/users"),
        api("GET", "/api/admin/directory").then((d) => d.groups)
    ]);

    const groupName = (id) => groups.find((g) => g.id === id)?.name ?? `#${id}`;
    const now = new Date();

    const rows = users.map((user) => {
        const self = user.id === state.meta.me.id;
        const locked = user.lockedUntil && new Date(user.lockedUntil) > now;

        return el("tr", {},
            el("td", {},
                el("div", {}, el("strong", { text: user.displayName || user.userName }), self ? el("span", { class: "faint", text: " (du)" }) : null),
                el("div", { class: "faint small-text mono", text: user.userName })),
            el("td", { class: "nowrap", text: user.level }),
            el("td", {}, el("div", { class: "chips" }, user.groupIds.map((id) => badge(groupName(id), "plain")))),
            el("td", {}, el("div", { class: "chips" },
                user.isActive ? badge("aktiv", "success") : badge("gesperrt", "error"),
                locked ? badge(`Fehlversuche bis ${formatDate(user.lockedUntil)}`, "warn") : null,
                user.mustChangePassword ? badge("muss Passwort ändern", "warn") : null,
                user.publicOnly ? badge("nur öffentliche Versionen", "plain") : null,
                user.appSessions ? badge(`in ${user.appSessions} App${user.appSessions > 1 ? "s" : ""} angemeldet`, "info") : null)),
            el("td", { class: "nowrap faint", text: formatDate(user.lastLoginAt) }),
            el("td", {}, el("div", { class: "actions" },
                el("button", { type: "button", text: "Bearbeiten", onclick: () => editUser(user, groups) }),
                el("button", { type: "button", text: "Passwort", onclick: () => resetPassword(user) }),
                locked ? el("button", { type: "button", text: "Entsperren", onclick: () => simpleAction(`/api/admin/users/${user.id}/unlock`, "Sperre aufgehoben.") }) : null,
                user.appSessions + user.panelSessions > (self ? 1 : 0)
                    ? el("button", { type: "button", text: "Abmelden", onclick: () => signOutUser(user) }) : null,
                self ? null : el("button", { type: "button", class: "danger", text: "Löschen", onclick: () => deleteUser(user) }))));
    });

    return [
        pageHead("Benutzer", "Wer sich im Entwicklermodus von School Manager und hier im Panel anmelden darf.",
            el("button", { type: "button", class: "primary", text: "Neuer Benutzer", onclick: () => editUser(null, groups) })),
        el("div", { class: "table-wrap" },
            el("table", {},
                el("thead", {}, el("tr", {}, ["Benutzer", "Stufe", "Gruppen", "Status", "Zuletzt angemeldet", ""].map((h) => el("th", { text: h })))),
                el("tbody", {}, rows)))
    ];
}

async function simpleAction(path, message) {
    try {
        await api("POST", path);
        toast(message, "success");
        await refresh();
    } catch (error) {
        toast(error.message, "error");
    }
}

async function signOutUser(user) {
    if (!await confirmDialog("Überall abmelden", `${user.userName} wird in School Manager und im Panel abgemeldet und muss sich neu anmelden.`, "Abmelden", "primary")) return;
    await simpleAction(`/api/admin/users/${user.id}/signout`, `${user.userName} ist abgemeldet.`);
}

async function deleteUser(user) {
    if (!await confirmDialog("Benutzer löschen", `${user.userName} endgültig löschen? Die Anmeldungen enden sofort. Nur sperren geht über „Bearbeiten“.`, "Löschen")) return;

    try {
        await api("DELETE", `/api/admin/users/${user.id}`);
        toast("Benutzer gelöscht.");
        await refresh();
    } catch (error) {
        toast(error.message, "error");
    }
}

function editUser(user, groups) {
    const isNew = !user;
    const self = user?.id === state.meta.me.id;

    const userName = el("input", { required: true, maxLength: 32, pattern: "[a-z0-9._\\-]{3,32}", autocomplete: "off", autocapitalize: "none", spellcheck: false, class: "mono" });
    const displayName = el("input", { maxLength: 100, value: user?.displayName ?? "" });

    const level = el("select", { disabled: self },
        Object.entries(levelLabels)
            .filter(([value]) => value !== "Administrator" || state.meta.me.level === "Administrator" || user?.level === "Administrator")
            .map(([value, label]) => el("option", { value, text: label })));
    level.value = user?.level ?? "Tester";

    const groupBoxes = groups.map((group) => ({
        id: group.id,
        box: el("input", { type: "checkbox", checked: user?.groupIds.includes(group.id) ?? false }),
        name: group.name
    }));

    const permissionBoxes = state.meta.permissions.map((permission) => {
        const box = el("input", { type: "checkbox", checked: user?.extraPermissions.includes(permission.name) ?? false });
        const note = el("small");
        const label = el("label", { class: "check" }, box, el("span", {}, permission.label, note));
        return { name: permission.name, hint: permission.hint, box, note, label };
    });

    const syncPermissions = () => {
        const fromLevel = levelPermissions(level.value);

        for (const p of permissionBoxes) {
            const byLevel = fromLevel.includes(p.name);
            p.box.disabled = byLevel;
            p.label.classList.toggle("disabled", byLevel);
            if (byLevel) p.box.checked = true;
            else if (p.box.dataset.levelChecked) p.box.checked = false;
            p.box.dataset.levelChecked = byLevel ? "1" : "";
            p.note.textContent = byLevel ? `${p.hint} – kommt mit der Stufe` : p.hint;
        }
    };

    level.addEventListener("change", syncPermissions);
    syncPermissions();

    const publicOnly = el("input", { type: "checkbox", checked: user?.publicOnly ?? false });
    const active = el("input", { type: "checkbox", checked: user?.isActive ?? true, disabled: self });

    const body = [];

    if (isNew) body.push(el("div", { class: "two" }, el("label", {}, "Benutzername", userName), el("label", {}, "Anzeigename", displayName)));
    else body.push(el("label", {}, "Anzeigename", displayName));

    body.push(el("label", {}, "Stufe", level));

    body.push(el("fieldset", {}, el("legend", { text: "Gruppen" }),
        groupBoxes.length
            ? el("div", { class: "check-grid" }, groupBoxes.map((g) => el("label", { class: "check" }, g.box, g.name)))
            : el("span", { class: "faint small-text", text: "Noch keine Gruppen – anlegen unter „Gruppen“." })));

    body.push(el("fieldset", {}, el("legend", { text: "Zusätzliche Rechte (dazu kommen die der Gruppen)" }),
        el("div", { class: "stack" }, permissionBoxes.map((p) => p.label))));

    body.push(el("fieldset", {}, el("legend", { text: "Updates und Zugang" }),
        el("label", { class: "check" }, publicOnly, el("span", {}, "Nur öffentliche Versionen", el("small", { text: "Bekommt nie eine Dev-Version, egal welche Rechte oder Freigaben." }))),
        el("label", { class: "check" }, active, el("span", {}, "Konto aktiv", el("small", { text: self ? "Das eigene Konto lässt sich nicht sperren." : "Gesperrt heisst: sofort überall abgemeldet, keine Anmeldung mehr." })))));

    let password, generate, mustChange;

    if (isNew) {
        password = el("input", { type: "password", autocomplete: "new-password", minLength: state.meta.minimumPasswordLength, maxLength: 200 });
        generate = el("input", { type: "checkbox", checked: true });
        mustChange = el("input", { type: "checkbox", checked: true });

        const syncPassword = () => {
            password.disabled = generate.checked;
            password.required = !generate.checked;
        };

        generate.addEventListener("change", syncPassword);
        syncPassword();

        body.push(el("fieldset", {}, el("legend", { text: "Passwort" }),
            el("label", { class: "check" }, generate, el("span", {}, "Sicheres Passwort erzeugen", el("small", { text: "Wird nach dem Speichern einmal angezeigt." }))),
            el("label", {}, `Oder selbst festlegen (mindestens ${state.meta.minimumPasswordLength} Zeichen)`, password),
            el("label", { class: "check" }, mustChange, el("span", {}, "Beim ersten Anmelden ändern", el("small", { text: "Das erste Mal im Panel anmelden und ein eigenes Passwort setzen – erst danach geht die Anmeldung in School Manager." })))));
    }

    openDialog(isNew ? "Neuer Benutzer" : `${user.userName} bearbeiten`, body, [
        { label: "Abbrechen" },
        {
            label: "Speichern", kind: "primary", submit: true,
            run: async () => {
                const payload = {
                    userName: isNew ? userName.value : user.userName,
                    displayName: displayName.value,
                    level: level.value,
                    extraPermissions: permissionBoxes.filter((p) => p.box.checked && !p.box.disabled).map((p) => p.name),
                    groupIds: groupBoxes.filter((g) => g.box.checked).map((g) => g.id),
                    publicOnly: publicOnly.checked,
                    isActive: active.checked,
                    password: isNew ? password.value : null,
                    generatePassword: isNew ? generate.checked : false,
                    mustChangePassword: isNew ? mustChange.checked : false
                };

                if (isNew) {
                    const result = await api("POST", "/api/admin/users", payload);
                    await refresh();

                    if (result.generatedPassword) {
                        showSecret(`${payload.userName} ist angelegt`, "Das Passwort für die erste Anmeldung:", result.generatedPassword);
                        return true;
                    }

                    toast("Benutzer angelegt.", "success");
                } else {
                    await api("PUT", `/api/admin/users/${user.id}`, payload);
                    toast("Gespeichert.", "success");
                    await refresh();
                }
            }
        }
    ]);
}

function resetPassword(user) {
    const generate = el("input", { type: "checkbox", checked: true });
    const password = el("input", { type: "password", autocomplete: "new-password", minLength: state.meta.minimumPasswordLength, maxLength: 200 });
    const mustChange = el("input", { type: "checkbox", checked: true });

    const sync = () => {
        password.disabled = generate.checked;
        password.required = !generate.checked;
    };

    generate.addEventListener("change", sync);
    sync();

    openDialog(`Passwort von ${user.userName}`, [
        el("p", { class: "muted", text: "Das neue Passwort gilt sofort; alle Anmeldungen mit dem alten enden." }),
        el("label", { class: "check" }, generate, el("span", {}, "Sicheres Passwort erzeugen")),
        el("label", {}, "Oder selbst festlegen", password),
        el("label", { class: "check" }, mustChange, el("span", {}, "Beim nächsten Anmelden ändern"))
    ], [
        { label: "Abbrechen" },
        {
            label: "Passwort setzen", kind: "primary", submit: true,
            run: async () => {
                const result = await api("POST", `/api/admin/users/${user.id}/password`, {
                    password: password.value,
                    generatePassword: generate.checked,
                    mustChangePassword: mustChange.checked
                });

                await refresh();

                if (result.generatedPassword) {
                    showSecret("Neues Passwort", `Das neue Passwort für ${user.userName}:`, result.generatedPassword);
                    return true;
                }

                toast("Passwort gesetzt.", "success");
            }
        }
    ]);
}

// ==== Gruppen ====

async function renderGroups() {
    const groups = await api("GET", "/api/admin/groups");

    const list = groups.length === 0
        ? el("div", { class: "card empty", text: "Noch keine Gruppen. Eine Gruppe bündelt Rechte, etwa „Tester Klasse 3b“ oder „Redaktion“ nur mit Meldungen." })
        : el("div", { class: "stack" }, groups.map((group) =>
            el("div", { class: "card item" },
                el("div", { class: "item-head" },
                    el("div", {}, el("h2", { text: group.name }), group.description ? el("p", { class: "muted small-text", text: group.description }) : null),
                    el("div", { class: "actions" },
                        el("button", { type: "button", text: "Bearbeiten", onclick: () => editGroup(group) }),
                        el("button", {
                            type: "button", class: "danger", text: "Löschen",
                            onclick: async () => {
                                if (!await confirmDialog("Gruppe löschen", `„${group.name}“ löschen? Die Mitglieder verlieren die Rechte dieser Gruppe.`, "Löschen")) return;
                                try {
                                    await api("DELETE", `/api/admin/groups/${group.id}`);
                                    toast("Gruppe gelöscht.");
                                    await refresh();
                                } catch (error) { toast(error.message, "error"); }
                            }
                        }))),
                el("div", { class: "chips" },
                    group.permissions.length
                        ? group.permissions.map((p) => badge(permissionLabel(p), "info"))
                        : el("span", { class: "faint small-text", text: "keine Rechte" })),
                el("div", { class: "meta" },
                    el("span", { text: group.members.length ? `Mitglieder: ${group.members.join(", ")}` : "Keine Mitglieder – zuordnen unter „Benutzer“." })))));

    return [
        pageHead("Gruppen", "Rechte gebündelt für mehrere Benutzer. Mitglieder werden beim Benutzer zugeordnet.",
            el("button", { type: "button", class: "primary", text: "Neue Gruppe", onclick: () => editGroup(null) })),
        list
    ];
}

function editGroup(group) {
    const name = el("input", { required: true, minLength: 2, maxLength: 64, value: group?.name ?? "" });
    const description = el("input", { maxLength: 300, value: group?.description ?? "" });

    const boxes = state.meta.permissions.map((permission) => ({
        name: permission.name,
        box: el("input", { type: "checkbox", checked: group?.permissions.includes(permission.name) ?? false }),
        permission
    }));

    openDialog(group ? `Gruppe ${group.name}` : "Neue Gruppe", [
        el("div", { class: "two" }, el("label", {}, "Name", name), el("label", {}, "Beschreibung", description)),
        el("fieldset", {}, el("legend", { text: "Rechte der Mitglieder" }),
            el("div", { class: "stack" }, boxes.map((b) =>
                el("label", { class: "check" }, b.box, el("span", {}, b.permission.label, el("small", { text: b.permission.hint }))))))
    ], [
        { label: "Abbrechen" },
        {
            label: "Speichern", kind: "primary", submit: true,
            run: async () => {
                const body = {
                    name: name.value,
                    description: description.value,
                    permissions: boxes.filter((b) => b.box.checked).map((b) => b.name)
                };

                if (group) await api("PUT", `/api/admin/groups/${group.id}`, body);
                else await api("POST", "/api/admin/groups", body);

                toast("Gruppe gespeichert.", "success");
                await refresh();
            }
        }
    ]);
}

// ==== Verlauf ====

async function renderAudit() {
    const entries = await api("GET", "/api/admin/audit?take=500");

    return [
        pageHead("Verlauf", "Wer wann was geändert hat – die letzten 500 Einträge."),
        entries.length === 0
            ? el("div", { class: "card empty", text: "Noch nichts aufgezeichnet." })
            : el("div", { class: "table-wrap" },
                el("table", {},
                    el("thead", {}, el("tr", {}, ["Zeit", "Wer", "Was"].map((h) => el("th", { text: h })))),
                    el("tbody", {}, entries.map((entry) =>
                        el("tr", {},
                            el("td", { class: "nowrap faint", text: formatDate(entry.time) }),
                            el("td", { class: "nowrap", text: entry.actor }),
                            el("td", { text: entry.action }))))))
    ];
}

// ==== Einstellungen ====

async function renderSettings() {
    const settings = await api("GET", "/api/admin/settings");
    const branch = el("input", { required: true, maxLength: 100, value: settings.devBranch, class: "mono" });

    const form = el("form", { class: "card stack" },
        el("h2", { text: "Entwicklungszweig" }),
        el("p", { class: "muted small-text", text: `Von diesem Zweig in ${settings.devRepo} taggt „Dev-Version veröffentlichen“. Mit jeder neuen Version anpassen, etwa entwicklung-1.3.0.` }),
        el("label", {}, "Zweig", branch),
        el("div", { class: "row end" }, el("button", { type: "submit", class: "primary", text: "Speichern" })));

    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        try {
            await api("PUT", "/api/admin/settings", { devBranch: branch.value });
            toast("Gespeichert.", "success");
        } catch (error) {
            toast(error.message, "error");
        }
    });

    return [
        pageHead("Einstellungen"),
        el("div", { class: "stack" },
            form,
            el("div", { class: "card stack" },
                el("h2", { text: "GitHub" }),
                el("div", { class: "meta" },
                    el("span", { text: `Öffentlich: ${settings.owner}/${settings.publicRepo} (${settings.publicBranch})` }),
                    el("span", { text: `Privat: ${settings.owner}/${settings.devRepo}` })),
                settings.hasToken
                    ? badge("Token hinterlegt", "success")
                    : el("div", { class: "notice warn", text: "Kein Token hinterlegt. Es wird als Umgebungsvariable GitHub__Token auf dem Server gesetzt, nicht hier – so steht es in keiner Datenbank." })))
    ];
}

// ==== Mein Konto ====

async function renderAccount() {
    const me = state.meta.me;
    const current = el("input", { type: "password", autocomplete: "current-password", required: true, maxLength: 200 });
    const next = el("input", { type: "password", autocomplete: "new-password", required: true, minLength: state.meta.minimumPasswordLength, maxLength: 200 });
    const repeat = el("input", { type: "password", autocomplete: "new-password", required: true, maxLength: 200 });
    const error = el("p", { class: "form-error", role: "alert" });

    const form = el("form", { class: "card stack" },
        el("h2", { text: "Passwort ändern" }),
        el("p", { class: "muted small-text", text: "Danach sind alle anderen Anmeldungen dieses Kontos beendet, auch in School Manager." }),
        el("label", {}, "Bisheriges Passwort", current),
        el("div", { class: "two" }, el("label", {}, "Neues Passwort", next), el("label", {}, "Wiederholen", repeat)),
        error,
        el("div", { class: "row end" }, el("button", { type: "submit", class: "primary", text: "Passwort ändern" })));

    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        error.textContent = "";

        if (next.value !== repeat.value) {
            error.textContent = "Die beiden neuen Passwörter stimmen nicht überein.";
            return;
        }

        try {
            await api("POST", "/api/me/password", { currentPassword: current.value, newPassword: next.value });
            form.reset();
            toast("Das neue Passwort gilt.", "success");
        } catch (e) {
            error.textContent = e.message;
        }
    });

    return [
        pageHead("Mein Konto"),
        el("div", { class: "stack" },
            el("div", { class: "card stack" },
                el("div", { class: "item-title" }, el("h2", { text: me.displayName || me.userName }), badge(me.level, "info")),
                el("div", { class: "meta" },
                    el("span", { class: "mono", text: me.userName }),
                    el("span", { text: me.groups.length ? `Gruppen: ${me.groups.join(", ")}` : "keine Gruppen" })),
                el("div", { class: "chips" }, me.permissions.map((p) => badge(permissionLabel(p), "plain")))),
            form)
    ];
}

start();
