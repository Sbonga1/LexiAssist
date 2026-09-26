(() => {
  const player = document.querySelector("[data-listening-player]");
  if (!player) {
    return;
  }

  const audio = player.querySelector("[data-listening-audio]");
  const playButton = player.querySelector("[data-listening-play]");
  const replayButton = player.querySelector("[data-listening-replay]");
  const status = player.querySelector("[data-listening-status]");
  const errorMessage = player.querySelector("[data-listening-error]");
  const answerSubmit = document.querySelector("[data-listening-answer-submit]");
  const replayLimitValue = player.dataset.maxReplays;
  const replayLimit = replayLimitValue === "" ? null : Number.parseInt(replayLimitValue, 10);

  let firstListenCompleted = false;
  let replayCount = 0;

  const updateReplayButton = () => {
    const hasReplaysLeft = replayLimit === null || replayCount < replayLimit;
    replayButton.disabled = !firstListenCompleted || !hasReplaysLeft || !audio.paused;
    if (firstListenCompleted && !hasReplaysLeft) {
      replayButton.textContent = "Replay limit reached";
    } else {
      replayButton.textContent = "Replay audio";
    }
  };

  const showAudioError = () => {
    errorMessage.textContent = "The audio could not be loaded or played. Your progress is saved; please ask your support person to check the audio file.";
    errorMessage.hidden = false;
    errorMessage.focus();
    status.textContent = "Audio is unavailable.";
    playButton.disabled = true;
    replayButton.disabled = true;
    answerSubmit.disabled = !firstListenCompleted;
  };

  const playAudio = async () => {
    errorMessage.hidden = true;
    if (!audio.paused) {
      audio.pause();
      playButton.textContent = "Resume audio";
      status.textContent = "Audio paused.";
      updateReplayButton();
      return;
    }

    try {
      await audio.play();
      playButton.textContent = "Pause audio";
      status.textContent = "Audio is playing.";
    } catch {
      showAudioError();
    }
  };

  const replayAudio = async () => {
    if (!firstListenCompleted || (replayLimit !== null && replayCount >= replayLimit)) {
      updateReplayButton();
      return;
    }

    replayButton.disabled = true;
    errorMessage.hidden = true;
    try {
      audio.currentTime = 0;
      await audio.play();
      replayCount++;
      playButton.textContent = "Pause audio";
      status.textContent = `Replay ${replayCount}${replayLimit === null ? "" : ` of ${replayLimit}`} is playing.`;
    } catch {
      showAudioError();
    }
  };

  audio.addEventListener("ended", () => {
    firstListenCompleted = true;
    playButton.textContent = "Play audio";
    playButton.disabled = false;
    answerSubmit.disabled = false;
    status.textContent = "Audio finished. You can answer the question or replay the audio.";
    updateReplayButton();
  });

  audio.addEventListener("pause", () => {
    if (!audio.ended && !playButton.disabled) {
      playButton.textContent = "Resume audio";
      updateReplayButton();
    }
  });

  audio.addEventListener("error", showAudioError);
  playButton.addEventListener("click", playAudio);
  replayButton.addEventListener("click", replayAudio);
  answerSubmit.disabled = true;
})();
