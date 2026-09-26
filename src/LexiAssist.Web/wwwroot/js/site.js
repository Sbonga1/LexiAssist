(() => {
    // Associate server/client validation messages with their controls so a
    // screen reader receives the same error description as a sighted user.
    document.querySelectorAll("[data-valmsg-for]").forEach((message) => {
        const fieldName = message.getAttribute("data-valmsg-for");
        const field = fieldName ? document.getElementById(fieldName) : null;
        if (!field || !message.id) {
            return;
        }

        const describedBy = (field.getAttribute("aria-describedby") || "")
            .split(/\s+/)
            .filter(Boolean);
        if (!describedBy.includes(message.id)) {
            describedBy.push(message.id);
            field.setAttribute("aria-describedby", describedBy.join(" "));
        }
    });

    const preferenceKey = "lexiassist-reading-background";
    const allowedBackgrounds = [
        "standard-light",
        "warm-cream",
        "soft-blue",
        "muted-gray"
    ];

    const selector = document.getElementById("reading-background");
    const root = document.documentElement;
    root.classList.add("js");

    const revealItems = document.querySelectorAll(".landing-reveal");
    if (revealItems.length > 0) {
        const reveal = (item) => {
            const delay = item.dataset.revealDelay;
            if (delay) {
                item.style.transitionDelay = `${delay}ms`;
            }
            item.classList.add("is-visible");
        };

        const reduceMotion = root.dataset.reduceMotion === "true"
            || window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;

        if (reduceMotion || !("IntersectionObserver" in window)) {
            revealItems.forEach(reveal);
        } else {
            const observer = new IntersectionObserver((entries, currentObserver) => {
                entries.forEach((entry) => {
                    if (!entry.isIntersecting) {
                        return;
                    }

                    reveal(entry.target);
                    currentObserver.unobserve(entry.target);
                });
            }, { threshold: 0.14, rootMargin: "0px 0px -8% 0px" });

            revealItems.forEach((item) => observer.observe(item));
        }
    }

    const adminSidebar = document.getElementById("admin-sidebar");
    const adminSidebarToggle = document.getElementById("admin-sidebar-toggle");
    const adminSidebarBackdrop = document.getElementById("admin-sidebar-backdrop");

    if (adminSidebar && adminSidebarToggle && adminSidebarBackdrop) {
        const setAdminSidebarOpen = (isOpen) => {
            document.body.classList.toggle("admin-sidebar-open", isOpen);
            adminSidebarToggle.setAttribute("aria-expanded", String(isOpen));
            adminSidebarToggle.setAttribute("aria-label", isOpen
                ? "Close administrator navigation"
                : "Open administrator navigation");
        };

        adminSidebarToggle.addEventListener("click", () => {
            setAdminSidebarOpen(!document.body.classList.contains("admin-sidebar-open"));
        });
        adminSidebarBackdrop.addEventListener("click", () => setAdminSidebarOpen(false));
        adminSidebar.querySelectorAll("a").forEach((link) => {
            link.addEventListener("click", () => setAdminSidebarOpen(false));
        });
        document.addEventListener("keydown", (event) => {
            if (event.key === "Escape") {
                setAdminSidebarOpen(false);
            }
        });
    }

    const screeningTestReorderPage = document.querySelector("[data-screening-test-reorder]");
    if (screeningTestReorderPage) {
        const tableBody = screeningTestReorderPage.querySelector(".admin-screening-tests-table tbody");
        const reorderForm = document.getElementById("screening-test-reorder-form");
        const reorderId = document.getElementById("screening-test-reorder-id");
        const reorderBeforeId = document.getElementById("screening-test-reorder-before-id");
        const reorderStatus = document.getElementById("screening-test-reorder-status");
        const rows = tableBody
            ? Array.from(tableBody.querySelectorAll("[data-screening-test-row]"))
            : [];
        let draggedRow = null;
        let keyboardRow = null;

        const setStatus = (message) => {
            if (reorderStatus) {
                reorderStatus.textContent = message;
            }
        };

        const setKeyboardRow = (row) => {
            if (keyboardRow && keyboardRow !== row) {
                keyboardRow.querySelector(".admin-screening-tests-drag-handle")?.setAttribute("aria-pressed", "false");
            }

            keyboardRow = row;
            row.querySelector(".admin-screening-tests-drag-handle")?.setAttribute("aria-pressed", "true");
            setStatus(`${row.querySelector(".admin-screening-tests-name")?.textContent.trim() || "Screening test"} grabbed. Use the arrow keys to move it, then press Enter to save.`);
        };

        const submitReorder = (row) => {
            if (!row || !reorderForm || !reorderId || !reorderBeforeId) {
                return;
            }

            reorderId.value = row.dataset.testId || "";
            reorderBeforeId.value = row.nextElementSibling?.dataset.testId || "";
            setStatus("Saving screening test order.");
            reorderForm.submit();
        };

        const clearDragState = () => {
            rows.forEach((row) => row.classList.remove("is-dragging", "is-drag-over"));
            draggedRow = null;
        };

        const moveKeyboardRow = (row, direction) => {
            if (!tableBody) {
                return;
            }

            const sibling = direction === "up" ? row.previousElementSibling : row.nextElementSibling;
            if (!sibling || !sibling.matches("[data-screening-test-row]")) {
                return;
            }

            if (direction === "up") {
                tableBody.insertBefore(row, sibling);
            } else {
                tableBody.insertBefore(sibling, row);
            }

            row.querySelector(".admin-screening-tests-drag-handle")?.focus();
            setStatus(`${row.querySelector(".admin-screening-tests-name")?.textContent.trim() || "Screening test"} moved ${direction}. Press Enter to save.`);
        };

        rows.forEach((row) => {
            const handle = row.querySelector(".admin-screening-tests-drag-handle");
            if (!handle) {
                return;
            }

            handle.addEventListener("dragstart", (event) => {
                draggedRow = row;
                row.classList.add("is-dragging");
                event.dataTransfer?.setData("text/plain", row.dataset.testId || "");
                if (event.dataTransfer) {
                    event.dataTransfer.effectAllowed = "move";
                }
                setStatus(`${row.querySelector(".admin-screening-tests-name")?.textContent.trim() || "Screening test"} grabbed. Drag it to a new position.`);
            });

            handle.addEventListener("dragend", clearDragState);

            handle.addEventListener("keydown", (event) => {
                if (event.key === " " || event.key === "Enter") {
                    event.preventDefault();
                    if (keyboardRow === row) {
                        submitReorder(row);
                    } else {
                        setKeyboardRow(row);
                    }
                    return;
                }

                if (event.key === "Escape" && keyboardRow === row) {
                    event.preventDefault();
                    window.location.reload();
                    return;
                }

                if (keyboardRow !== row) {
                    return;
                }

                if (event.key === "ArrowUp" || event.key === "ArrowDown") {
                    event.preventDefault();
                    moveKeyboardRow(row, event.key === "ArrowUp" ? "up" : "down");
                }
            });
        });

        rows.forEach((row) => {
            row.addEventListener("dragover", (event) => {
                if (!draggedRow || draggedRow === row || !tableBody) {
                    return;
                }

                event.preventDefault();
                const bounds = row.getBoundingClientRect();
                const insertBefore = event.clientY < bounds.top + bounds.height / 2;
                if (insertBefore) {
                    if (row.previousElementSibling !== draggedRow) {
                        tableBody.insertBefore(draggedRow, row);
                    }
                } else if (row.nextElementSibling !== draggedRow) {
                    tableBody.insertBefore(draggedRow, row.nextElementSibling);
                }

                rows.forEach((candidate) => candidate.classList.toggle("is-drag-over", candidate === row));
            });

            row.addEventListener("drop", (event) => {
                if (!draggedRow) {
                    return;
                }

                event.preventDefault();
                const rowToSave = draggedRow;
                clearDragState();
                submitReorder(rowToSave);
            });

            row.addEventListener("dragleave", () => row.classList.remove("is-drag-over"));
        });
    }

    // Authenticated users receive their saved preference from the server-rendered layout.
    if (root.dataset.userAuthenticated === "true") {
        return;
    }

    function applyBackground(preference) {
        const selected = allowedBackgrounds.includes(preference)
            ? preference
            : "standard-light";

        root.dataset.readingBackground = selected;

        if (selector) {
            selector.value = selected;
        }
    }

    let savedPreference = null;
    try {
        savedPreference = window.localStorage.getItem(preferenceKey);
    } catch {
        // The default background remains available when browser storage is disabled.
    }

    applyBackground(savedPreference);

    selector?.addEventListener("change", () => {
        applyBackground(selector.value);

        try {
            window.localStorage.setItem(preferenceKey, selector.value);
        } catch {
            // The selected background still applies for the current page.
        }
    });

})();
