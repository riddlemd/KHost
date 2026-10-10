// Set before the host stops, so the circuit's drop reads as an exit, not a lost connection.
// Laid over the page, never replacing it: shutdown still re-renders the console, and Blazor's diff
// throws on nodes taken out from under it, along with the theme link the colours come from.
export function showStopped() {
    document.documentElement.dataset.khStopped = "true";

    const page = document.createElement("main");
    page.className = "kh-host-stopped";

    const heading = document.createElement("h1");
    heading.className = "kh-host-stopped__heading";
    heading.textContent = "KHost has stopped";

    const note = document.createElement("p");
    note.className = "kh-host-stopped__note";
    note.textContent = "You can close this tab.";

    page.append(heading, note);
    document.body.append(page);
}
