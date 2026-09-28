(() => {
    const rootSelector = '[data-assessment-builder-root]';
    const reviewSelector = '[data-draft-review]';
    const formSelector = '[data-draft-form]';
    const feedback = (root, message, kind = 'info') => {
        const node = root?.querySelector('[data-draft-feedback], [data-builder-feedback]');
        if (!node) return;
        node.className = `alert alert-${kind}`;
        node.textContent = message;
        node.hidden = false;
        node.tabIndex = -1;
        node.focus({ preventScroll: true });
    };
    const parseHtml = html => new DOMParser().parseFromString(html, 'text/html');
    const requestHeaders = { 'X-Requested-With': 'XMLHttpRequest', Accept: 'application/json, text/html' };

    const rows = review => [...review.querySelectorAll('[data-draft-row]')];
    const allBoxes = review => [...review.querySelectorAll('input[name="SelectedIndexes"]')];
    const activeIndex = review => Number(review.querySelector('[data-active-question-index]')?.value || 0);
    const activeEditor = review => review.querySelector(`[data-draft-editor="${activeIndex(review)}"]`);
    const draftForm = review => review.querySelector(formSelector);

    const filteredRows = review => rows(review).filter(row => !row.hidden);
    const updateSelection = review => {
        const boxes = allBoxes(review);
        const selected = boxes.filter(box => box.checked && !box.disabled).length;
        const visibleBoxes = filteredRows(review).map(row => row.querySelector('input[name="SelectedIndexes"]')).filter(box => box && !box.disabled);
        const visibleSelected = visibleBoxes.filter(box => box.checked).length;
        const selectAll = review.querySelector('[data-select-all]');
        if (selectAll) {
            selectAll.checked = visibleBoxes.length > 0 && visibleSelected === visibleBoxes.length;
            selectAll.indeterminate = visibleSelected > 0 && visibleSelected < visibleBoxes.length;
            selectAll.disabled = visibleBoxes.length === 0;
        }
        const filter = review.querySelector('[data-type-filter]');
        const isFiltered = filter && filter.value !== 'all';
        const scope = review.querySelector('[data-selection-scope]');
        if (scope) scope.textContent = isFiltered ? 'visible drafts of this type' : 'drafts';
        const totalSelected = review.querySelector('[data-selected-drafts]');
        if (totalSelected) totalSelected.textContent = String(selected);
        const count = review.querySelector('[data-selection-count]');
        if (count) count.textContent = isFiltered ? `${visibleSelected} of ${visibleBoxes.length} visible selected · ${selected} total selected` : `${selected} of ${boxes.length} selected`;
        const actionCount = review.querySelector('[data-action-selected-count]');
        if (actionCount) actionCount.textContent = String(selected);
        const add = review.querySelector('[data-add-selected]');
        if (add) {
            add.textContent = `Add ${selected} question${selected === 1 ? '' : 's'}`;
            add.disabled = selected === 0 || review.dataset.adding === 'true';
        }
    };

    const updateFilter = review => {
        const filter = review.querySelector('[data-type-filter]');
        const filteredType = filter?.value || 'all';
        rows(review).forEach(row => { row.hidden = filteredType !== 'all' && row.dataset.draftType !== filteredType; });
        const activeRow = rows(review).find(row => row.dataset.draftIndex === String(activeIndex(review)));
        const note = review.querySelector('[data-active-filter-note]');
        if (note) note.hidden = !activeRow?.hidden;
        updateSelection(review);
    };

    const setActiveQuestion = (review, index, focus = true) => {
        const form = draftForm(review);
        if (!form) return;
        form.querySelector('[data-active-question-index]').value = String(index);
        review.querySelectorAll('[data-draft-editor]').forEach(editor => { editor.hidden = editor.dataset.draftEditor !== String(index); });
        review.querySelectorAll('[data-draft-index]').forEach(button => button.setAttribute('aria-current', String(button.dataset.draftIndex === String(index))));
        const selected = review.querySelector(`[data-draft-index="${index}"]`);
        selected?.scrollIntoView({ block: 'nearest' });
        if (focus) activeEditor(review)?.querySelector('textarea, input:not([type="hidden"]), select, button')?.focus({ preventScroll: true });
        updateFilter(review);
    };

    const valueSnapshot = form => [...new FormData(form).entries()]
        .filter(([key]) => key !== '__RequestVerificationToken' && key !== 'Version' && key !== 'ActiveQuestionIndex' && key !== 'draftAction')
        .map(([key, value]) => [key, value instanceof File ? `${value.name}:${value.size}` : String(value)]);
    const markSaved = (review, version) => {
        const form = draftForm(review);
        if (version !== undefined) {
            form.querySelector('[data-draft-version]').value = String(version);
            review.querySelector('[data-discard-version]').value = String(version);
            review.querySelector('[data-regenerate-version]').value = String(version);
        }
        review.dataset.savedSnapshot = JSON.stringify(valueSnapshot(form));
        review.dataset.dirty = 'false';
        const status = review.querySelector('[data-save-status]');
        if (status) status.textContent = 'All draft changes are saved.';
    };
    const isDirty = review => review.dataset.dirty === 'true' || review.dataset.savedSnapshot !== JSON.stringify(valueSnapshot(draftForm(review)));
    const setDirtyState = review => {
        const dirty = isDirty(review);
        review.dataset.dirty = String(dirty);
        const status = review.querySelector('[data-save-status]');
        if (status) status.textContent = dirty ? 'Unsaved draft changes' : 'All draft changes are saved.';
    };

    const describeError = async response => {
        const text = await response.text();
        try {
            const parsed = JSON.parse(text);
            return parsed.error || parsed.message || `The request failed (${response.status}).`;
        } catch {
            const page = parseHtml(text);
            return page.querySelector('.alert-danger, .alert-warning')?.textContent.trim() || `The request failed (${response.status}).`;
        }
    };

    const renderReturnedReview = (review, html) => {
        const page = parseHtml(html);
        const replacement = page.querySelector(reviewSelector);
        if (!replacement) return false;
        review.replaceWith(replacement);
        initializeReview(replacement);
        replacement.querySelector('[role="alert"]')?.focus({ preventScroll: true });
        return true;
    };

    const validateQuestion = (editor) => {
        const errors = [];
        const field = name => editor.querySelector(`[name="${name}"]`);
        const prompt = field(`Questions[${editor.dataset.draftEditor}].Prompt`)?.value.trim() || '';
        const index = editor.dataset.draftEditor;
        const q = suffix => field(`Questions[${index}].${suffix}`);
        const type = Number(q('QuestionType')?.value);
        const options = [...editor.querySelectorAll('[data-choice-row]')].map(row => row.querySelector('[data-choice-text]')?.value.trim() || '');
        const integer = (name, min, max, label, required = false) => {
            const input = q(name);
            if (!input) return;
            if (!input.value && !required) return;
            const value = Number(input.value);
            if (!Number.isInteger(value) || value < min || value > max) errors.push(`Enter ${label} from ${min} to ${max}${required ? '' : ' or leave it blank'}.`);
        };
        if (prompt.length < 3 || prompt.length > 1000) errors.push('Enter a prompt between 3 and 1,000 characters.');
        const weight = Number(q('Weight')?.value);
        if (!Number.isFinite(weight) || weight < 0.1 || weight > 100) errors.push('Enter a question weight from 0.1 to 100.');
        if ([1, 5, 13].includes(type) || (type === 9 && q('ListeningResponseType')?.value === 'multiple-choice')) {
            const nonempty = options.filter(Boolean);
            if (nonempty.length < 2 || nonempty.length > 10 || new Set(nonempty.map(value => value.toLocaleLowerCase())).size !== nonempty.length) errors.push('Enter 2 to 10 different answer options.');
            const correct = editor.querySelector(`[name="Questions[${index}].CorrectChoiceId"]:checked`)?.value;
            const ids = [...editor.querySelectorAll('[data-choice-id]')].map(input => input.value);
            if (!correct || !ids.includes(correct) || !options[ids.indexOf(correct)]) errors.push('Select the correct answer from the options.');
        }
        const correctAnswer = q('CorrectAnswer')?.value.trim() || '';
        const accepted = (q('AcceptedAnswersText')?.value || '').split(/\r?\n/).map(value => value.trim()).filter(Boolean);
        const textAnswer = [2, 3, 4, 7, 8, 10, 11].includes(type) || (type === 9 && q('ListeningResponseType')?.value === 'text-response');
        if (textAnswer && !correctAnswer && accepted.length === 0) errors.push('Enter a correct answer or at least one accepted answer.');
        if (accepted.length > 10 || accepted.some(value => value.length > 300)) errors.push('Use no more than 10 accepted answers, each 300 characters or fewer.');
        if ([8, 10].includes(type) && !(q('PassageText')?.value.trim())) errors.push('Enter the reading passage.');
        if (type === 10) integer('TargetWordsPerMinute', 1, 1000, 'a target pace');
        if (type === 6) {
            if ((q('TimedNamingItemsText')?.value || '').split(/\r?\n/).map(x => x.trim()).filter(Boolean).length !== 20) errors.push('Enter exactly 20 naming items.');
            integer('TimeLimitSeconds', 1, 600, 'a time limit', true);
        }
        if (type === 11) {
            const sequence = (q('WorkingMemorySequenceText')?.value || '').split(/\r?\n/).map(x => x.trim()).filter(Boolean);
            if (sequence.length < 2 || sequence.length > 12) errors.push('Enter 2 to 12 sequence items.');
            integer('MemoryDisplayDurationMilliseconds', 1000, 30000, 'a display duration', true);
        }
        if (type === 12) {
            const mapping = new Map();
            for (const line of (q('TimedMatchingReferenceMappingText')?.value || '').split(/\r?\n/).map(x => x.trim()).filter(Boolean)) {
                const pair = line.split('=', 2).map(value => value.trim());
                if (pair.length === 2 && pair[0] && pair[1] && !mapping.has(pair[0].toLocaleLowerCase())) mapping.set(pair[0].toLocaleLowerCase(), pair[1]);
            }
            const items = (q('TimedMatchingItemsText')?.value || '').split(/\r?\n/).map(x => x.trim()).filter(Boolean);
            if (mapping.size < 2) errors.push('Enter at least two reference pairs in item = symbol format.');
            if (!items.length || items.some(item => !mapping.has(item.toLocaleLowerCase()))) errors.push('Enter matching items that appear in the reference mapping.');
            integer('TimeLimitSeconds', 1, 600, 'a time limit', true);
        }
        if (type === 13) integer('PatternDifficultyLevel', 1, 5, 'a pattern difficulty', true);
        if (type === 9) {
            const audioPath = q('AudioPath')?.value.trim() || '';
            if (!/^[^/\\\\:\u0000-\u001f]{1,255}\.(aac|m4a|mp3|ogg|wav|webm)$/i.test(audioPath)) errors.push('Choose a supported listening audio file name.');
            const responseType = q('ListeningResponseType')?.value;
            if (responseType === 'audio-response' && !q('AllowsAudioRecording')?.checked) errors.push('Allow learner audio recording for an audio response.');
            if (responseType === 'text-response' && !correctAnswer) errors.push('Enter the expected spoken response.');
            integer('MaxAudioReplays', 0, 20, 'a replay limit');
        }
        const imagePath = q('ImagePath')?.value.trim() || '';
        if (imagePath && !(/^(https?:\/\/[^\s]+)$/i.test(imagePath) || (/^\/(?!\/)[^\\]*$/.test(imagePath)))) errors.push('Use a safe image URL or an application image path.');
        return errors;
    };

    const validateSelected = review => {
        const selected = allBoxes(review).filter(box => box.checked && !box.disabled);
        let firstInvalid = null;
        for (const box of selected) {
            const editor = review.querySelector(`[data-draft-editor="${box.value}"]`);
            if (!editor) continue;
            const errors = validateQuestion(editor);
            showQuestionErrors(editor, errors);
            if (errors.length && firstInvalid === null) firstInvalid = { box, editor };
        }
        if (!firstInvalid) return true;
        const filter = review.querySelector('[data-type-filter]');
        if (filter?.value !== 'all') { filter.value = 'all'; updateFilter(review); }
        setActiveQuestion(review, firstInvalid.box.value);
        firstInvalid.editor.querySelector('[data-question-errors]')?.focus();
        return false;
    };
    const showQuestionErrors = (editor, errors) => {
        const box = editor.querySelector('[data-question-errors]');
        if (!box) return;
        box.replaceChildren();
        if (!errors.length) { box.hidden = true; return; }
        const title = document.createElement('strong');
        title.textContent = 'Review this question:';
        const list = document.createElement('ul');
        errors.forEach(error => { const item = document.createElement('li'); item.textContent = error; list.append(item); });
        box.append(title, list);
        box.hidden = false;
    };

    const saveDraft = async (review, { silent = false } = {}) => {
        if (review.dataset.adding === 'true') return false;
        const form = draftForm(review);
        if (form.dataset.pending === 'true') return false;
        form.dataset.pending = 'true';
        const button = review.querySelector('[data-save-draft]');
        if (button) { button.disabled = true; button.setAttribute('aria-busy', 'true'); }
        if (!silent) {
            const status = review.querySelector('[data-save-status]');
            if (status) status.textContent = 'Saving draft changes…';
        }
        try {
            const response = await fetch(form.action, { method: 'POST', credentials: 'same-origin', headers: requestHeaders, body: new FormData(form) });
            const contentType = response.headers.get('content-type') || '';
            if (contentType.includes('application/json') && response.ok) {
                const result = await response.json();
                markSaved(review, result.version);
                return true;
            }
            const body = await response.text();
            if (!renderReturnedReview(review, body)) feedback(review, (await describeError(new Response(body, { status: response.status }))), 'danger');
            return false;
        } catch {
            feedback(review, 'Draft changes were not confirmed as saved. Your edits are still here; retry Save changes.', 'danger');
            return false;
        } finally {
            if (form.isConnected) form.dataset.pending = 'false';
            if (button?.isConnected) { button.disabled = false; button.removeAttribute('aria-busy'); }
        }
    };

    const doAdd = async (review, submitter) => {
        if (review.dataset.adding === 'true' || !validateSelected(review)) return;
        const form = draftForm(review);
        review.dataset.adding = 'true';
        const buttons = [...review.querySelectorAll('[data-add-selected], [data-save-draft]')];
        const add = review.querySelector('[data-add-selected]');
        buttons.forEach(button => { button.disabled = true; button.setAttribute('aria-busy', 'true'); });
        feedback(review, 'Adding the selected questions to this assessment…');
        try {
            const data = new FormData(form);
            if (submitter?.name) data.append(submitter.name, submitter.value);
            const response = await fetch(submitter?.formAction || form.action, { method: 'POST', credentials: 'same-origin', headers: requestHeaders, body: data });
            const contentType = response.headers.get('content-type') || '';
            if (contentType.includes('application/json')) {
                const result = await response.json();
                if (response.ok && result.redirectUrl) {
                    feedback(review, result.message || `Added ${result.added} questions.`, 'success');
                    window.location.assign(result.redirectUrl);
                    return;
                }
                const version = result.version;
                if (version !== undefined) markSaved(review, version);
                const invalid = result.invalidIndexes || [];
                if (invalid.length) {
                    for (const rawIndex of invalid) {
                        const editor = review.querySelector(`[data-draft-editor="${rawIndex}"]`);
                        const serverErrors = result.errors?.[rawIndex] || result.errors?.[String(rawIndex)];
                        if (editor) showQuestionErrors(editor, serverErrors?.length ? serverErrors : validateQuestion(editor));
                    }
                    const index = Number(invalid[0]);
                    const filter = review.querySelector('[data-type-filter]');
                    if (filter?.value !== 'all') { filter.value = 'all'; updateFilter(review); }
                    setActiveQuestion(review, index);
                    review.querySelector(`#draft-error-${index}`)?.focus({ preventScroll: false });
                }
                feedback(review, result.message || result.error || `Could not add questions (${response.status}). Correct the highlighted drafts and retry.`, response.status >= 500 ? 'danger' : 'warning');
                return;
            }
            const body = await response.text();
            if (!renderReturnedReview(review, body)) feedback(review, (await describeError(new Response(body, { status: response.status }))), 'danger');
        } catch {
            feedback(review, 'The server did not confirm whether these questions were added. Retry Add; the saved draft version prevents duplicate additions.', 'danger');
        } finally {
            if (review.isConnected) review.dataset.adding = 'false';
            buttons.forEach(button => { if (button.isConnected) { button.removeAttribute('aria-busy'); button.disabled = false; } });
            if (add?.isConnected) updateSelection(review);
        }
    };

    const navigateAfterUnsaved = (review, continuation) => {
        if (!isDirty(review)) { continuation(); return; }
        const dialog = review.querySelector('[data-unsaved-dialog]');
        review.dataset.pendingNavigation = 'true';
        review._continueNavigation = continuation;
        if (dialog?.showModal) dialog.showModal();
        else if (window.confirm('Discard unsaved draft changes?')) continuation();
    };

    const regenerate = async (review, index) => {
        const form = review.querySelector('#regenerate-draft-form');
        const button = review.querySelector(`[data-regenerate-question="${index}"]`);
        if (!form || button?.disabled) return;
        form.querySelector('[data-regenerate-index]').value = String(index);
        button.disabled = true;
        button.setAttribute('aria-busy', 'true');
        feedback(review, 'Regenerating this question. The saved version stays available until a replacement succeeds…');
        try {
            const response = await fetch(form.action, { method: 'POST', credentials: 'same-origin', headers: requestHeaders, body: new FormData(form) });
            if (!response.ok) { feedback(review, await describeError(response), 'danger'); return; }
            const result = await response.json();
            window.location.assign(result.redirectUrl);
        } catch {
            feedback(review, 'Regeneration was not confirmed. Your saved draft is unchanged; try again.', 'danger');
        } finally {
            if (button.isConnected) { button.disabled = false; button.removeAttribute('aria-busy'); }
        }
    };

    const initializeReview = review => {
        if (review.dataset.initialized === 'true') return;
        review.dataset.initialized = 'true';
        updateFilter(review);
        const navigator = review.querySelector('[data-draft-navigator]');
        const navigatorToggle = review.querySelector('[data-toggle-draft-navigator]');
        const navigatorOpen = window.matchMedia('(max-width: 650px)').matches;
        navigator?.classList.toggle('is-open', navigatorOpen);
        navigatorToggle?.setAttribute('aria-expanded', String(navigatorOpen));
        review.querySelectorAll('[data-draft-editor]').forEach(editor => {
            const prompt = editor.querySelector('[data-prompt-field]');
            const count = editor.querySelector('[data-prompt-count]');
            if (prompt && count) count.textContent = `${prompt.value.length}/${prompt.maxLength}`;
        });
        const form = draftForm(review);
        if (form) {
            markSaved(review);
            form.addEventListener('input', () => setDirtyState(review));
            form.addEventListener('change', event => {
                if (event.target.matches('[data-select-all]')) return;
                updateSelection(review);
                setDirtyState(review);
            });
        }
        review.addEventListener('change', event => {
            if (event.target.matches('[data-type-filter]')) updateFilter(review);
            const changedEditor = event.target.closest('[data-draft-editor]');
            const changedErrors = changedEditor?.querySelector('[data-question-errors]');
            if (changedEditor && changedErrors && !changedErrors.hidden) showQuestionErrors(changedEditor, validateQuestion(changedEditor));
            if (event.target.matches('[data-select-all]')) {
                filteredRows(review).forEach(row => {
                    const box = row.querySelector('input[name="SelectedIndexes"]');
                    if (box && !box.disabled) box.checked = event.target.checked;
                });
                updateSelection(review);
                setDirtyState(review);
            }
        });
        review.addEventListener('input', event => {
            const editor = event.target.closest('[data-draft-editor]');
            if (!editor) return;
            const rowText = review.querySelector(`[data-draft-index="${editor.dataset.draftEditor}"] .draft-question-excerpt`);
            const prompt = editor.querySelector(`[name="Questions[${editor.dataset.draftEditor}].Prompt"]`);
            if (rowText && prompt) rowText.textContent = prompt.value.trim() || 'Untitled draft';
            const promptCount = editor.querySelector('[data-prompt-count]');
            if (prompt && promptCount) promptCount.textContent = `${prompt.value.length}/${prompt.maxLength}`;
            const errors = editor.querySelector('[data-question-errors]');
            if (errors && !errors.hidden) showQuestionErrors(editor, validateQuestion(editor));
        });
    };

    document.querySelectorAll(reviewSelector).forEach(initializeReview);
    window.addEventListener('beforeunload', event => {
        const review = document.querySelector(reviewSelector);
        if (review && isDirty(review)) { event.preventDefault(); event.returnValue = ''; }
    });

    document.addEventListener('submit', async event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement)) return;
        const review = form.closest(reviewSelector);
        if (review && form.matches(formSelector)) {
            event.preventDefault();
            const submitter = event.submitter;
            if (submitter?.value === 'add') { await doAdd(review, submitter); return; }
            await saveDraft(review);
            return;
        }

        const root = form.closest(rootSelector);
        if (!root || form.dataset.allowPageNavigation === 'true' || form.dataset.pending === 'true' || !form.matches('.assessment-builder-ai-form, .assessment-builder-import-form')) return;
        if (form.matches('#discard-draft-form, #regenerate-draft-form')) return;
        if (!['POST', 'GET'].includes(form.method.toUpperCase())) return;
        event.preventDefault();
        form.dataset.pending = 'true';
        const submitter = event.submitter;
        const isImport = form.matches('.assessment-builder-import-form');
        const isGenerate = form.matches('.assessment-builder-ai-form');
        const isBank = form.matches('.assessment-builder-bank-search');
        if (isImport) {
            const file = form.querySelector('[name="csvFile"]')?.files?.[0];
            if (!file || !/\.csv$/i.test(file.name) || file.size === 0 || file.size > 8 * 1024 * 1024) {
                form.dataset.pending = 'false';
                feedback(root, 'Choose a non-empty .csv file up to 8 MB. XLSX is not supported.', 'danger');
                return;
            }
        }
        if (submitter) { submitter.disabled = true; submitter.setAttribute('aria-busy', 'true'); }
        if (isGenerate && submitter) submitter.textContent = 'Generating questions…';
        root.setAttribute('aria-busy', 'true');
        feedback(root, isGenerate ? 'Generating questions. This may take a little while…' : isImport ? 'Checking this CSV on your device. Uploading only after size and type validation…' : isBank ? 'Searching the question bank…' : 'Saving…');
        try {
            const options = { method: form.method.toUpperCase(), credentials: 'same-origin', headers: requestHeaders };
            let url = form.action;
            const data = new FormData(form);
            if (options.method === 'GET') {
                const target = new URL(url, location.href);
                for (const [key, value] of data) target.searchParams.set(key, String(value));
                url = target;
            } else {
                if (submitter?.name && !data.has(submitter.name)) data.append(submitter.name, submitter.value);
                if (isImport) {
                    await compressCsv(data, form);
                    feedback(root, data.get('csvCompressed') === 'true'
                        ? 'The CSV passed local checks and was losslessly compressed. Uploading it for server validation…'
                        : 'The CSV passed local checks. Keeping the original file and uploading it for server validation…');
                }
                options.body = data;
            }
            const response = await fetch(url, options);
            const body = await response.text();
            const returned = parseHtml(body);
            if (isImport && response.ok && returned.querySelector(reviewSelector)) {
                window.location.assign(response.url);
                return;
            }
            const error = returned.querySelector('.alert-danger, .alert-warning');
            if (error) {
                feedback(root, error.textContent.trim(), 'danger');
                if (isGenerate && submitter) submitter.textContent = 'Retry generation';
                return;
            }
            const updated = returned.querySelector(rootSelector);
            if (updated) {
                root.replaceWith(updated);
                document.title = returned.title || document.title;
                updated.querySelector('[role="status"], [role="alert"]')?.focus({ preventScroll: true });
            } else if (!response.ok) {
                feedback(root, `The request failed (${response.status}). Check your connection and retry.`, 'danger');
                if (isGenerate && submitter) submitter.textContent = 'Retry generation';
            } else {
                feedback(root, 'The request completed but the workspace could not be refreshed. Reload the page to see the saved result.', 'warning');
                if (isGenerate && submitter) submitter.textContent = 'Retry generation';
            }
        } catch {
            feedback(root, 'The request could not be confirmed. Check your connection and try again.', 'danger');
            if (isGenerate && submitter) submitter.textContent = 'Retry generation';
        } finally {
            if (form.isConnected) form.dataset.pending = 'false';
            if (submitter?.isConnected) { submitter.disabled = false; submitter.removeAttribute('aria-busy'); }
            root.removeAttribute('aria-busy');
        }
    });

    document.addEventListener('click', event => {
        const target = event.target instanceof Element ? event.target : null;
        if (!target) return;
        const review = target.closest(reviewSelector);
        if (review) {
            const questionButton = target.closest('.draft-select-question');
            if (questionButton) {
                setActiveQuestion(review, Number(questionButton.dataset.draftIndex));
                review.querySelector('.draft-review-main')?.classList.add('is-mobile-editing');
                if (window.matchMedia('(min-width: 651px) and (max-width: 1000px)').matches) {
                    review.querySelector('[data-draft-navigator]')?.classList.remove('is-open');
                    review.querySelector('[data-toggle-draft-navigator]')?.setAttribute('aria-expanded', 'false');
                }
                return;
            }
            if (target.closest('[data-back-to-drafts]')) { review.querySelector('.draft-review-main')?.classList.remove('is-mobile-editing'); review.querySelector('.draft-select-question[aria-current="true"]')?.focus(); return; }
            if (target.closest('[data-clear-filter]')) { review.querySelector('[data-type-filter]').value = 'all'; updateFilter(review); return; }
            if (target.closest('[data-toggle-draft-navigator]')) {
                const nav = review.querySelector('[data-draft-navigator]');
                const open = nav.classList.toggle('is-open');
                target.closest('[data-toggle-draft-navigator]').setAttribute('aria-expanded', String(open));
                return;
            }
            const move = target.closest('[data-move-choice]');
            if (move) {
                const row = move.closest('[data-choice-row]');
                const sibling = move.dataset.moveChoice === 'up' ? row.previousElementSibling : row.nextElementSibling;
                if (sibling) { move.dataset.moveChoice === 'up' ? sibling.before(row) : sibling.after(row); reindexOptions(row.closest('[data-draft-options]')); setDirtyState(review); }
                return;
            }
            if (target.closest('[data-remove-choice]')) {
                const row = target.closest('[data-choice-row]');
                row.remove();
                reindexOptions(review.querySelector(`[data-draft-editor="${activeIndex(review)}"] [data-draft-options]`));
                setDirtyState(review);
                return;
            }
            if (target.closest('[data-add-choice]')) { addChoice(activeEditor(review)); setDirtyState(review); return; }
            const regen = target.closest('[data-regenerate-question]');
            if (regen) {
                navigateAfterUnsaved(review, () => regenerate(review, Number(regen.dataset.regenerateQuestion)));
                return;
            }
            if (target.closest('[data-discard-drafts]')) {
                navigateAfterUnsaved(review, () => review.querySelector('[data-discard-dialog]').showModal());
                return;
            }
            const unsavedAction = target.closest('[data-unsaved-action]');
            if (unsavedAction) {
                const dialog = review.querySelector('[data-unsaved-dialog]');
                const action = unsavedAction.dataset.unsavedAction;
                const proceed = review._continueNavigation;
                dialog.close();
                review.dataset.pendingNavigation = 'false';
                review._continueNavigation = null;
                if (action === 'cancel') return;
                if (action === 'discard') {
                    // beforeunload also compares the form to its saved snapshot. Move
                    // that baseline to the explicitly discarded values so this
                    // deliberate navigation is not blocked by the browser prompt.
                    review.dataset.savedSnapshot = JSON.stringify(valueSnapshot(draftForm(review)));
                    review.dataset.dirty = 'false';
                    proceed?.();
                } else saveDraft(review).then(saved => { if (saved) proceed?.(); });
                return;
            }
            const discardAction = target.closest('[data-discard-action]');
            if (discardAction) {
                const dialog = review.querySelector('[data-discard-dialog]');
                dialog.close();
                if (discardAction.dataset.discardAction === 'confirm') {
                    const form = review.querySelector('#discard-draft-form');
                    fetch(form.action, { method: 'POST', credentials: 'same-origin', headers: requestHeaders, body: new FormData(form) })
                        .then(response => { if (response.redirected) location.assign(response.url); else if (!response.ok) return describeError(response).then(message => feedback(review, message, 'danger')); else location.assign(response.url); })
                        .catch(() => feedback(review, 'Drafts were not confirmed as discarded. Try again.', 'danger'));
                }
                return;
            }
            const guard = target.closest('[data-guard-unsaved]');
            if (guard) { event.preventDefault(); navigateAfterUnsaved(review, () => location.assign(guard.href)); return; }
        }

        const root = target.closest(rootSelector);
        if (root) {
            const focus = target.closest('[data-focus-mode]');
            if (focus) {
                const enabled = document.body.classList.toggle('is-workspace-focus');
                focus.setAttribute('aria-pressed', String(enabled));
                const label = focus.querySelector('[data-focus-label]');
                if (label) label.textContent = enabled ? 'Exit focus mode' : 'Focus mode';
                else focus.textContent = enabled ? 'Exit focus mode' : 'Focus mode';
                return;
            }
            const panelControl = target.closest('[data-toggle-panel], [data-close-panel]');
            if (panelControl) {
                const id = panelControl.dataset.togglePanel || panelControl.dataset.closePanel;
                const panel = root.querySelector(`#${id}`);
                const open = id === 'assessment-outline' ? panel.classList.contains('is-open') : !panel.hidden;
                if (open || panelControl.dataset.closePanel) {
                    id === 'assessment-outline' ? panel.classList.remove('is-open') : panel.hidden = true;
                    root.querySelector(`[data-toggle-panel="${id}"]`)?.setAttribute('aria-expanded', 'false');
                    root.querySelector(`[data-toggle-panel="${id}"]`)?.focus();
                } else {
                    id === 'assessment-outline' ? panel.classList.add('is-open') : panel.hidden = false;
                    panelControl.setAttribute('aria-expanded', 'true');
                    panel.querySelector('button, a, input, select')?.focus();
                }
            }
        }
    });

    const reindexOptions = list => {
        if (!list) return;
        [...list.querySelectorAll('[data-choice-row]')].forEach((row, index) => {
            const editor = row.closest('[data-draft-editor]');
            const qIndex = editor.dataset.draftEditor;
            const choice = row.querySelector('[data-choice-text]');
            const choiceId = row.querySelector('[data-choice-id]');
            const correct = row.querySelector('[data-correct-choice]');
            const label = row.querySelector('label.visually-hidden');
            const letter = row.querySelector('[data-option-letter]');
            const id = `question-${qIndex}-choice-${index}`;
            choice.name = `Questions[${qIndex}].Choices[${index}]`; choice.id = id;
            choiceId.name = `Questions[${qIndex}].ChoiceIds[${index}]`;
            correct.name = `Questions[${qIndex}].CorrectChoiceId`;
            correct.setAttribute('aria-label', `Mark option ${index + 1} as correct`);
            if (letter) letter.textContent = String.fromCharCode(65 + index);
            label.htmlFor = id; label.textContent = `Answer option ${index + 1}`;
            row.querySelector('[data-move-choice="up"]').disabled = index === 0;
            row.querySelector('[data-move-choice="down"]').disabled = index === list.querySelectorAll('[data-choice-row]').length - 1;
        });
    };
    const addChoice = editor => {
        const list = editor?.querySelector('[data-draft-options]');
        if (!list || list.children.length >= 10) return;
        const row = document.createElement('div'); row.className = 'draft-option-row'; row.dataset.choiceRow = '';
        const qIndex = editor.dataset.draftEditor;
        const id = crypto.randomUUID().replaceAll('-', '');
        row.innerHTML = `<input type="hidden" data-choice-id value="${id}"><input type="radio" data-correct-choice value="${id}"><span class="draft-option-letter" data-option-letter aria-hidden="true"></span><div class="draft-option-content"><label class="visually-hidden">Answer option</label><input data-choice-text maxlength="300"></div><button type="button" class="draft-option-action" data-move-choice="up" aria-label="Move option up">↑</button><button type="button" class="draft-option-action" data-move-choice="down" aria-label="Move option down">↓</button><button type="button" class="draft-option-action" data-remove-choice aria-label="Remove answer option">Remove</button>`;
        list.append(row); reindexOptions(list); row.querySelector('[data-choice-text]').focus();
    };

    const compressCsv = async (formData, form) => {
        const input = form.querySelector('[name="csvFile"]');
        const file = input?.files?.[0];
        if (!file || file.size < 256 * 1024 || typeof CompressionStream === 'undefined') return;
        try {
            const stream = new Blob([await file.arrayBuffer()]).stream().pipeThrough(new CompressionStream('gzip'));
            const bytes = await new Response(stream).arrayBuffer();
            if (bytes.byteLength < file.size) {
                formData.set('csvFile', new File([bytes], file.name, { type: file.type || 'text/csv' }));
                formData.set('csvCompressed', 'true');
            }
        } catch { /* Retain the original file when local gzip is unavailable. */ }
    };
})();
