// Set up event handlers
const reconnectModal = document.getElementById("components-reconnect-modal");
reconnectModal.addEventListener(
  "components-reconnect-state-changed",
  handleReconnectStateChanged,
);

const retryButton = document.getElementById("components-reconnect-button");
retryButton.addEventListener("click", retry);

const resumeButton = document.getElementById("components-resume-button");
resumeButton.addEventListener("click", resume);

function handleReconnectStateChanged(event) {
  if (event.detail.state === "show") {
    reconnectModal.showModal();
  } else if (event.detail.state === "hide") {
    reconnectModal.close();
  } else if (event.detail.state === "failed") {
    document.addEventListener(
      "visibilitychange",
      retryWhenDocumentBecomesVisible,
    );
  } else if (event.detail.state === "rejected") {
    location.reload();
  }
}

async function retry() {
  document.removeEventListener(
    "visibilitychange",
    retryWhenDocumentBecomesVisible,
  );

  try {
    const successful = await Blazor.reconnect();
    if (!successful) {
      const resumeSuccessful = await Blazor.resumeCircuit();
      if (!resumeSuccessful) {
        location.reload();
      } else {
        reconnectModal.close();
      }
    }
  } catch (err) {
    document.addEventListener(
      "visibilitychange",
      retryWhenDocumentBecomesVisible,
    );
  }
}

async function resume() {
  try {
    const successful = await Blazor.resumeCircuit();
    if (!successful) {
      location.reload();
    }
  } catch {
    location.reload();
  }
}

async function retryWhenDocumentBecomesVisible() {
  if (document.visibilityState === "visible") {
    await retry();
  }
}
