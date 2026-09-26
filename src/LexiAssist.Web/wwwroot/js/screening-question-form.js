(() => {
  const selector = document.querySelector("[data-question-type-selector]");
  if (!selector) {
    return;
  }

  const fieldGroups = Array.from(document.querySelectorAll("[data-question-types]"));
  const readingStimulusHelp = document.querySelector("[data-reading-stimulus-help]");

  const updateFieldGroups = () => {
    const selectedType = selector.value;
    if (readingStimulusHelp) {
      readingStimulusHelp.hidden = selectedType !== "3" && selectedType !== "4";
    }

    for (const group of fieldGroups) {
      const supportedTypes = group.dataset.questionTypes.split(",");
      const isVisible = supportedTypes.includes(selectedType);
      group.hidden = !isVisible;

      for (const field of group.querySelectorAll("input, textarea, select")) {
        field.disabled = !isVisible;
      }
    }
  };

  selector.addEventListener("change", updateFieldGroups);
  updateFieldGroups();
})();
