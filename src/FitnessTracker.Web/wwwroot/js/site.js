// Site-wide behaviour. Plain JavaScript, no libraries; every page works without it.

// A form is sent once: a double-click or a second tap is ignored until the next page loads. Without this, a
// double-clicked "Delete location" sends a second delete, which finds nothing and ends on "Page not found", and a
// double-clicked "Add location" tries to add it twice. The buttons are also disabled, to show the form was sent.
document.addEventListener('submit', (event) => {
  const form = event.target;
  if (!(form instanceof HTMLFormElement) || event.defaultPrevented) {
    return;
  }

  if (form.dataset.sent === 'true') {
    event.preventDefault();
    return;
  }

  form.dataset.sent = 'true';
  // After this event, so the browser has already read the form (a disabled button isn't sent).
  setTimeout(() => {
    form.querySelectorAll('button[type="submit"], input[type="submit"]').forEach((button) => {
      button.disabled = true;
    });
  });
});

// The Back button can show a page exactly as it was left, form still marked as sent: make it usable again.
window.addEventListener('pageshow', (event) => {
  if (!event.persisted) {
    return;
  }

  document.querySelectorAll('form[data-sent="true"]').forEach((form) => {
    delete form.dataset.sent;
    form.querySelectorAll('button[type="submit"], input[type="submit"]').forEach((button) => {
      button.disabled = false;
    });
  });
});
