(() => {
  const list = document.getElementById("sound-builder-chunks");
  const orderField = document.getElementById("ordered-chunk-indexes");
  const orderStatus = document.getElementById("sound-builder-order-status");
  if (!list || !orderField) return;

  let draggedChunk = null;

  const chunks = () => Array.from(list.querySelectorAll(".sound-builder-chunk"));
  const chunkText = (chunk) => chunk?.querySelector(".sound-builder-chunk-text")?.textContent?.trim() || "Chunk";

  const updateMoveButtons = () => {
    const items = chunks();
    items.forEach((chunk, index) => {
      chunk.querySelector('[data-move="previous"]')?.toggleAttribute("disabled", index === 0);
      chunk.querySelector('[data-move="next"]')?.toggleAttribute("disabled", index === items.length - 1);
    });
  };

  const saveOrder = () => {
    orderField.value = chunks().map((chunk) => chunk.dataset.index).join(",");
    updateMoveButtons();
  };

  const moveChunk = (chunk, direction) => {
    if (!chunk) return;
    const sibling = direction === "previous" ? chunk.previousElementSibling : chunk.nextElementSibling;
    if (!sibling) return;

    if (direction === "previous") {
      list.insertBefore(chunk, sibling);
    } else {
      list.insertBefore(sibling, chunk);
    }

    saveOrder();
    chunk.focus();
    if (orderStatus) orderStatus.textContent = `${chunkText(chunk)} moved ${direction === "previous" ? "earlier" : "later"}.`;
  };

  list.addEventListener("click", (event) => {
    const button = event.target.closest("button[data-move]");
    if (!button) return;
    event.preventDefault();
    moveChunk(button.closest(".sound-builder-chunk"), button.dataset.move);
  });

  list.addEventListener("keydown", (event) => {
    const chunk = event.target.closest(".sound-builder-chunk");
    if (!chunk || (event.key !== "ArrowLeft" && event.key !== "ArrowRight")) return;
    event.preventDefault();
    moveChunk(chunk, event.key === "ArrowLeft" ? "previous" : "next");
  });

  list.addEventListener("dragstart", (event) => {
    draggedChunk = event.target.closest(".sound-builder-chunk");
    if (!draggedChunk) return;
    event.dataTransfer.effectAllowed = "move";
    event.dataTransfer.setData("text/plain", draggedChunk.dataset.index);
  });

  list.addEventListener("dragover", (event) => {
    if (!draggedChunk) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = "move";
  });

  list.addEventListener("drop", (event) => {
    const target = event.target.closest(".sound-builder-chunk");
    if (!draggedChunk || !target || target === draggedChunk) return;
    event.preventDefault();
    const insertAfter = event.clientY > target.getBoundingClientRect().top + target.offsetHeight / 2;
    list.insertBefore(draggedChunk, insertAfter ? target.nextElementSibling : target);
    saveOrder();
    if (orderStatus) orderStatus.textContent = `${chunkText(draggedChunk)} moved.`;
  });

  list.addEventListener("dragend", () => {
    draggedChunk = null;
    saveOrder();
  });

  document.getElementById("sound-builder-form")?.addEventListener("submit", saveOrder);
  saveOrder();
})();
