(() => {
  const page = document.querySelector("[data-question-authoring]");
  const selector = page?.querySelector("[data-question-type-selector]");
  if (!page || !selector) return;
  const form = page.querySelector("#questionAuthoringForm");
  const typeButtons = Array.from(page.querySelectorAll("[data-question-type-choice]"));
  const fieldGroups = Array.from(page.querySelectorAll("[data-question-types]"));
  const answerOptions = page.querySelector("[data-answer-options]");
  const choiceOptionsValue = page.querySelector("[data-choice-options-value]");
  const choiceCorrectAnswer = page.querySelector("[data-choice-correct-answer]");
  const confirmTypeChange = page.querySelector("[data-confirm-type-change]");
  const originalType = page.dataset.originalQuestionType;
  const readingStimulusHelp = page.querySelector("[data-reading-stimulus-help]");
  const authoringHeading = page.querySelector("[data-authoring-heading]");
  const authoringSupport = page.querySelector("[data-authoring-support]");
  const detailHeading = page.querySelector("[data-detail-heading]");
  const detailSupport = page.querySelector("[data-detail-support]");
  const commonQuestionFields = Array.from(page.querySelectorAll("[data-common-question-fields]"));
  const listening = page.querySelector("[data-listening-composer]");
  const listeningPrompt = listening?.querySelector("[data-listening-prompt]");
  const commonPrompt = page.querySelector("[data-question-prompt]");
  const listeningPromptInput = listeningPrompt?.querySelector("textarea");
  const responseType = listening?.querySelector("[data-listening-response-type]");
  const choiceFieldGroup = page.querySelector("[data-listening-choice-fields]");
  let currentType = selector.value;
  let recorder;
  let recorderStream;
  let recorderChunks = [];
  let timerHandle;
  let recordingSeconds = 0;
  let audioContext;
  let analyser;
  let waveformFrame;
  const supportedBy = (group, type) => group.dataset.questionTypes.split(",").includes(type);
  const inputHasValue = (input) => input.type === "checkbox" || input.type === "radio" ? input.checked : Boolean(input.value?.trim());
  const setTypeButtons = (type) => typeButtons.forEach((button) => { const selected = button.dataset.questionTypeChoice === type; button.classList.toggle("is-selected", selected); button.setAttribute("aria-pressed", String(selected)); });
  const syncOptionNames = () => {
    if (!answerOptions) return;
    const rows = Array.from(answerOptions.querySelectorAll(".question-authoring-option-row"));
    rows.forEach((row, index) => {
      const letter = String.fromCharCode(65 + index); const marker = row.querySelector("[data-option-letter]"); const answer = row.querySelector("[data-answer-option]"); const radio = row.querySelector("[data-correct-option]");
      const moveUp = row.querySelector("[data-move-option-up]"), moveDown = row.querySelector("[data-move-option-down]"), remove = row.querySelector("[data-remove-option]");
      if (marker) marker.textContent = letter;
      if (answer) { answer.name = `AnswerOptions[${index}]`; answer.setAttribute("aria-label", `Answer option ${letter}`); }
      if (radio) { radio.value = answer?.value ?? ""; radio.setAttribute("aria-label", `Mark option ${letter} as the correct answer`); }
      if (moveUp) { moveUp.setAttribute("aria-label", `Move option ${letter} up`); moveUp.disabled = index === 0; }
      if (moveDown) { moveDown.setAttribute("aria-label", `Move option ${letter} down`); moveDown.disabled = index === rows.length - 1; }
      if (remove) { remove.setAttribute("aria-label", `Remove option ${letter}`); remove.disabled = rows.length <= 2; }
    });
  };
  const syncOptionValues = () => {
    if (!answerOptions) return;
    syncOptionNames(); const rows = Array.from(answerOptions.querySelectorAll(".question-authoring-option-row")); const values = rows.map((row) => row.querySelector("[data-answer-option]")?.value.trim() ?? "");
    if (choiceOptionsValue) choiceOptionsValue.value = values.filter(Boolean).join("\n");
    const selected = answerOptions.querySelector("[data-correct-option]:checked")?.value ?? "";
    if (choiceCorrectAnswer) choiceCorrectAnswer.value = values.find((value) => value.toLowerCase() === selected.toLowerCase()) ?? "";
  };
  const setDisabled = (root, disabled) => root?.querySelectorAll("input, textarea, select, button").forEach((field) => { field.disabled = disabled; });
  const updateListeningResponse = () => {
    if (!listening) return;
    const selected = responseType?.value || "multiple-choice";
    if (choiceFieldGroup && currentType === "9") { choiceFieldGroup.hidden = selected !== "multiple-choice"; setDisabled(choiceFieldGroup, selected !== "multiple-choice"); }
    listening.querySelectorAll("[data-response-help]").forEach((help) => { help.hidden = help.dataset.responseHelp !== selected; });
    const expected = listening.querySelector("[data-listening-expected]");
    if (expected) { expected.hidden = selected !== "text-response"; setDisabled(expected, selected !== "text-response"); }
  };
  const updateFieldGroups = (type) => {
    for (const group of fieldGroups) { const visible = supportedBy(group, type) || (group === choiceFieldGroup && type === "9"); group.hidden = !visible; group.disabled = !visible; setDisabled(group, !visible); }
    if (readingStimulusHelp) readingStimulusHelp.hidden = type !== "3" && type !== "4";
    const listeningType = type === "9";
    if (listeningType && listeningPromptInput && commonPrompt) listeningPromptInput.value = commonPrompt.value;
    if (!listeningType && listeningPromptInput && commonPrompt && listeningPromptInput.value.trim()) commonPrompt.value = listeningPromptInput.value;
    if (authoringHeading) authoringHeading.textContent = listeningType ? "Listening question" : "Question authoring workspace";
    if (authoringSupport) authoringSupport.textContent = listeningType ? "Add an audio stimulus, define the learner task and preview the candidate experience." : "Create, edit and manage questions for this assessment. Use AI-assisted tools, your resource library, or create manually.";
    if (detailHeading) detailHeading.textContent = listeningType ? "Listening question details" : "Question details";
    if (detailSupport) detailSupport.textContent = listeningType ? "Configure the audio source, question task, answers and settings for this listening question." : "Enter the prompt, select the correct response, then configure the settings for this question type.";
    commonQuestionFields.forEach((field) => { field.hidden = listeningType; setDisabled(field, listeningType); });
    if (listeningPrompt) { listeningPrompt.hidden = !listeningType; setDisabled(listeningPrompt, !listeningType); }
    updateListeningResponse(); syncOptionValues();
  };
  const hasTypeData = (type) => fieldGroups.filter((group) => supportedBy(group, type)).some((group) => Array.from(group.querySelectorAll("input, textarea, select")).some(inputHasValue));
  const changeType = (nextType) => {
    const needsConfirm = page.dataset.questionMode === "edit" && nextType !== originalType && confirmTypeChange?.value !== "true" && hasTypeData(originalType);
    if (needsConfirm && !window.confirm("Changing question type can clear settings stored for the current type. Continue?")) { selector.value = currentType; setTypeButtons(currentType); return; }
    if (needsConfirm && confirmTypeChange) confirmTypeChange.value = "true";
    if (nextType === currentType) return; selector.value = nextType; currentType = nextType; setTypeButtons(nextType); updateFieldGroups(nextType);
  };
  typeButtons.forEach((button) => button.addEventListener("click", () => changeType(button.dataset.questionTypeChoice)));
  page.querySelector("[data-add-option]")?.addEventListener("click", () => {
    if (!answerOptions) return;
    const index = answerOptions.querySelectorAll(".question-authoring-option-row").length; const row = document.createElement("div"); row.className = "question-authoring-option-row";
    row.innerHTML = `<span class="question-authoring-option-letter" data-option-letter></span><input type="radio" name="SelectedCorrectAnswer" data-correct-option /><input class="form-control" name="AnswerOptions[${index}]" maxlength="2000" data-answer-option /><span class="question-authoring-option-move-controls"><button type="button" class="question-authoring-move-button" data-move-option-up>↑</button><button type="button" class="question-authoring-move-button" data-move-option-down>↓</button></span><button type="button" class="question-authoring-icon-button" data-remove-option>×</button>`;
    answerOptions.append(row); row.querySelector("[data-answer-option]")?.focus(); syncOptionValues();
  });
  answerOptions?.addEventListener("input", (event) => { if (event.target.matches("[data-answer-option]")) syncOptionValues(); });
  answerOptions?.addEventListener("change", (event) => { if (event.target.matches("[data-correct-option]")) syncOptionValues(); });
  answerOptions?.addEventListener("click", (event) => {
    const moveUp = event.target.closest("[data-move-option-up]"), moveDown = event.target.closest("[data-move-option-down]"), move = moveUp ?? moveDown;
    if (move) { const row = move.closest(".question-authoring-option-row"), neighbor = moveUp ? row?.previousElementSibling : row?.nextElementSibling; if (row && neighbor) { if (moveUp) answerOptions.insertBefore(row, neighbor); else answerOptions.insertBefore(neighbor, row); syncOptionValues(); } return; }
    const remove = event.target.closest("[data-remove-option]"); if (remove && answerOptions.querySelectorAll(".question-authoring-option-row").length > 2) { remove.closest(".question-authoring-option-row")?.remove(); syncOptionValues(); }
  });

  const audioPath = listening?.querySelector("[data-audio-path]"); const audioDuration = listening?.querySelector("[data-audio-duration]"); const stimulus = listening?.querySelector("[data-audio-stimulus]"); const storedPreview = listening?.querySelector("[data-audio-stored-preview]"); const fileNameLabel = listening?.querySelector("[data-audio-file-name]"); const durationLabel = listening?.querySelector("[data-audio-duration-label]"); const uploadStatus = listening?.querySelector("[data-upload-status]"); const permissionState = listening?.querySelector("[data-permission-state]"); const permissionActions = listening?.querySelector("[data-permission-actions]"); const recordStatus = listening?.querySelector("[data-record-status]"); const fileInput = listening?.querySelector("[data-audio-file]"); const dropzone = listening?.querySelector("[data-audio-dropzone]"); const uploadUrl = listening?.dataset.audioUploadUrl; const removeUrl = listening?.dataset.audioRemoveUrl; const token = form?.querySelector("input[name='__RequestVerificationToken']")?.value;
  const setStimulus = (payload) => { if (!audioPath || !stimulus) return; const fileName = payload.fileName || ""; listening.dataset.currentAudioFile = fileName; audioPath.value = fileName; if (audioDuration) audioDuration.value = payload.durationSeconds || ""; if (fileNameLabel) fileNameLabel.textContent = fileName || "Audio stimulus"; if (durationLabel) durationLabel.textContent = payload.durationSeconds ? ` · ${payload.durationSeconds}s` : ""; if (storedPreview && payload.previewUrl) storedPreview.src = payload.previewUrl; stimulus.hidden = !fileName; };
  const validateBrowserDuration = (file) => new Promise((resolve, reject) => { const url = URL.createObjectURL(file); const audio = document.createElement("audio"); audio.preload = "metadata"; audio.onloadedmetadata = () => { const duration = Number.isFinite(audio.duration) ? Math.ceil(audio.duration) : null; URL.revokeObjectURL(url); if (duration && duration > 120) reject(new Error("Audio must be no longer than two minutes.")); else resolve(duration); }; audio.onerror = () => { URL.revokeObjectURL(url); reject(new Error("The selected file could not be decoded as audio.")); }; audio.src = url; });
  const sendAudio = async (file, source) => {
    if (!uploadUrl || !file) return; if (uploadStatus) uploadStatus.textContent = "Validating and storing audio privately…"; const body = new FormData(); body.append("audioFile", file); body.append("source", source);
    try { const duration = await validateBrowserDuration(file); const response = await fetch(uploadUrl, { method: "POST", body, credentials: "same-origin", headers: { RequestVerificationToken: token || "" } }); const payload = await response.json(); if (!response.ok) throw new Error(payload.error || "The audio could not be stored."); if (!payload.durationSeconds && duration) payload.durationSeconds = duration; setStimulus(payload); if (uploadStatus) uploadStatus.textContent = `${payload.fileName} is ready for this question.`; } catch (error) { if (uploadStatus) uploadStatus.textContent = error.message; }
  };
  const setPanel = (mode) => listening?.querySelectorAll("[data-audio-panel]").forEach((panel) => { panel.hidden = panel.dataset.audioPanel !== mode; });
  listening?.querySelectorAll("[data-audio-source]").forEach((radio) => radio.addEventListener("change", () => {
    const previous = listening.querySelector("[data-audio-source][data-previous-checked]");
    if (audioPath?.value && previous && previous.value !== radio.value && !window.confirm("Changing the audio source may replace the current stimulus. Continue?")) { radio.checked = false; previous.checked = true; return; }
    listening.querySelectorAll("[data-audio-source]").forEach((item) => item.toggleAttribute("data-previous-checked", item.checked));
    setPanel(radio.value); if (radio.value === "existing") { const existing = listening.querySelector("[data-existing-audio-path]"); if (existing) existing.value = audioPath?.value || ""; }
  }));
  fileInput?.addEventListener("change", () => sendAudio(fileInput.files?.[0], "upload"));
  ["dragover", "dragenter"].forEach((name) => dropzone?.addEventListener(name, (event) => { event.preventDefault(); dropzone.classList.add("is-dragging"); }));
  ["dragleave", "drop"].forEach((name) => dropzone?.addEventListener(name, (event) => { event.preventDefault(); dropzone.classList.remove("is-dragging"); }));
  dropzone?.addEventListener("drop", (event) => sendAudio(event.dataTransfer.files?.[0], "upload"));
  listening?.querySelector("[data-existing-audio-path]")?.addEventListener("input", (event) => { if (audioPath) audioPath.value = event.target.value.trim(); });
  listening?.querySelector("[data-replace-audio]")?.addEventListener("click", () => { listening.querySelector("[data-audio-source='upload']")?.click(); fileInput?.click(); });
  listening?.querySelector("[data-remove-audio]")?.addEventListener("click", async () => { if (!window.confirm("Remove this audio stimulus? You will need to add it again before saving.")) return; const currentFile = listening?.dataset.currentAudioFile || audioPath?.value; if (removeUrl && currentFile) { await fetch(removeUrl, { method: "POST", credentials: "same-origin", headers: { RequestVerificationToken: token || "" } }); } setStimulus({ fileName: "" }); if (storedPreview) storedPreview.removeAttribute("src"); });
  const stopTimer = () => { if (timerHandle) window.clearInterval(timerHandle); timerHandle = undefined; };
  const stopWaveform = () => { if (waveformFrame) window.cancelAnimationFrame(waveformFrame); waveformFrame = undefined; analyser = undefined; if (audioContext) { audioContext.close(); audioContext = undefined; } };
  const drawWaveform = () => { const canvas = listening?.querySelector("[data-waveform]"); if (!canvas || !analyser) return; const context = canvas.getContext("2d"); const values = new Uint8Array(analyser.fftSize); analyser.getByteTimeDomainData(values); context.clearRect(0, 0, canvas.width, canvas.height); context.strokeStyle = "#147bd1"; context.lineWidth = 2; context.beginPath(); values.forEach((value, index) => { const x = index / (values.length - 1) * canvas.width; const y = value / 255 * canvas.height; index ? context.lineTo(x, y) : context.moveTo(x, y); }); context.stroke(); waveformFrame = window.requestAnimationFrame(drawWaveform); };
  const startWaveform = () => { if (!recorderStream || !window.AudioContext) return; audioContext = new AudioContext(); analyser = audioContext.createAnalyser(); analyser.fftSize = 256; audioContext.createMediaStreamSource(recorderStream).connect(analyser); drawWaveform(); };
  const stopTracks = () => { recorderStream?.getTracks().forEach((track) => track.stop()); recorderStream = undefined; stopWaveform(); };
  const updateTimer = () => { recordingSeconds += 1; if (recordStatus) recordStatus.textContent = `Recording · 00:${String(recordingSeconds).padStart(2, "0")} / 02:00`; if (recordingSeconds >= 120) recorder?.stop(); };
  const populateMicrophones = async () => {
    const deviceSelect = listening?.querySelector("[data-microphone-device]");
    if (!deviceSelect || !navigator.mediaDevices?.enumerateDevices) return;
    const devices = (await navigator.mediaDevices.enumerateDevices()).filter((device) => device.kind === "audioinput");
    deviceSelect.innerHTML = "";
    devices.forEach((device, index) => { const option = document.createElement("option"); option.value = device.deviceId; option.textContent = device.label || `Microphone ${index + 1}`; deviceSelect.append(option); });
  };
  listening?.querySelector("[data-record-button]")?.addEventListener("click", async () => {
    if (!navigator.mediaDevices?.getUserMedia || !window.MediaRecorder) { if (permissionState) permissionState.textContent = "Microphone access was not granted."; if (permissionActions) permissionActions.hidden = false; return; }
    try { recorderStream = await navigator.mediaDevices.getUserMedia({ audio: { deviceId: listening.querySelector("[data-microphone-device]")?.value || undefined } }); if (permissionState) permissionState.textContent = "Microphone permission granted."; if (permissionActions) permissionActions.hidden = true; await populateMicrophones(); startWaveform(); recorder = new MediaRecorder(recorderStream); recorderChunks = []; recordingSeconds = 0; recorder.addEventListener("dataavailable", (event) => { if (event.data.size) recorderChunks.push(event.data); }); recorder.addEventListener("stop", () => { stopTimer(); stopTracks(); const blob = new Blob(recorderChunks, { type: recorder.mimeType || "audio/webm" }); const ext = (recorder.mimeType || "").includes("ogg") ? "ogg" : "webm"; sendAudio(new File([blob], `recording.${ext}`, { type: blob.type }), "record"); if (recordStatus) recordStatus.textContent = "Recording ready and stored privately."; }); recorder.start(); timerHandle = window.setInterval(updateTimer, 1000); if (recordStatus) recordStatus.textContent = "Recording · 00:00 / 02:00"; listening.querySelector("[data-record-button]").disabled = true; listening.querySelector("[data-stop-recording]").disabled = false; listening.querySelector("[data-pause-recording]").disabled = false; } catch { if (permissionState) permissionState.textContent = "Microphone access was not granted."; if (permissionActions) permissionActions.hidden = false; }
  });
  listening?.querySelector("[data-retry-recording]")?.addEventListener("click", () => listening.querySelector("[data-record-button]")?.click());
  listening?.querySelector("[data-upload-instead]")?.addEventListener("click", () => listening.querySelector("[data-audio-source='upload']")?.click());
  listening?.querySelector("[data-stop-recording]")?.addEventListener("click", () => { if (recorder && recorder.state !== "inactive") recorder.stop(); listening.querySelector("[data-record-button]").disabled = false; listening.querySelector("[data-stop-recording]").disabled = true; });
  listening?.querySelector("[data-pause-recording]")?.addEventListener("click", (event) => { if (!recorder) return; if (recorder.state === "recording") { recorder.pause(); event.target.textContent = "Resume"; stopTimer(); } else if (recorder.state === "paused") { recorder.resume(); event.target.textContent = "Pause"; timerHandle = window.setInterval(updateTimer, 1000); } });
  listening?.querySelector("[data-restart-recording]")?.addEventListener("click", () => { if (recorder && recorder.state !== "inactive") recorder.stop(); stopTracks(); listening.querySelector("[data-record-button]")?.click(); });
  responseType?.addEventListener("change", updateListeningResponse);
  commonPrompt?.addEventListener("input", () => { if (currentType === "9" && listeningPromptInput) listeningPromptInput.value = commonPrompt.value; });
  listeningPromptInput?.addEventListener("input", () => { if (currentType === "9" && commonPrompt) commonPrompt.value = listeningPromptInput.value; });
  for (const tab of page.querySelectorAll("[data-smart-tab]")) tab.addEventListener("click", () => { page.querySelectorAll("[data-smart-tab]").forEach((sibling) => { const selected = sibling === tab; sibling.classList.toggle("is-selected", selected); sibling.setAttribute("aria-selected", String(selected)); }); page.querySelectorAll("[data-smart-panel]").forEach((panel) => { panel.hidden = panel.dataset.smartPanel !== tab.dataset.smartTab; }); });
  form?.addEventListener("submit", () => { if (audioPath && listening?.dataset.currentAudioFile) audioPath.value = listening.dataset.currentAudioFile; syncOptionValues(); });
  const existingAudioPath = listening?.querySelector("[data-existing-audio-path]");
  if (listening && audioPath && !audioPath.value && existingAudioPath?.value) audioPath.value = existingAudioPath.value;
  if (listening && audioPath?.value) listening.dataset.currentAudioFile = audioPath.value;
  const selectedSource = listening?.querySelector("[data-audio-source]:checked")?.value; if (selectedSource) { listening.querySelector("[data-audio-source]:checked")?.toggleAttribute("data-previous-checked", true); setPanel(selectedSource); }
  syncOptionValues(); setTypeButtons(selector.value); updateFieldGroups(selector.value); updateListeningResponse();
})();
