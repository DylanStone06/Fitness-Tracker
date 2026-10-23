// Site-wide behaviour. Plain JavaScript, no libraries; every page works without it.

// A form is sent once: a double-click or a second tap is ignored for a few seconds, until the next page normally
// loads. Without this, a double-clicked "Delete location" sends a second delete, which finds nothing and ends on
// "Page not found", and a double-clicked "Add location" tries to add it twice. The buttons are also disabled, to
// show the form was sent. If no new page comes (a dropped connection, a file download), the form works again
// after the pause.
const resendAfterMilliseconds = 10000;

function submitButtonsOf(form) {
  return form.querySelectorAll('button[type="submit"], input[type="submit"]');
}

function release(form) {
  delete form.dataset.sent;
  submitButtonsOf(form).forEach((button) => {
    button.disabled = false;
  });
}

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
    submitButtonsOf(form).forEach((button) => {
      button.disabled = true;
    });
  });
  setTimeout(() => release(form), resendAfterMilliseconds);
});

// The Back button can show a page exactly as it was left, form still marked as sent: make it usable again.
window.addEventListener('pageshow', (event) => {
  if (event.persisted) {
    document.querySelectorAll('form[data-sent="true"]').forEach(release);
  }
});

// TRV-1: on a location's form, Monday's opening hours (or "closed") copied to Tuesday to Friday, or to every day, for a
// place open the same hours all week. The days are listed Monday first. The buttons are hidden until this runs.
document.querySelectorAll('[data-copy-monday]').forEach((button) => {
  const form = button.form;
  const status = form?.querySelector('[data-copy-monday-status]');
  const field = (day, name, type = '') => form.querySelector(`input${type}[name="OpeningDays[${day}].${name}"]`);
  button.hidden = false;
  button.addEventListener('click', () => {
    const everyDay = button.dataset.copyMonday === 'every-day';
    for (let day = 1; day <= (everyDay ? 6 : 4); day += 1) {
      field(day, 'Closed', '[type="checkbox"]').checked = field(0, 'Closed', '[type="checkbox"]').checked;
      field(day, 'Opens').value = field(0, 'Opens').value;
      field(day, 'Closes').value = field(0, 'Closes').value;
    }
    if (status) {
      status.textContent = `Monday's hours copied to ${everyDay ? 'every day' : 'Tuesday to Friday'}.`;
    }
  });
});

// ACC-6: on the activity form, the empty getting-ready and changing boxes show the chosen category's defaults (the
// server fills a blank box with them when it saves). The page sets them for the category it opens with; this keeps
// them right when the category changes.
document.querySelectorAll('select[data-buffers]').forEach((select) => {
  const defaults = JSON.parse(select.dataset.buffers);
  const form = select.form;
  const showDefaults = () => {
    const [preparation, changing] = defaults[select.value] ?? [0, 0];
    form?.querySelector('[name="PreparationMinutes"]')?.setAttribute('placeholder', preparation);
    form?.querySelector('[name="ChangingMinutes"]')?.setAttribute('placeholder', changing);
  };
  select.addEventListener('change', showDefaults);
});
