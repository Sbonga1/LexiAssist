(() => {
    "use strict";

    const audio = document.getElementById("word-reader-audio-player");
    const fallback = document.getElementById("word-reader-audio-error");
    if (!audio || !fallback) return;

    audio.addEventListener("error", () => {
        fallback.hidden = false;
    });
})();
