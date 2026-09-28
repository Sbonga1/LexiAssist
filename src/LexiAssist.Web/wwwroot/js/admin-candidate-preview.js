(() => {
  const root = document.querySelector("[data-candidate-preview]");
  if (!root) return;

  const questions = [...root.querySelectorAll("[data-preview-question]")];
  const modeButtons = [...root.querySelectorAll("[data-preview-mode]")];
  const pager = root.querySelector("[data-preview-pager]");
  const navigation = root.querySelector("[data-preview-navigation]");
  const pageNumbers = root.querySelector("[data-preview-page-numbers]");
  const progress = root.querySelector("[data-preview-progress]");
  const progressLabel = root.querySelector("[data-preview-progress-label]");
  const questionCount = root.querySelector("[data-preview-question-count]");
  const previous = root.querySelector("[data-preview-previous]");
  const next = root.querySelector("[data-preview-next]");
  const modeKey = `lexiassist-admin-preview-mode-${root.dataset.testId || "assessment"}`;
  const recordings = new Map();
  let mode = "list";
  let activeIndex = 0;
  let recorder = null;
  let stream = null;
  let recordingQuestion = null;
  let chunks = [];
  let audioContext = null;
  let animationFrame = 0;

  try {
    mode = sessionStorage.getItem(modeKey) === "pager" ? "pager" : "list";
  } catch {
    mode = "list";
  }

  function questionKey(index) {
    const question = questions[index];
    return question?.id || `${root.dataset.testId || "assessment"}-${index}`;
  }

  function renderPageNumbers() {
    if (!pageNumbers) return;
    pageNumbers.replaceChildren();
    const total = questions.length;
    let visible = [...Array(total).keys()];
    if (total > 7) {
      const keep = new Set([0, total - 1, activeIndex - 1, activeIndex, activeIndex + 1]);
      visible = [...keep].filter(value => value >= 0 && value < total).sort((a, b) => a - b);
    }

    let last = -2;
    for (const index of visible) {
      if (index - last > 1 && last >= 0) {
        const ellipsis = document.createElement("span");
        ellipsis.className = "assessment-preview-ellipsis";
        ellipsis.textContent = "…";
        ellipsis.setAttribute("aria-hidden", "true");
        pageNumbers.append(ellipsis);
      }
      const button = document.createElement("button");
      button.type = "button";
      button.className = "assessment-preview-page-number";
      button.textContent = String(index + 1);
      button.setAttribute("aria-label", `Go to question ${index + 1}`);
      button.setAttribute("aria-current", index === activeIndex ? "page" : "false");
      if (index === activeIndex) button.classList.add("is-current");
      button.addEventListener("click", () => showQuestion(index, true));
      pageNumbers.append(button);
      last = index;
    }
  }

  function refreshVoiceWidgets() {
    for (const [index, question] of questions.entries()) {
      const voice = question.querySelector("[data-preview-voice]");
      const listNote = question.querySelector("[data-preview-voice-list-note]");
      if (voice) voice.hidden = mode !== "pager" || index !== activeIndex;
      if (listNote) listNote.hidden = mode === "pager";
      if (voice && mode === "pager" && index === activeIndex) updateVoiceControls(question, recordings.get(questionKey(index)));
    }
  }

  function showQuestion(index, moveFocus) {
    if (!questions.length) return;
    if (recorder?.state === "recording") stopRecording();
    activeIndex = Math.max(0, Math.min(questions.length - 1, index));
    root.classList.toggle("is-preview-pager", mode === "pager");
    root.classList.toggle("is-preview-list", mode === "list");
    for (const [position, question] of questions.entries()) {
      question.hidden = mode === "pager" && position !== activeIndex;
    }
    if (pager) pager.hidden = mode !== "pager";
    if (navigation) navigation.hidden = mode !== "pager";
    if (progress) {
      progress.max = questions.length;
      progress.value = activeIndex + 1;
    }
    if (progressLabel) progressLabel.textContent = `${Math.round(((activeIndex + 1) / questions.length) * 100)}%`;
    if (questionCount) questionCount.textContent = `Question ${activeIndex + 1} of ${questions.length}`;
    if (previous) previous.disabled = activeIndex === 0;
    if (next) next.disabled = activeIndex === questions.length - 1;
    for (const button of modeButtons) {
      button.setAttribute("aria-pressed", String(button.dataset.previewMode === mode));
    }
    renderPageNumbers();
    refreshVoiceWidgets();
    if (moveFocus && mode === "pager") questions[activeIndex].querySelector("h2")?.focus();
  }

  function setMode(nextMode) {
    if (mode === nextMode) return;
    if (recorder?.state === "recording") stopRecording();
    mode = nextMode;
    try { sessionStorage.setItem(modeKey, mode); } catch { /* Session preference is optional. */ }
    showQuestion(activeIndex, false);
  }

  function setStatus(question, message) {
    const status = question.querySelector("[data-preview-recording-status]");
    if (status) status.textContent = message;
  }

  function setError(question, message) {
    const error = question.querySelector("[data-preview-recording-error]");
    if (!error) return;
    error.textContent = message;
    error.hidden = !message;
  }

  function setWaveform(question, active) {
    const waveform = question.querySelector("[data-preview-waveform]");
    if (!waveform) return;
    waveform.classList.toggle("is-recording", active);
    waveform.querySelectorAll("span").forEach((bar, index) => {
      bar.style.setProperty("--bar-height", active ? `${22 + ((index * 37) % 62)}%` : `${12 + ((index * 13) % 18)}%`);
    });
  }

  function updateVoiceControls(question, recording) {
    const isActiveRecorder = questionKey(activeIndex) === recordingQuestion && recorder?.state === "recording";
    const start = question.querySelector("[data-preview-start]");
    const stop = question.querySelector("[data-preview-stop]");
    const play = question.querySelector("[data-preview-play]");
    const clear = question.querySelector("[data-preview-clear]");
    const audio = question.querySelector("[data-preview-audio]");
    if (start) {
      start.disabled = isActiveRecorder;
      start.querySelector("span").textContent = isActiveRecorder ? "Recording…" : "Start recording";
    }
    if (stop) stop.disabled = !isActiveRecorder;
    if (play) play.disabled = !recording;
    if (clear) clear.disabled = !recording && !isActiveRecorder;
    if (audio) {
      audio.hidden = !recording;
      audio.src = recording?.url || "";
    }
    setWaveform(question, isActiveRecorder);
  }

  async function startRecording(question) {
    if (recorder?.state === "recording") return;
    const key = questionKey(activeIndex);
    setError(question, "");
    setStatus(question, "Waiting for microphone permission…");
    const start = question.querySelector("[data-preview-start]");
    if (start) start.disabled = true;
    const mediaDevices = window.navigator?.mediaDevices;

    if (!mediaDevices?.getUserMedia || typeof window.MediaRecorder === "undefined") {
      setStatus(question, "Microphone capture is unavailable in this browser.");
      setError(question, "Use a browser that supports secure microphone capture, then try again.");
      updateVoiceControls(question, recordings.get(key));
      return;
    }

    let permissionRequest;
    let permissionTimer;
    try {
      permissionRequest = mediaDevices.getUserMedia({ audio: true });
      stream = await Promise.race([
        permissionRequest,
        new Promise((_, reject) => {
          permissionTimer = window.setTimeout(() => {
            const error = new Error("Microphone permission is taking longer than expected.");
            error.name = "PermissionPromptTimeout";
            reject(error);
          }, 12000);
        })
      ]);
      window.clearTimeout(permissionTimer);
      if (questionKey(activeIndex) !== key || mode !== "pager") {
        stream.getTracks().forEach(track => track.stop());
        stream = null;
        return;
      }
      const options = window.MediaRecorder.isTypeSupported?.("audio/webm;codecs=opus") ? { mimeType: "audio/webm;codecs=opus" } : undefined;
      recorder = new window.MediaRecorder(stream, options);
      chunks = [];
      recordingQuestion = key;
      recorder.addEventListener("dataavailable", event => {
        if (event.data?.size) chunks.push(event.data);
      });
      recorder.addEventListener("stop", () => finishRecording(question, key), { once: true });
      recorder.start();
      setStatus(question, "Recording. Select Stop when you are finished.");
      updateVoiceControls(question, recordings.get(key));
      startWaveform(question);
    } catch (error) {
      window.clearTimeout(permissionTimer);
      if (error?.name === "PermissionPromptTimeout") {
        permissionRequest?.then(lateStream => lateStream.getTracks().forEach(track => track.stop())).catch(() => {});
      }
      const denied = error?.name === "NotAllowedError" || error?.name === "SecurityError";
      const unavailable = error?.name === "NotFoundError" || error?.name === "DevicesNotFoundError";
      setStatus(question, "Ready to record. No audio was saved.");
      setError(question, denied
        ? "Microphone access was denied. Allow microphone access for this site in browser settings, then retry."
        : error?.name === "PermissionPromptTimeout"
          ? "Microphone permission is still pending. Allow access for this site in browser settings, then retry."
        : unavailable
          ? "No microphone was found. Connect a microphone and retry."
          : "The microphone could not start. Check browser permission and device connection, then retry.");
      updateVoiceControls(question, recordings.get(key));
    }
  }

  function startWaveform(question) {
    const Context = window.AudioContext || window.webkitAudioContext;
    if (!Context || !stream) return;
    try {
      audioContext = new Context();
      const analyser = audioContext.createAnalyser();
      analyser.fftSize = 64;
      audioContext.createMediaStreamSource(stream).connect(analyser);
      const values = new Uint8Array(analyser.frequencyBinCount);
      const bars = [...question.querySelectorAll("[data-preview-waveform] span")];
      const draw = () => {
        analyser.getByteFrequencyData(values);
        bars.forEach((bar, index) => bar.style.setProperty("--bar-height", `${Math.max(10, (values[index % values.length] / 255) * 100)}%`));
        animationFrame = requestAnimationFrame(draw);
      };
      draw();
    } catch {
      /* The recorder still works in browsers without Web Audio visualization. */
    }
  }

  function stopRecording() {
    if (recorder?.state === "recording") recorder.stop();
    stream?.getTracks().forEach(track => track.stop());
    stream = null;
    cancelAnimationFrame(animationFrame);
    animationFrame = 0;
    audioContext?.close().catch(() => {});
    audioContext = null;
  }

  function finishRecording(question, key) {
    const blob = new Blob(chunks, { type: recorder?.mimeType || "audio/webm" });
    chunks = [];
    recorder = null;
    recordingQuestion = null;
    if (blob.size > 0) {
      const prior = recordings.get(key);
      if (prior) URL.revokeObjectURL(prior.url);
      recordings.set(key, { blob, url: URL.createObjectURL(blob) });
      setStatus(question, "Recording captured temporarily. You can play it back or clear it.");
    } else {
      setStatus(question, "No audio was captured. Start a new recording to retry.");
    }
    updateVoiceControls(question, recordings.get(key));
  }

  async function playRecording(question) {
    const recording = recordings.get(questionKey(activeIndex));
    if (!recording) return;
    const audio = question.querySelector("[data-preview-audio]");
    try {
      await audio.play();
      setStatus(question, "Playing the temporary recording.");
      audio.onended = () => setStatus(question, "Recording captured temporarily. You can play it back or clear it.");
    } catch {
      setError(question, "Playback could not start. Use the audio player controls to retry.");
    }
  }

  function clearRecording(question) {
    const key = questionKey(activeIndex);
    if (recorder?.state === "recording" && recordingQuestion === key) stopRecording();
    const recording = recordings.get(key);
    if (recording) URL.revokeObjectURL(recording.url);
    recordings.delete(key);
    const audio = question.querySelector("[data-preview-audio]");
    if (audio) {
      audio.pause();
      audio.removeAttribute("src");
      audio.load();
    }
    setError(question, "");
    setStatus(question, "Recording cleared. Ready to record.");
    updateVoiceControls(question, null);
  }

  modeButtons.forEach(button => button.addEventListener("click", () => setMode(button.dataset.previewMode)));
  previous?.addEventListener("click", () => showQuestion(activeIndex - 1, true));
  next?.addEventListener("click", () => showQuestion(activeIndex + 1, true));
  root.addEventListener("keydown", event => {
    if (mode !== "pager" || event.altKey || event.ctrlKey || event.metaKey) return;
    if (event.target instanceof HTMLElement && event.target.matches("input, textarea, select, audio, button")) return;
    if (event.key === "ArrowRight" && activeIndex < questions.length - 1) {
      event.preventDefault();
      showQuestion(activeIndex + 1, true);
    } else if (event.key === "ArrowLeft" && activeIndex > 0) {
      event.preventDefault();
      showQuestion(activeIndex - 1, true);
    }
  });
  questions.forEach(question => {
    question.querySelector("[data-preview-start]")?.addEventListener("click", () => startRecording(question));
    question.querySelector("[data-preview-stop]")?.addEventListener("click", stopRecording);
    question.querySelector("[data-preview-play]")?.addEventListener("click", () => playRecording(question));
    question.querySelector("[data-preview-clear]")?.addEventListener("click", () => clearRecording(question));
  });

  showQuestion(0, false);
  window.addEventListener("pagehide", () => {
    if (recorder?.state === "recording") stopRecording();
    for (const recording of recordings.values()) URL.revokeObjectURL(recording.url);
  }, { once: true });
})();
