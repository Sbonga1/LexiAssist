(() => {
    const processingSpeed = document.querySelector('[data-processing-speed-activity]');
    if (processingSpeed) {
        initializeProcessingSpeed(processingSpeed);
        return;
    }

    const activity = document.querySelector('[data-timed-matching-activity]');
    if (!activity) {
        return;
    }

    const remaining = activity.querySelector('[data-timed-matching-remaining]');
    const timeoutInput = activity.querySelector('[data-timed-matching-timeout]');
    const choices = activity.querySelectorAll('input[name="SelectedAnswer"]');
    const limitSeconds = Math.max(1, Number.parseInt(activity.dataset.timeLimitSeconds || '30', 10));
    const startedAt = performance.now();
    let timerId;

    const finishOnTimeout = () => {
        timeoutInput.value = 'true';
        choices.forEach(choice => { choice.required = false; });
        activity.submit();
    };

    const updateTimer = () => {
        const elapsedSeconds = Math.floor((performance.now() - startedAt) / 1000);
        const secondsLeft = Math.max(0, limitSeconds - elapsedSeconds);
        remaining.textContent = secondsLeft.toString();
        if (secondsLeft <= 0) {
            window.clearInterval(timerId);
            finishOnTimeout();
        }
    };

    timerId = window.setInterval(updateTimer, 250);
    updateTimer();
})();

function initializeProcessingSpeed(activity) {
    const remaining = activity.querySelector('[data-processing-speed-remaining]');
    const status = activity.querySelector('[data-processing-speed-status]');
    const item = activity.querySelector('[data-processing-speed-item]');
    const itemValues = Array.from(activity.querySelectorAll('[data-processing-speed-item-value]'))
        .map(value => value.textContent.trim());
    const choices = Array.from(activity.querySelectorAll('input[name="TimedMatchingCurrentAnswer"]'));
    const answersInput = activity.querySelector('[data-processing-speed-answers]');
    const timeoutInput = activity.querySelector('[data-processing-speed-timeout]');
    const next = activity.querySelector('[data-processing-speed-next]');
    const submit = activity.querySelector('[data-processing-speed-submit]');
    const limitSeconds = Math.max(1, Number.parseInt(activity.dataset.timeLimitSeconds || '30', 10));
    const startedAt = performance.now();
    const answers = [];
    let currentIndex = 0;
    let timerId;
    let finished = false;

    const renderItem = () => {
        item.textContent = itemValues[currentIndex] || '';
        status.textContent = `Item ${Math.min(currentIndex + 1, itemValues.length)} of ${itemValues.length}`;
        choices.forEach(choice => { choice.checked = false; });
        next.disabled = true;
        if (choices[0]) {
            choices[0].focus();
        }
    };

    const persistAnswers = () => {
        answersInput.value = JSON.stringify(answers);
    };

    const finish = (timedOut) => {
        if (finished) {
            return;
        }

        finished = true;
        window.clearInterval(timerId);
        persistAnswers();
        timeoutInput.value = timedOut ? 'true' : 'false';
        choices.forEach(choice => { choice.disabled = true; });
        next.disabled = true;
        submit.click();
    };

    const commitCurrentAnswer = () => {
        const selected = choices.find(choice => choice.checked);
        if (!selected || currentIndex >= itemValues.length) {
            return false;
        }

        answers.push(selected.value);
        persistAnswers();
        if (answers.length >= itemValues.length) {
            finish(false);
            return true;
        }

        currentIndex += 1;
        renderItem();
        return true;
    };

    choices.forEach(choice => {
        choice.addEventListener('change', () => {
            next.disabled = false;
        });
    });
    next.addEventListener('click', commitCurrentAnswer);

    const finishOnTimeout = () => {
        commitCurrentAnswer();
        finish(true);
    };

    const updateTimer = () => {
        const elapsedSeconds = Math.floor((performance.now() - startedAt) / 1000);
        const secondsLeft = Math.max(0, limitSeconds - elapsedSeconds);
        remaining.textContent = secondsLeft.toString();
        if (secondsLeft <= 0) {
            finishOnTimeout();
        }
    };

    renderItem();
    timerId = window.setInterval(updateTimer, 250);
    updateTimer();
}
