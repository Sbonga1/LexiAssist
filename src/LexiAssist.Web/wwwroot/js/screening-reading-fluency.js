(() => {
  const form = document.querySelector("#reading-fluency-activity");
  if (!form) {
    return;
  }

  const startButton = form.querySelector("[data-reading-fluency-start]");
  const finishButton = form.querySelector("[data-reading-fluency-finish]");
  const correctionContainer = form.querySelector("[data-reading-fluency-corrections]");
  const correctionCount = form.querySelector("[name='CorrectionCount']");
  const timer = form.querySelector("[data-reading-fluency-timer]");
  const elapsed = form.querySelector("[data-reading-fluency-elapsed]");
  const status = form.querySelector("[data-reading-fluency-status]");
  const errorMessage = form.querySelector("[data-reading-fluency-error]");
  const antiforgeryToken = form.querySelector('input[name="__RequestVerificationToken"]')?.value;
  const questionToken = form.querySelector('input[name="QuestionToken"]')?.value;

  let runToken = "";
  let startedAt = 0;
  let timerHandle = null;
  let saveInProgress = false;

  const formatElapsed = (milliseconds) => {
    const totalTenths = Math.floor(milliseconds / 100);
    const tenths = totalTenths % 10;
    const totalSeconds = Math.floor(totalTenths / 10);
    const seconds = totalSeconds % 60;
    const minutes = Math.floor(totalSeconds / 60);
    return `${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}.${tenths}`;
  };

  const updateTimer = () => {
    elapsed.textContent = formatElapsed(performance.now() - startedAt);
  };

  const postForm = async (url, values) => {
    const body = new URLSearchParams({
      __RequestVerificationToken: antiforgeryToken,
      ...values
    });

    return fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded;charset=UTF-8" },
      body: body.toString()
    });
  };

  const showError = (message) => {
    errorMessage.textContent = message;
    errorMessage.hidden = false;
    errorMessage.focus();
  };

  const startActivity = async () => {
    startButton.disabled = true;
    errorMessage.hidden = true;
    status.textContent = "Starting the timer…";

    try {
      const response = await postForm(form.dataset.startUrl, { QuestionToken: questionToken });
      if (!response.ok) {
        throw new Error("The activity could not be started. Reload the page and try again.");
      }

      const result = await response.json();
      if (typeof result.runToken !== "string" || result.runToken.length === 0) {
        throw new Error("The activity could not be started. Please contact your support person.");
      }

      runToken = result.runToken;
      startedAt = performance.now();
      startButton.hidden = true;
      timer.hidden = false;
      correctionContainer.hidden = false;
      finishButton.hidden = false;
      finishButton.disabled = false;
      status.textContent = "Timer started. Read aloud at your usual comfortable pace, then finish when you reach the end.";
      updateTimer();
      timerHandle = window.setInterval(updateTimer, 100);
      finishButton.focus();
    } catch (error) {
      startButton.disabled = false;
      status.textContent = "";
      showError(error instanceof Error ? error.message : "The activity could not be started.");
    }
  };

  const finishActivity = async () => {
    if (saveInProgress || !runToken) {
      return;
    }

    saveInProgress = true;
    finishButton.disabled = true;
    errorMessage.hidden = true;
    status.textContent = "Saving your elapsed time and activity details…";

    try {
      const response = await postForm(form.dataset.finishUrl, {
        RunToken: runToken,
        CorrectionCount: correctionCount.value
      });
      if (!response.ok) {
        throw new Error("Your activity could not be saved. Please try again.");
      }

      const result = await response.json();
      if (timerHandle !== null) {
        window.clearInterval(timerHandle);
        timerHandle = null;
      }
      window.location.assign(result.continueUrl || form.dataset.returnUrl);
    } catch (error) {
      saveInProgress = false;
      finishButton.disabled = false;
      status.textContent = "";
      showError(error instanceof Error ? error.message : "Your activity could not be saved.");
    }
  };

  startButton.addEventListener("click", startActivity);
  finishButton.addEventListener("click", finishActivity);
})();
