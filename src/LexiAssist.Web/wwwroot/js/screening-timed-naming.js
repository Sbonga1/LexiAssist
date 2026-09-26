(() => {
  const form = document.querySelector("#timed-naming-activity");
  if (!form) {
    return;
  }

  const startButton = form.querySelector("[data-timed-naming-start]");
  const nextButton = form.querySelector("[data-timed-naming-next]");
  const errorCheckbox = form.querySelector("[data-timed-naming-error]");
  const grid = form.querySelector("[data-timed-naming-grid]");
  const timer = form.querySelector("[data-timed-naming-timer]");
  const elapsed = form.querySelector("[data-timed-naming-elapsed]");
  const progressText = form.querySelector("[data-timed-naming-progress]");
  const progressBar = form.querySelector("[data-timed-naming-progress-bar]");
  const progressFill = form.querySelector("[data-timed-naming-progress-fill]");
  const controls = form.querySelector("[data-timed-naming-controls]");
  const instructions = form.querySelector("[data-timed-naming-instructions]");
  const status = form.querySelector("[data-timed-naming-status]");
  const errorMessage = form.querySelector("[data-timed-naming-error-message]");
  const antiforgeryToken = form.querySelector('input[name="__RequestVerificationToken"]')?.value;
  const questionToken = form.querySelector('input[name="QuestionToken"]')?.value;

  let items = [];
  let runToken = "";
  let currentIndex = 0;
  let selfReportedErrorIndexes = [];
  let startedAt = 0;
  let timeLimitMilliseconds = 0;
  let timerHandle = null;
  let currentItemRecorded = false;
  let activityTimedOut = false;
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
    const elapsedMilliseconds = performance.now() - startedAt;
    elapsed.textContent = formatElapsed(Math.min(elapsedMilliseconds, timeLimitMilliseconds));
    if (elapsedMilliseconds >= timeLimitMilliseconds && !activityTimedOut) {
      activityTimedOut = true;
      if (timerHandle !== null) {
        window.clearInterval(timerHandle);
        timerHandle = null;
      }
      errorCheckbox.disabled = true;
      nextButton.textContent = "Save completed items";
      status.textContent = "Time is up. Saving the items completed so far…";
      finishActivity(currentIndex);
    }
  };

  const renderGrid = () => {
    grid.replaceChildren();
    items.forEach((item, index) => {
      const cell = document.createElement("li");
      cell.className = "timed-naming-item";
      cell.textContent = item;
      cell.setAttribute("aria-label", `Item ${index + 1}: ${item}`);
      grid.append(cell);
    });
    updateCurrentItem();
  };

  const updateCurrentItem = () => {
    const cells = Array.from(grid.children);
    cells.forEach((cell, index) => {
      const isCurrent = index === currentIndex && currentIndex < items.length;
      const isError = selfReportedErrorIndexes.includes(index) || (isCurrent && errorCheckbox.checked);
      cell.classList.toggle("is-current", isCurrent);
      cell.classList.toggle("is-error", isError);
      if (isCurrent) {
        cell.setAttribute("aria-current", "step");
      } else {
        cell.removeAttribute("aria-current");
      }
    });

    if (currentIndex < items.length) {
      progressText.textContent = `Item ${currentIndex + 1} of ${items.length}`;
      progressBar.setAttribute("aria-valuenow", String(currentIndex));
      progressBar.setAttribute("aria-valuetext", `${currentIndex} of ${items.length} items completed`);
      progressFill.style.width = `${(currentIndex / items.length) * 100}%`;
      nextButton.textContent = currentIndex === items.length - 1 ? "Finish and save" : "Next item";
    }
  };

  const announceCurrentItem = () => {
    status.textContent = currentIndex < items.length
      ? `Item ${currentIndex + 1} of ${items.length}: ${items[currentIndex]}. Name it aloud, then continue.`
      : "All items are complete. Saving your activity…";
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

  const showFailure = (message) => {
    errorMessage.textContent = message;
    errorMessage.hidden = false;
    errorMessage.focus();
    controls.hidden = false;
    nextButton.disabled = false;
  };

  const startActivity = async () => {
    startButton.disabled = true;
    status.textContent = "Preparing the randomized item order…";
    errorMessage.hidden = true;

    try {
      const response = await postForm(form.dataset.startUrl, { QuestionToken: questionToken });
      if (!response.ok) {
        throw new Error("The activity could not be started. Reload the page and try again.");
      }

      const result = await response.json();
      if (!Array.isArray(result.items) || result.items.length !== 20 || typeof result.runToken !== "string" ||
          !Number.isInteger(result.timeLimitSeconds) || result.timeLimitSeconds < 1 || result.timeLimitSeconds > 600) {
        throw new Error("The activity items could not be loaded. Please contact your support person.");
      }

      items = result.items;
      runToken = result.runToken;
      timeLimitMilliseconds = result.timeLimitSeconds * 1000;
      currentIndex = 0;
      selfReportedErrorIndexes = [];
      activityTimedOut = false;
      saveInProgress = false;
      startedAt = performance.now();
      renderGrid();

      instructions.hidden = true;
      timer.hidden = false;
      progressText.hidden = false;
      progressBar.hidden = false;
      grid.hidden = false;
      controls.hidden = false;
      announceCurrentItem();
      updateTimer();
      timerHandle = window.setInterval(updateTimer, 100);
      nextButton.focus();
    } catch (error) {
      startButton.disabled = false;
      status.textContent = "";
      errorMessage.textContent = error instanceof Error ? error.message : "The activity could not be started.";
      errorMessage.hidden = false;
    }
  };

  const finishActivity = async (completedCount) => {
    if (saveInProgress) {
      return;
    }
    saveInProgress = true;
    nextButton.disabled = true;
    status.textContent = "Saving your activity…";
    errorMessage.hidden = true;

    try {
      const response = await postForm(form.dataset.finishUrl, {
        RunToken: runToken,
        CompletedOrderJson: JSON.stringify(Array.from({ length: completedCount }, (_, index) => index)),
        SelfReportedErrorIndexesJson: JSON.stringify(selfReportedErrorIndexes)
      });

      if (!response.ok) {
        throw new Error("Your activity could not be saved. Please try saving again.");
      }

      const result = await response.json();
      if (timerHandle !== null) {
        window.clearInterval(timerHandle);
        timerHandle = null;
      }
      window.location.assign(result.continueUrl || form.dataset.returnUrl);
    } catch (error) {
      saveInProgress = false;
      status.textContent = "";
      showFailure(error instanceof Error ? error.message : "Your activity could not be saved.");
      if (activityTimedOut || currentIndex >= items.length) {
        nextButton.textContent = "Retry save";
      }
    }
  };

  const advance = async () => {
    if (nextButton.disabled || saveInProgress) {
      return;
    }
    if (activityTimedOut) {
      await finishActivity(currentIndex);
      return;
    }
    if (currentIndex >= items.length) {
      await finishActivity(items.length);
      return;
    }
    nextButton.disabled = true;
    errorMessage.hidden = true;
    if (!currentItemRecorded) {
      if (errorCheckbox.checked) {
        selfReportedErrorIndexes.push(currentIndex);
      }
      currentItemRecorded = true;
      updateCurrentItem();
    }

    if (currentIndex === items.length - 1) {
      currentIndex = items.length;
      errorCheckbox.disabled = true;
      await finishActivity(items.length);
      return;
    }

    currentIndex++;
    currentItemRecorded = false;
    errorCheckbox.checked = false;
    updateCurrentItem();
    announceCurrentItem();
    nextButton.disabled = false;
    nextButton.focus();
  };

  startButton.addEventListener("click", startActivity);
  nextButton.addEventListener("click", advance);
  errorCheckbox.addEventListener("change", updateCurrentItem);
})();
