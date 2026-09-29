(() => {
  'use strict';
  const allowed = () => location.origin === 'https://www.maimemo.com' && /^\/home\/login\/?$/.test(location.pathname);
  if (!allowed()) return null;
  if (window.__wordBubbleAccountV3) return window.__wordBubbleAccountV3.read();
  const visible = el => el && el.isConnected && el.getClientRects().length > 0 &&
    getComputedStyle(el).display !== 'none' && getComputedStyle(el).visibility !== 'hidden';
  const epoch = Date.now().toString(36) + '-' + Math.random().toString(36).slice(2, 9);
  let submittedAt = 0;
  let submittedError = null;
  let errorChanged = false;
  const errorObserver = new MutationObserver(() => { errorChanged = true; });
  const fields = () => ({
    account: document.querySelector('#login form.denglu input[type=email]'),
    password: document.querySelector('#login form.denglu input[type=password]'),
    submit: document.querySelector('#login form.denglu #loginBtn'),
    error: document.querySelector('#login form.denglu .error')
  });
  function read() {
    if (!allowed()) return null;
    const f = fields();
    const error = visible(f.error) ? (f.error.textContent || '').trim().slice(0, 240) : '';
    if (errorObserver.takeRecords().length > 0) errorChanged = true;
    // A previous error may remain visible during retry. Only a fresh result may
    // release the new request, including a clear/show cycle between two reads.
    if (error && (errorChanged || f.error !== submittedError)) submittedAt = 0;
    const waiting = submittedAt > 0 && Date.now() - submittedAt < 25000;
    if (!waiting) errorObserver.disconnect();
    if (![f.account, f.password, f.submit].every(visible))
      return { stage: 'page', message: '账号连接页面已变化，请重试连接。', actions: [] };
    // Never return either input value, credentials, cookies or account storage.
    return { stage: 'login', stamp: epoch, message: error || (waiting ? '正在连接你的学习空间…' : ''), waiting,
      actions: waiting ? [] : [{ id: 'login', label: '连接墨墨账号', hint: '' }] };
  }
  function login(command) {
    if (!allowed() || !command || command.stamp !== epoch || read()?.waiting ||
        typeof command.account !== 'string' || typeof command.password !== 'string' ||
        !command.account.trim() || !command.password || command.account.length > 254 || command.password.length > 256) return false;
    const f = fields();
    if (![f.account, f.password, f.submit].every(visible) || f.submit.disabled) return false;
    f.account.value = command.account.trim();
    f.password.value = command.password;
    errorObserver.disconnect();
    submittedError = f.error; errorChanged = false;
    if (f.error) errorObserver.observe(f.error, { attributes: true, childList: true, characterData: true, subtree: true });
    submittedAt = Date.now();
    // Invoke the official form's existing handler; do not reproduce its API.
    try { f.submit.click(); return true; }
    finally { f.password.value = ''; f.account.value = ''; }
  }
  window.__wordBubbleAccountV3 = Object.freeze({ read, login });
  return read();
})();
