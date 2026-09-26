(() => {
    "use strict";

    const formatTime = (milliseconds) => {
        const totalSeconds = Math.max(0, Math.ceil(milliseconds / 1000));
        const minutes = Math.floor(totalSeconds / 60).toString().padStart(2, "0");
        const seconds = (totalSeconds % 60).toString().padStart(2, "0");
        return `${minutes}:${seconds}`;
    };

    const readJson = async (response) => {
        try {
            return await response.json();
        } catch {
            return {};
        }
    };

    const initialize = (root) => {
        const form = root.querySelector("[data-audio-recording-consent-form]");
        const consent = root.querySelector("[data-audio-recording-consent]");
        const startButton = root.querySelector("[data-audio-recording-start]");
        const stopButton = root.querySelector("[data-audio-recording-stop]");
        const status = root.querySelector("[data-audio-recording-status]");
        const playback = root.querySelector("[data-audio-recording-playback]");
        const startUrl = root.dataset.startUrl;
        const recordingsUrl = root.dataset.recordingsUrl;
        const maxSeconds = Number.parseInt(root.dataset.maxDurationSeconds || "120", 10);
        const token = form?.querySelector('input[name="__RequestVerificationToken"]')?.value;
        let mediaRecorder = null;
        let mediaStream = null;
        let chunks = [];
        let recordingId = null;
        let completeUrl = null;
        let startedAt = 0;
        let stopTimer = null;
        let clockTimer = null;
        let isUploading = false;

        const setStatus = (message) => {
            status.textContent = message;
        };

        const stopTracks = () => {
            mediaStream?.getTracks().forEach((track) => track.stop());
            mediaStream = null;
        };

        const appendPlayback = (entry) => {
            if (!entry?.playbackUrl || new URL(entry.playbackUrl, window.location.href).origin !== window.location.origin) {
                return;
            }

            const wrapper = document.createElement("div");
            wrapper.className = "mb-3";
            const label = document.createElement("p");
            label.className = "mb-1 fw-semibold";
            label.textContent = `Saved recording${entry.durationMilliseconds ? ` (${Math.round(entry.durationMilliseconds / 1000)} seconds)` : ""}`;
            const audio = document.createElement("audio");
            audio.controls = true;
            audio.preload = "none";
            audio.src = entry.playbackUrl;
            audio.setAttribute("aria-label", "Play your saved voice recording");
            wrapper.append(label, audio);
            playback.append(wrapper);
        };

        const loadPlayback = async () => {
            if (!recordingsUrl) {
                return;
            }

            try {
                const response = await fetch(recordingsUrl, { credentials: "same-origin", headers: { Accept: "application/json" } });
                if (!response.ok) {
                    return;
                }

                const entries = await response.json();
                if (Array.isArray(entries)) {
                    entries.forEach(appendPlayback);
                }
            } catch {
                // A missing recordings list does not stop the learning activity.
            }
        };

        if (!form) {
            void loadPlayback();
            return;
        }

        const uploadRecording = async () => {
            if (isUploading || !recordingId || !completeUrl || chunks.length === 0) {
                setStatus("No audio was captured. You can continue without a recording.");
                startButton.disabled = false;
                stopButton.disabled = true;
                return;
            }

            isUploading = true;
            const mediaType = mediaRecorder?.mimeType || chunks[0]?.type || "audio/webm";
            const audioBlob = new Blob(chunks, { type: mediaType });
            const data = new FormData();
            data.append("AudioFile", audioBlob, "learner-recording");
            data.append("DurationMilliseconds", Math.max(250, Math.round(performance.now() - startedAt)).toString());
            data.append("__RequestVerificationToken", token || "");
            setStatus("Saving your recording securely…");

            try {
                const response = await fetch(completeUrl, {
                    method: "POST",
                    credentials: "same-origin",
                    headers: { RequestVerificationToken: token || "" },
                    body: data
                });
                const result = await readJson(response);
                if (!response.ok) {
                    setStatus(result.message || "The recording could not be saved. You can continue without it.");
                    return;
                }

                setStatus("Recording saved. You can play it below.");
                await loadPlayback();
            } catch {
                setStatus("The recording could not be saved right now. You can continue without it.");
            } finally {
                isUploading = false;
                startButton.disabled = false;
                stopButton.disabled = true;
                consent.checked = false;
                recordingId = null;
                completeUrl = null;
                chunks = [];
            }
        };

        const stopRecording = () => {
            if (mediaRecorder && mediaRecorder.state !== "inactive") {
                mediaRecorder.stop();
                if (stopTimer) window.clearTimeout(stopTimer);
                if (clockTimer) window.clearInterval(clockTimer);
                stopButton.disabled = true;
                setStatus("Recording stopped. Saving securely…");
            }
        };

        if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia || !window.MediaRecorder) {
            startButton.disabled = true;
            stopButton.disabled = true;
            consent.disabled = true;
            setStatus("Audio recording is not supported in this browser or connection. You can continue without recording.");
        } else {
            startButton.addEventListener("click", async () => {
                if (!consent.checked) {
                    setStatus("Please read and accept the recording notice before starting.");
                    consent.focus();
                    return;
                }

                startButton.disabled = true;
                setStatus("Requesting microphone access…");
                let startResponse;
                try {
                    mediaStream = await navigator.mediaDevices.getUserMedia({ audio: true });
                    const data = new URLSearchParams({ consentAccepted: "true" });
                    startResponse = await fetch(startUrl, {
                        method: "POST",
                        credentials: "same-origin",
                        headers: {
                            "Content-Type": "application/x-www-form-urlencoded;charset=UTF-8",
                            RequestVerificationToken: token || ""
                        },
                        body: data
                    });

                    const session = await readJson(startResponse);
                    if (!startResponse.ok) {
                        throw new Error(session.message || "This activity is not currently set up for recording.");
                    }

                    recordingId = session.recordingId;
                    completeUrl = session.completeUrl;
                    if (!recordingId || !completeUrl || new URL(completeUrl, window.location.href).origin !== window.location.origin) {
                        throw new Error("A secure recording session could not be started.");
                    }

                    const maxDuration = Math.min(maxSeconds, Number.parseInt(session.maxDurationSeconds, 10) || maxSeconds);
                    const mimeType = ["audio/webm;codecs=opus", "audio/ogg;codecs=opus", "audio/mp4"]
                        .find((candidate) => MediaRecorder.isTypeSupported?.(candidate));
                    mediaRecorder = mimeType ? new MediaRecorder(mediaStream, { mimeType }) : new MediaRecorder(mediaStream);
                    chunks = [];
                    mediaRecorder.addEventListener("dataavailable", (event) => {
                        if (event.data?.size > 0) chunks.push(event.data);
                    });
                    mediaRecorder.addEventListener("stop", () => {
                        stopTracks();
                        void uploadRecording();
                    }, { once: true });
                    mediaRecorder.addEventListener("error", () => {
                        stopTracks();
                        setStatus("The browser could not record audio. You can continue without a recording.");
                        startButton.disabled = false;
                        stopButton.disabled = true;
                    }, { once: true });

                    startedAt = performance.now();
                    mediaRecorder.start(500);
                    stopButton.disabled = false;
                    setStatus(`Recording… maximum ${formatTime(maxDuration * 1000)}. Select Stop and save when you are ready.`);
                    stopTimer = window.setTimeout(stopRecording, maxDuration * 1000);
                    clockTimer = window.setInterval(() => {
                        const remaining = Math.max(0, maxDuration * 1000 - (performance.now() - startedAt));
                        setStatus(`Recording… ${formatTime(remaining)} remaining. Select Stop and save when you are ready.`);
                    }, 1000);
                } catch (error) {
                    stopTracks();
                    if (startResponse?.ok && completeUrl) {
                        setStatus("Microphone recording did not start. Please reload this activity before trying again.");
                    } else {
                        setStatus(error instanceof Error ? error.message : "Microphone access is unavailable. You can continue without a recording.");
                    }
                    startButton.disabled = false;
                    stopButton.disabled = true;
                }
            });
            stopButton.addEventListener("click", stopRecording);
        }

        void loadPlayback();
    };

    document.querySelectorAll("[data-learner-audio-recorder]").forEach(initialize);
})();
