(() => {
    "use strict";

    const activity = document.getElementById("fast-name-race-run");
    const form = document.getElementById("fast-name-race-form");
    const grid = document.getElementById("fast-name-race-grid");
    if (!activity || !form || !grid) return;

    const items = Array.from(grid.querySelectorAll("[data-item-index]"));
    const durationSeconds = Number(activity.dataset.durationSeconds);
    if (items.length === 0 || !Number.isFinite(durationSeconds) || durationSeconds <= 0) return;

    const clock = document.getElementById("fast-name-race-clock");
    const count = document.getElementById("fast-name-race-count");
    const progress = document.getElementById("fast-name-race-progress");
    const currentAnnouncement = document.getElementById("fast-name-race-current");
    const status = document.getElementById("fast-name-race-status");
    const completedInput = document.getElementById("fast-name-race-items-completed");
    const errorsInput = document.getElementById("fast-name-race-error-indexes");
    const durationInput = document.getElementById("fast-name-race-duration");
    const markButton = document.getElementById("fast-name-race-mark-error");
    const nextButton = document.getElementById("fast-name-race-next");
    const pauseButton = document.getElementById("fast-name-race-pause");
    const resumeButton = document.getElementById("fast-name-race-resume");
    const finishButton = document.getElementById("fast-name-race-finish");

    let currentIndex = 0;
    let completedItems = 0;
    const totalDurationMilliseconds = durationSeconds * 1000;
    let remainingMilliseconds = totalDurationMilliseconds;
    let accumulatedActiveMilliseconds = 0;
    let activeSegmentStartedAt = Date.now();
    let timerId = null;
    let paused = false;
    let submitting = false;
    const errorIndexes = new Set();

    const activeElapsedMilliseconds = () => Math.min(
        totalDurationMilliseconds,
        accumulatedActiveMilliseconds + (paused ? 0 : Date.now() - activeSegmentStartedAt));

    const renderClock = () => {
        const seconds = Math.ceil(remainingMilliseconds / 1000);
        const minutesPart = Math.floor(seconds / 60);
        const secondsPart = seconds % 60;
        clock.textContent = `${String(minutesPart).padStart(2, "0")}:${String(secondsPart).padStart(2, "0")}`;
    };

    const renderProgress = () => {
        count.textContent = `${completedItems} of ${items.length}`;
        progress.value = completedItems;
        completedInput.value = String(completedItems);
        errorsInput.value = JSON.stringify(Array.from(errorIndexes).filter(index => index < completedItems).sort((a, b) => a - b));
    };

    const setCurrent = () => {
        if (completedItems < items.length) currentIndex = completedItems;
        items.forEach((item, index) => {
            const isCurrent = index === currentIndex && completedItems < items.length;
            item.setAttribute("aria-current", isCurrent ? "true" : "false");
            item.classList.toggle("is-current", isCurrent);
            item.classList.toggle("is-complete", index < completedItems);
            item.classList.toggle("has-marked-correction", errorIndexes.has(index) && index < completedItems);
        });
        if (completedItems < items.length) {
            currentIndex = completedItems;
            currentAnnouncement.textContent = `Current item ${currentIndex + 1} of ${items.length}: ${items[currentIndex].textContent.trim()}.`;
        }
        markButton.setAttribute("aria-pressed", errorIndexes.has(currentIndex) ? "true" : "false");
        markButton.textContent = errorIndexes.has(currentIndex) ? "Remove correction mark" : "Mark this item for correction";
        renderProgress();
    };

    const stopTimer = () => {
        if (timerId !== null) window.clearInterval(timerId);
        timerId = null;
    };

    const finish = () => {
        if (submitting) return;
        const elapsedMilliseconds = activeElapsedMilliseconds();
        submitting = true;
        stopTimer();
        paused = true;
        durationInput.value = String(Math.min(totalDurationMilliseconds, Math.max(0, Math.round(elapsedMilliseconds))));
        renderProgress();
        markButton.disabled = true;
        nextButton.disabled = true;
        pauseButton.disabled = true;
        resumeButton.disabled = true;
        finishButton.disabled = true;
        status.textContent = "Saving your activity summary…";
        form.requestSubmit();
    };

    const tick = () => {
        const segmentElapsed = Date.now() - activeSegmentStartedAt;
        const elapsed = Math.min(totalDurationMilliseconds, accumulatedActiveMilliseconds + segmentElapsed);
        remainingMilliseconds = Math.max(0, totalDurationMilliseconds - elapsed);
        renderClock();
        if (remainingMilliseconds <= 0) finish();
    };

    const startTimer = () => {
        activeSegmentStartedAt = Date.now();
        timerId = window.setInterval(tick, 200);
        tick();
    };

    markButton.addEventListener("click", () => {
        if (paused || completedItems >= items.length) return;
        if (errorIndexes.has(currentIndex)) errorIndexes.delete(currentIndex);
        else errorIndexes.add(currentIndex);
        setCurrent();
        status.textContent = errorIndexes.has(currentIndex)
            ? "A correction mark is set for this item."
            : "The correction mark has been removed.";
    });

    nextButton.addEventListener("click", () => {
        if (paused || completedItems >= items.length) return;
        completedItems += 1;
        if (completedItems >= items.length) {
            renderProgress();
            finish();
            return;
        }
        setCurrent();
        status.textContent = `Item ${completedItems} completed. Continue when ready.`;
    });

    pauseButton.addEventListener("click", () => {
        if (paused) return;
        tick();
        if (submitting) return;
        accumulatedActiveMilliseconds = activeElapsedMilliseconds();
        stopTimer();
        paused = true;
        pauseButton.hidden = true;
        resumeButton.hidden = false;
        markButton.disabled = true;
        nextButton.disabled = true;
        status.textContent = "Activity paused. Take the time you need.";
    });

    resumeButton.addEventListener("click", () => {
        if (!paused || submitting || remainingMilliseconds <= 0) return;
        paused = false;
        pauseButton.hidden = false;
        resumeButton.hidden = true;
        markButton.disabled = false;
        nextButton.disabled = false;
        status.textContent = "Activity resumed. Continue at a comfortable pace.";
        startTimer();
    });

    finishButton.addEventListener("click", finish);
    form.addEventListener("submit", event => {
        if (!submitting) {
            event.preventDefault();
            finish();
        }
    });

    setCurrent();
    renderClock();
    startTimer();
})();
