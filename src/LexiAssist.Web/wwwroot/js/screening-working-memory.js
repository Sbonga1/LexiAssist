(() => {
    const activity = document.querySelector('[data-working-memory-activity]');
    if (!activity) {
        return;
    }

    const sequence = activity.querySelector('[data-working-memory-sequence]');
    const answer = activity.querySelector('[data-working-memory-answer]');
    const input = activity.querySelector('input[name="TextAnswer"]');
    const submit = activity.querySelector('button[type="submit"]');
    const status = activity.querySelector('[data-working-memory-status]');
    const duration = Math.max(1000, Number.parseInt(activity.dataset.displayDurationMs || '3000', 10));

    window.setTimeout(() => {
        sequence.hidden = true;
        answer.hidden = false;
        input.disabled = false;
        submit.disabled = false;
        status.textContent = 'The sequence is hidden. Enter it in the same order.';
        input.focus();
    }, duration);
})();
