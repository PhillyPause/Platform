// The action centre's two small helpers, on every page and a no-op on any
// page without them. Event delegation, so enhanced navigation needs nothing
// re-attached.
//
// 1. Copy: a button with data-copy="#id" copies that field's text.
// 2. Email: a link with data-mailto="address" rebuilds its mailto: from the
//    subject and body fields as they are now, so what the reader edited is
//    what their mail app opens. Without script, the link still carries the
//    draft as it was written.

document.addEventListener('click', async (event) => {
  const copy = event.target.closest('[data-copy]');
  if (copy) {
    const field = document.querySelector(copy.dataset.copy);
    if (!field) return;
    const text = 'value' in field ? field.value : field.textContent;
    const label = copy.textContent;
    try {
      await navigator.clipboard.writeText(text);
      copy.textContent = 'Copied';
    } catch {
      // No clipboard (an old browser, or an insecure origin): select it so
      // the reader can copy it themselves.
      if (field.select) field.select();
      copy.textContent = 'Selected: press Ctrl+C';
    }
    setTimeout(() => { copy.textContent = label; }, 2500);
    return;
  }

  const mail = event.target.closest('a[data-mailto]');
  if (mail) {
    const subject = document.querySelector(mail.dataset.subject || '#draft-subject');
    const body = document.querySelector(mail.dataset.body || '#draft-body');
    if (!subject || !body) return;
    mail.href = 'mailto:' + mail.dataset.mailto
      + '?subject=' + encodeURIComponent(subject.value)
      + '&body=' + encodeURIComponent(body.value);
  }
});
