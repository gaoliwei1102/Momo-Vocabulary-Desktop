const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require(process.env.WORDBUBBLE_JSDOM || 'jsdom');
const script = fs.readFileSync(path.join(__dirname, '../../src/WordBubble/Assets/account-adapter.js'), 'utf8');
function setup(url = 'https://www.maimemo.com/home/login?continue=home/web_study') {
  const dom = new JSDOM('<div id="login"><form class="denglu"><input type="email"><input type="password"><button id="loginBtn" type="button">登录</button><p class="error" style="display:none"></p></form></div>', { url, runScripts: 'outside-only' });
  const w = dom.window; w.HTMLElement.prototype.getClientRects = () => [{}]; w.eval(script);
  return { w, api: w.__wordBubbleAccountV3, $: s => w.document.querySelector(s) };
}
test('credentials go only to the existing form handler and are cleared afterwards', () => {
  const { api, $ } = setup(); let submitted;
  $('#loginBtn').onclick = () => submitted = [$('input[type=email]').value, $('input[type=password]').value];
  assert.equal(api.login({ stamp: api.read().stamp, account: 'fixture@example.test', password: 'fixture-only-password' }), true);
  assert.deepEqual(submitted, ['fixture@example.test', 'fixture-only-password']);
  assert.equal($('input[type=password]').value, ''); assert.equal($('input[type=email]').value, '');
  assert.ok(!JSON.stringify(api.read()).includes('fixture'));
});
test('native login never returns credentials that were already in the form', () => {
  const { api, $ } = setup(); $('input[type=email]').value = 'existing@example.test'; $('input[type=password]').value = 'existing-secret';
  const snapshot = JSON.stringify(api.read()); assert.ok(!snapshot.includes('existing'));
});
test('duplicate login is locked until an official error permits explicit retry', () => {
  const { api, $ } = setup(); let clicks = 0; $('#loginBtn').onclick = () => clicks++;
  const command = { stamp: api.read().stamp, account: 'fixture@example.test', password: 'fixture-only-password' };
  assert.equal(api.login(command), true); assert.equal(api.login(command), false); assert.equal(clicks, 1);
  $('.error').style.display = 'block'; $('.error').textContent = '密码错误';
  assert.equal(api.read().message, '密码错误'); assert.equal(api.read().waiting, false);
  assert.equal(api.login(command), true); assert.equal(clicks, 2);
  assert.equal(api.read().waiting, true);
  assert.equal(api.login(command), false); assert.equal(clicks, 2);
});

test('a repeated identical official error unlocks retry even when clearing happened between reads', () => {
  const { api, $ } = setup(); let clicks = 0;
  $('#loginBtn').onclick = () => { clicks++; $('.error').style.display = 'none'; $('.error').textContent = ''; };
  const command = { stamp: api.read().stamp, account: 'fixture@example.test', password: 'fixture-only-password' };
  assert.equal(api.login(command), true);
  $('.error').textContent = '密码错误'; $('.error').style.display = 'block';
  assert.equal(api.read().waiting, false);
  assert.equal(api.login(command), true);
  $('.error').textContent = '密码错误'; $('.error').style.display = 'block';
  assert.equal(api.read().waiting, false); assert.equal(api.read().message, '密码错误');
  assert.equal(api.login(command), true); assert.equal(clicks, 3);
  assert.equal(api.read().waiting, true);
});

test('replacing the official error element permits retry after its new failure', () => {
  const { api, $ } = setup();
  const command = { stamp: api.read().stamp, account: 'fixture@example.test', password: 'fixture-only-password' };
  assert.equal(api.login(command), true);
  $('.error').outerHTML = '<p class="error">连接失败</p>';
  assert.equal(api.read().waiting, false); assert.equal(api.read().message, '连接失败');
  assert.equal(api.login(command), true);
  assert.equal(api.read().waiting, true);
});
test('delivered error mutations unlock only the attempt that received them', async () => {
  const { api, $ } = setup();
  const command = { stamp: api.read().stamp, account: 'fixture@example.test', password: 'fixture-only-password' };
  assert.equal(api.login(command), true);
  $('.error').textContent = '密码错误'; $('.error').style.display = 'block';
  await Promise.resolve();
  assert.equal(api.read().waiting, false);
  assert.equal(api.login(command), true);
  await Promise.resolve();
  assert.equal(api.read().waiting, true); assert.equal(api.login(command), false);
});

test('login only accepts exact official login source, never lookalikes or ports', () => {
  for (const url of ['http://www.maimemo.com/home/login', 'https://www.maimemo.com:444/home/login', 'https://www.maimemo.com.evil.example/home/login', 'https://www.maimemo.com/home/login-extra', 'https://tc-apis.maimemo.com/home/login'])
    assert.equal(setup(url).api, undefined);
});
test('stale commands and absent login controls cannot submit credentials', () => {
  const { api, $ } = setup();
  assert.equal(api.login({ stamp: 'old', account: 'a', password: 'b' }), false);
  const stamp = api.read().stamp; $('#loginBtn').remove();
  assert.equal(api.read().stage, 'page'); assert.equal(api.login({ stamp, account: 'a', password: 'b' }), false);
});
test('login adapter reinjection does not unlock an outstanding request', () => {
  const { api, w } = setup(); api.login({ stamp: api.read().stamp, account: 'a', password: 'b' });
  w.eval(script); assert.equal(w.__wordBubbleAccountV3.read().waiting, true);
});
