// Collocated JS for RVideoV2Info.razor (RVID-UI-V2-PHASE3A.1 reference controls).
// Opens the hidden native <input type=file> for the compact Upload icon button so the
// file chooser is reachable via keyboard/click without exposing the native input chrome.
export function triggerFileInput(selector) {
    const input = document.querySelector(selector);
    if (input && typeof input.click === "function") {
        input.click();
    }
}