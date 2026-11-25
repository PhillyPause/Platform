// The action centre's small helpers, on every page and a no-op on any page
// without them. Event delegation, so enhanced navigation needs nothing
// re-attached.
//
// 1. Copy: a button with data-copy="#id" copies that field's text.
// 2. Email: a link with data-mailto="address" rebuilds its mailto: from the
//    subject and body fields as they are now, so what the reader edited is
//    what their mail app opens. Without script, the link still carries the
//    draft as it was written.
// 3. One message to several (MailMerge.cs): a copy or email link with
//    data-fill='{"salutation":..,"ask":..,"name":..}' fills {salutation},
//    {ask} and {name} for its official first, as MailMerge.Fill does; change
//    the two together. Its row is ticked off, and the line under the list
//    says how many are done and who is next.

const fill = (text, values) => values
  ? text.replace(/\{(salutation|ask|name)\}/gi, (token, word) => values[word.toLowerCase()] ?? token)
  : text;

const valuesOf = (element) => {
  try { return element.dataset.fill ? JSON.parse(element.dataset.fill) : null; } catch { return null; }
};

const opened = (element) => {
  const row = element.closest('[data-merge-row]');
  if (!row) return;
  row.classList.add('is-opened');
  const rows = [...document.querySelectorAll('[data-merge-row]')];
  const done = rows.filter((r) => r.classList.contains('is-opened')).length;
  const next = rows.find((r) => !r.classList.contains('is-opened'));
  const progress = document.querySelector('[data-merge-progress]');
  if (progress) {
    progress.textContent = next
      ? `${done} of ${rows.length} opened. Next: ${next.dataset.mergeName}.`
      : `All ${rows.length} opened. Sent them all? Thank you.`;
  }
};

document.addEventListener('click', async (event) => {
  const copy = event.target.closest('[data-copy]');
  if (copy) {
    const field = document.querySelector(copy.dataset.copy);
    if (!field) return;
    const text = fill('value' in field ? field.value : field.textContent, valuesOf(copy));
    const label = copy.textContent;
    try {
      await navigator.clipboard.writeText(text);
      copy.textContent = 'Copied';
    } catch {
      // No clipboard (an old browser, or an insecure origin): select it so
      // the reader can copy it themselves. A merge's tokens stay unfilled
      // in the field, so say so.
      if (field.select) field.select();
      copy.textContent = valuesOf(copy) ? 'Selected: copy it, and fill in the braces' : 'Selected: press Ctrl+C';
    }
    opened(copy);
    setTimeout(() => { copy.textContent = label; }, 2500);
    return;
  }

  const mail = event.target.closest('a[data-mailto]');
  if (mail) {
    const subject = document.querySelector(mail.dataset.subject || '#draft-subject');
    const body = document.querySelector(mail.dataset.body || '#draft-body');
    if (!subject || !body) return;
    const values = valuesOf(mail);
    mail.href = 'mailto:' + mail.dataset.mailto
      + '?subject=' + encodeURIComponent(fill(subject.value, values))
      + '&body=' + encodeURIComponent(fill(body.value, values));
    opened(mail);
  }
});
