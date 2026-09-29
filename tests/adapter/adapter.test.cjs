const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require(process.env.WORDBUBBLE_JSDOM || 'jsdom');
const adapter = fs.readFileSync(path.join(__dirname, '../../src/WordBubble/Assets/study-adapter.js'), 'utf8');

// Synthetic DOM follows the site's public rendered class names. No account, website
// code, network or browser profile is used. jsdom does not do physical layout.
function setup({ answer = false, url = 'https://tc-apis.maimemo.com/webstudy/app' } = {}) {
  const dom = new JSDOM(`<!doctype html><style>.hidden { display:none }</style><div class="rev-root">
    <div class="rev-top"><div class="rev-top-stats"><span>00:20</span><span>12/80</span></div>
      <div class="spelling">serendipity</div><div class="phonetic">英 /ˌserənˈdɪpəti/</div>
      <div class="interp-ctn" ${answer ? '' : 'hidden'}>n. 意外发现美好事物的运气</div></div>
    ${answer ? '' : '<div class="rev-recall-mask">点击屏幕显示答案</div>'}
    <div class="rev-scroller"><div class="phrase-root"><div class="phrase-phrase">A moment of serendipity.</div><div class="phrase-interpretation">一次美好的偶遇。</div></div></div>
    <div class="verify-input hidden"></div>
    <div class="rev-resp-btns"><taro-button-core class="reset-button" disabled="false">认识<span class="predict">4天</span></taro-button-core>
      <taro-button-core class="reset-button" disabled="false">模糊</taro-button-core><taro-button-core class="reset-button" disabled="false">忘记</taro-button-core></div></div>`, { url, runScripts: 'outside-only' });
  const w = dom.window;
  w.HTMLElement.prototype.getClientRects = function () { return [{ width: 100, height: 30 }]; };
  w.eval(adapter);
  const api = w.__wordBubbleStudyV3;
  const click = id => api.act({ id, stamp: api.read().stamp });
  return { w, api, click, $: selector => w.document.querySelector(selector) };
}

test('recall exposes word and progress without hidden meaning or feedback', () => {
  const { api } = setup(); const s = api.read();
  assert.equal(s.stage, 'recall'); assert.equal(s.word, 'serendipity'); assert.equal(s.progress, '12/80');
  assert.equal(s.meaning, ''); assert.equal(s.examples.length, 0);
  assert.equal(Object.hasOwn(s, 'example'), false); assert.equal(Object.hasOwn(s, 'translation'), false);
  assert.deepEqual(Array.from(s.actions, a => a.id), ['audio', 'reveal']);
});
test('one reveal per unchanged page; official transition unlocks answer', () => {
  const { api, click, $ } = setup(); let count = 0;
  $('.rev-recall-mask').onclick = () => count++;
  const before = api.read(); assert.equal(click('reveal').accepted, true);
  assert.equal(click('reveal').accepted, false); assert.equal(count, 1); assert.equal(api.read().waiting, true);
  $('.rev-recall-mask').remove(); $('.interp-ctn').hidden = false;
  const answer = api.read(); assert.equal(answer.stage, 'answer'); assert.equal(answer.waiting, false);
  assert.notEqual(answer.stamp, before.stamp); assert.ok(answer.meaning.includes('意外'));
});
test('answer copies official labels, predicted interval and available example', () => {
  const { api, $ } = setup({ answer: true });
  const buttons = $('.rev-resp-btns').children; buttons[1].textContent = '不确定'; buttons[2].textContent = '不认识';
  const s = api.read(); assert.equal(s.actions.find(a => a.id === 'vague').label, '不确定');
  assert.equal(s.actions.find(a => a.id === 'familiar').hint, '4天');
  assert.deepEqual(Array.from(s.examples, example => example.text), ['A moment of serendipity.']);
});
test('rapid feedback and progress-only updates cannot submit twice', () => {
  const { api, click, $ } = setup({ answer: true }); let count = 0;
  $('.reset-button').onclick = () => count++;
  assert.equal(click('familiar').accepted, true);
  $('.rev-top-stats').lastElementChild.textContent = '13/80';
  assert.equal(click('familiar').accepted, false); assert.equal(click('forget').accepted, false); assert.equal(count, 1);
  assert.equal(api.read().waiting, true);
});
test('temporarily disabling feedback does not unlock a pending click', () => {
  const { api, click, $ } = setup({ answer: true });
  click('familiar'); $('.reset-button').setAttribute('disabled', 'true'); api.read();
  $('.reset-button').setAttribute('disabled', 'false');
  assert.equal(api.read().waiting, true); assert.equal(click('familiar').accepted, false);
});
test('stale snapshot cannot rate a new word', () => {
  const { api, $ } = setup({ answer: true }); const stamp = api.read().stamp;
  $('.spelling').textContent = 'another';
  assert.equal(api.act({ id: 'familiar', stamp }).accepted, false);
});
test('replacing equivalent response DOM cannot unlock duplicate feedback', () => {
  const { api, click, $ } = setup({ answer: true }); click('familiar');
  const responses = $('.rev-resp-btns'); responses.replaceWith(responses.cloneNode(true));
  assert.equal(api.read().waiting, true); assert.equal(click('familiar').accepted, false);
});
test('disabled native buttons and aria-disabled targets cannot be invoked', () => {
  const { api, click, $ } = setup({ answer: true });
  $('.reset-button').outerHTML = '<button class="reset-button" disabled="false">认识</button>';
  assert.equal(click('familiar').accepted, false);
  $('.reset-button').removeAttribute('disabled'); $('.reset-button').setAttribute('aria-disabled', 'true');
  assert.equal(api.read().actions.some(a => a.id === 'familiar'), false);
});
test('modal prevents feedback from reaching the page behind it', () => {
  const { api, click, w } = setup({ answer: true });
  w.document.body.insertAdjacentHTML('beforeend', '<div role="dialog">请确认</div>');
  assert.equal(api.read().stage, 'blocked'); assert.equal(click('familiar').accepted, false);
});
test('CSS-disabled controls cannot be invoked programmatically', () => {
  const { click, $ } = setup({ answer: true });
  $('.reset-button').style.pointerEvents = 'none';
  assert.equal(click('familiar').accepted, false);
});
test('spelling uses its visible prompt and input without revealing the word', () => {
  const { api, $ } = setup(); assert.equal(api.read().stage, 'recall');
  $('.verify-input').classList.remove('hidden'); $('.verify-input').innerHTML = '<input type="text">';
  $('.spelling').remove(); $('.interp-ctn').hidden = false;
  assert.equal(api.read().stage, 'spelling'); assert.equal(api.read().word, '');
  assert.ok(api.read().meaning.includes('意外'));
  let value = ''; $('.verify-input input').addEventListener('input', e => value = e.target.value);
  assert.equal(api.act({ id: 'spelling-input', stamp: api.read().stamp, value: 'serendipity' }).accepted, true);
  assert.equal(value, 'serendipity');
});
test('login, lookalike domains and unrelated paths never install adapter', () => {
  for (const url of ['https://www.maimemo.com/login', 'https://tc-apis.maimemo.com.evil.example/webstudy/app', 'https://tc-apis.maimemo.com:444/webstudy/app', 'https://tc-apis.maimemo.com/webstudy/apple'])
    assert.equal(setup({ url }).api, undefined);
});
test('changed labels and missing controls fall back without proxying unknown actions', () => {
  const { api, click, $ } = setup({ answer: true });
  $('.reset-button').textContent = '删除'; assert.equal(api.read().stage, 'page'); assert.equal(click('familiar').accepted, false);
  $('.reset-button').remove(); assert.equal(api.read().stage, 'page');
});
test('hidden inactive study roots are ignored', () => {
  const { api, $, w } = setup(); const copy = $('.rev-root').cloneNode(true);
  copy.classList.add('hidden'); copy.querySelector('.spelling').textContent = 'hiddenword';
  w.document.body.prepend(copy); assert.equal(api.read().word, 'serendipity');
});
test('round completion makes no claim of daily completion or server acknowledgement', () => {
  const { api, $, w } = setup(); $('.rev-root').remove();
  w.document.body.insertAdjacentHTML('beforeend', '<div class="finished-status">完成</div>');
  assert.equal(api.read().stage, 'complete'); assert.match(api.read().message, /这一轮/); assert.equal(api.read().actions.length, 0);
});
test('audio is throttled but does not consume a feedback action', () => {
  const { api, click } = setup({ answer: true });
  assert.equal(click('audio').accepted, true); assert.equal(click('audio').accepted, false);
  assert.equal(api.read().waiting, false); assert.equal(click('familiar').accepted, true);
});
test('same document reinjection preserves the pending feedback lock', () => {
  const { api, click, w } = setup({ answer: true }); click('familiar');
  w.eval(adapter); assert.equal(api.read().waiting, true); assert.equal(click('forget').accepted, false);
});
test('official dialog choices are copied and forwarded only after explicit selection', () => {
  const { api, w, click } = setup({ answer: true }); let count = 0;
  w.document.body.insertAdjacentHTML('beforeend', '<div class="taroify-dialog"><div class="taroify-dialog__content">新增例句发音功能</div><button>不再提示</button><button>查看设置</button></div>');
  const dialog = w.document.querySelector('.taroify-dialog'); dialog.querySelector('button').onclick = () => count++;
  const s = api.read(); assert.equal(s.stage, 'blocked'); assert.equal(s.message, '新增例句发音功能');
  assert.equal(s.actions[0].label, '不再提示'); assert.equal(count, 0);
  assert.equal(click('dialog-0').accepted, true); assert.equal(count, 1); assert.equal(click('dialog-0').accepted, false);
});
test('changed dialog text invalidates an old confirmation', () => {
  const { api, w } = setup();
  w.document.body.insertAdjacentHTML('beforeend', '<div role="dialog"><p>原来的提示</p><button>确定</button></div>');
  const stamp = api.read().stamp; w.document.querySelector('[role=dialog] p').textContent = '另一条提示';
  assert.equal(api.act({ id: 'dialog-0', stamp }).accepted, false);
});
test('official Taro modal blocks the card and forwards its visible div choices', () => {
  const { api, w, click, $ } = setup({ answer: true });
  let feedback = 0, confirmations = 0, cancellations = 0;
  $('.reset-button').onclick = () => feedback++;
  // Taro showModal uses taro__modal and taro-model__btn, including the model typo.
  w.document.body.insertAdjacentHTML('beforeend', `<div class="taro__modal" style="opacity:1">
    <div class="taro-modal__mask"></div><div class="taro-modal__content">
      <div class="taro-modal__title">错误</div><div class="taro-modal__text">连接暂时中断，请重试</div>
      <div class="taro-modal__foot"><div class="taro-model__btn taro-model__cancel" style="display:none">取消</div>
        <div class="taro-model__btn taro-model__confirm">重试</div></div></div></div>`);
  $('.taro-model__confirm').onclick = () => confirmations++;
  $('.taro-model__cancel').onclick = () => cancellations++;
  const state = api.read();
  assert.equal(state.stage, 'blocked');
  assert.equal(state.message, '错误\n连接暂时中断，请重试');
  assert.deepEqual(Array.from(state.actions, action => action.label), ['重试']);
  assert.equal(click('familiar').accepted, false); assert.equal(feedback, 0);
  assert.equal(confirmations, 0); assert.equal(cancellations, 0);
  assert.equal(click(state.actions[0].id).accepted, true); assert.equal(confirmations, 1);
  assert.equal(click(state.actions[0].id).accepted, false); assert.equal(confirmations, 1);
  $('.taro__modal').style.opacity = '0';
  assert.equal(api.read().stage, 'answer');
  $('.taro__modal').style.display = 'none'; $('.taro__modal').style.opacity = '1';
  assert.equal(api.read().stage, 'answer');
});

test('reused official Taro modal invalidates stale choice and preserves both labels', () => {
  const { api, w, click, $ } = setup({ answer: true });
  w.document.body.insertAdjacentHTML('beforeend', `<div class="taro__modal"><div class="taro-modal__content">
    <div class="taro-modal__text">第一次确认</div><div class="taro-modal__foot">
      <div class="taro-model__btn taro-model__cancel">取消</div>
      <div class="taro-model__btn taro-model__confirm">确定</div></div></div></div>`);
  let cancellations = 0; $('.taro-model__cancel').onclick = () => cancellations++;
  const before = api.read();
  assert.deepEqual(Array.from(before.actions, action => action.label), ['取消', '确定']);
  $('.taro-modal__text').textContent = '另一条需要用户确认的提示';
  assert.equal(api.act({ id: before.actions[1].id, stamp: before.stamp }).accepted, false);
  assert.equal(click('dialog-0').accepted, true); assert.equal(cancellations, 1);
});

test('completion requires explicit continue or sign-in, and never auto-selects words', () => {
  const { api, w, $, click } = setup(); $('.rev-root').remove();
  w.document.body.insertAdjacentHTML('beforeend', '<div class="finished-status"><div class="finished-review-more">还有一些待复习单词</div><button>继续学习</button><button>签到</button><button>自动选词</button></div>');
  let count = 0; w.document.querySelector('.finished-status button').onclick = () => count++;
  assert.deepEqual(Array.from(api.read().actions, a => a.id), ['continue', 'sign-in']); assert.equal(count, 0);
  assert.equal(click('continue').accepted, true); assert.equal(count, 1);
});
test('additional review exposes the official chosen quantity before confirmation', () => {
  const { api, w, $ } = setup(); $('.rev-root').remove();
  w.document.body.insertAdjacentHTML('beforeend', '<div class="finished-status"><div class="finished-input"><input value="20"></div><button>确定</button></div>');
  assert.equal(api.read().actions[0].label, '继续复习 20 词');
});

test('official entry checking and login redirect spinners remain loading', () => {
  for (const message of ['正在检查登录状态', '正在前往登录']) {
    const { api, w, $ } = setup(); $('.rev-root').remove();
    w.document.body.insertAdjacentHTML('beforeend', `<taro-view-core class="index"><taro-view-core class="index__access">
      <taro-view-core class="index__access-status"><div class="taroify-loading"></div></taro-view-core>
      <taro-text-core class="index__access-message">${message}</taro-text-core></taro-view-core></taro-view-core>`);
    const state = api.read(); assert.equal(state.stage, 'loading'); assert.equal(state.message, message);
    assert.equal(state.actions.length, 0);
  }
});

test('official access denial preserves the automatic sync requirement', () => {
  const { api, w, $ } = setup(); $('.rev-root').remove();
  w.document.body.insertAdjacentHTML('beforeend', '<div class="index"><div class="index__access"><div class="index__access-status">请先开启自动同步</div><div class="index__access-message">使用网页版需要先在 App 内开启学习数据自动同步，开启后请刷新页面重试</div></div></div>');
  const state = api.read(); assert.equal(state.stage, 'blocked');
  assert.equal(state.message, '请先开启自动同步\n使用网页版需要先在 App 内开启学习数据自动同步，开启后请刷新页面重试');
  assert.equal(state.actions.length, 0);
});

test('entry failures forward only known explicit retry or login choices', () => {
  for (const [label, id] of [['重试检查', 'entry-retry'], ['重新登录', 'entry-login']]) {
    const { api, w, $, click } = setup(); $('.rev-root').remove();
    w.document.body.insertAdjacentHTML('beforeend', `<div class="index"><div class="index__access"><div class="index__access-status">检查失败</div><div class="index__access-message">暂时无法检查登录状态，请重试</div><div class="index__access-actions"><button class="index__action-btn">${label}</button><button>未知操作</button></div></div></div>`);
    let count = 0; $('.index__action-btn').onclick = () => count++;
    const state = api.read(); assert.equal(state.stage, 'blocked');
    assert.deepEqual(Array.from(state.actions, a => a.id), [id]); assert.equal(count, 0);
    assert.equal(click(id).accepted, true); assert.equal(count, 1); assert.equal(click(id).accepted, false);
  }
});

test('entry consent requires explicit label selection before starting study', () => {
  const { api, w, $, click } = setup(); $('.rev-root').remove();
  w.document.body.insertAdjacentHTML('beforeend', `<taro-view-core class="index">
    <taro-text-core>墨墨背单词 · 网页版公测说明</taro-text-core>
    <taro-text-core>一、测试须知\n当前功能不保证持续提供。\n二、测试准备\n请开启学习数据自动同步。</taro-text-core>
    <taro-view-core class="index__terms-row"><label class="index__checkbox"><input type="checkbox" class="index__native-checkbox" style="opacity:0"><span>我已阅读测试须知、</span></label><a class="index__link" href="https://www.maimemo.com/terms">《用户协议》</a>和<a class="index__link" href="https://www.maimemo.com/privcay">《隐私政策》</a></taro-view-core>
    <button class="index__action-btn">开始学习</button></taro-view-core>`);
  let consentChanges = 0, starts = 0;
  $('.index__native-checkbox').onchange = () => consentChanges++;
  $('.index__action-btn').onclick = () => starts++;
  const before = api.read();
  assert.equal(before.stage, 'blocked'); assert.match(before.message, /二、测试准备/);
  assert.match(before.message, /我已阅读测试须知、《用户协议》和《隐私政策》/);
  assert.match(before.message, /https:\/\/www.maimemo.com\/terms/);
  assert.deepEqual(Array.from(before.actions, a => a.id), ['entry-consent']);
  assert.equal(consentChanges, 0); assert.equal(starts, 0); assert.equal(click('entry-start').accepted, false);
  assert.equal(click('entry-consent').accepted, true); assert.equal(consentChanges, 1);
  assert.equal($('.index__native-checkbox').checked, true);
  assert.equal(api.read().actions.find(a => a.id === 'entry-consent').label, '取消同意');
  assert.equal(api.act({ id: 'entry-start', stamp: before.stamp }).accepted, false);
  assert.equal(click('entry-start').accepted, true); assert.equal(starts, 1);
  assert.equal(click('entry-start').accepted, false);
});

test('revoking entry consent removes the start action without starting study', () => {
  const { api, w, $, click } = setup(); $('.rev-root').remove();
  w.document.body.insertAdjacentHTML('beforeend', '<div class="index"><p>测试须知</p><div class="index__terms-row"><label class="index__checkbox"><input type="checkbox" class="index__native-checkbox" checked style="opacity:0">同意条款</label></div><button class="index__action-btn">开始学习</button></div>');
  let starts = 0; $('.index__action-btn').onclick = () => starts++;
  assert.ok(api.read().actions.some(a => a.id === 'entry-start'));
  assert.equal(click('entry-consent').accepted, true);
  assert.equal(api.read().actions.some(a => a.id === 'entry-start'), false);
  assert.equal(click('entry-start').accepted, false); assert.equal(starts, 0);
});

test('public hyphenated tabbar class opens review and ignores a hidden entry page', () => {
  const { api, w, $, click } = setup(); $('.rev-root').remove();
  w.document.body.insertAdjacentHTML('beforeend', '<div class="index hidden"><div class="index__access">旧的登录提示</div></div><div class="home"><div class="taroify-tabbar"><div class="taroify-tabbar-item">复习</div><div class="taroify-tabbar-item">选词</div></div></div>');
  let count = 0; $('.taroify-tabbar-item').onclick = () => count++;
  assert.equal(api.read().stage, 'ready'); assert.equal(count, 0);
  assert.equal(click('review-tab').accepted, true); assert.equal(count, 1);
});

test('examples contain the complete rendered list, retain duplicate sentences and skip hidden or empty English', () => {
  const { api, $ } = setup({ answer: true });
  $('.rev-scroller').innerHTML = `
    <div class="phrase-root"><div class="phrase-phrase">The <span class="phrase-hl"><span class="memo-word">first</span></span> sentence.</div><div class="phrase-interpretation">第一句。</div></div>
    <div class="phrase-root hidden"><div class="phrase-phrase">Hidden parent.</div><div class="phrase-interpretation">隐藏的整句。</div></div>
    <div class="phrase-root"><div class="phrase-phrase hidden">Hidden English.</div><div class="phrase-interpretation">隐藏的英文。</div></div>
    <div class="phrase-root"><div class="phrase-phrase">   </div><div class="phrase-interpretation">没有英文。</div></div>
    <div class="phrase-root"><div class="phrase-phrase">The first sentence.</div><div class="phrase-interpretation">另一个译法。</div></div>
    <div class="phrase-root"><div class="phrase-phrase">The third sentence.</div><div class="phrase-interpretation">第三句。</div></div>
    <div class="phrase-root"><div class="phrase-phrase">The fourth sentence.</div><div class="phrase-interpretation">第四句。</div></div>`;
  const state = api.read();
  assert.deepEqual(Array.from(state.examples, example => example.text), [
    'The first sentence.', 'The first sentence.', 'The third sentence.', 'The fourth sentence.'
  ]);
  assert.equal(new Set(Array.from(state.examples, example => example.id)).size, 4);
  for (const example of state.examples) {
    assert.match(example.id, /^example-\d+$/);
    assert.deepEqual(Object.keys(example).sort(), ['id', 'text']);
  }
  assert.deepEqual(Array.from(api.read().examples, example => example.id), Array.from(state.examples, example => example.id));
  assert.equal(Object.hasOwn(state, 'example'), false); assert.equal(Object.hasOwn(state, 'translation'), false);
});

test('reading examples never accesses Chinese text and a translation request reads only its paired root', () => {
  const { api, $, w } = setup({ answer: true });
  $('.rev-scroller').innerHTML = `
    <div class="phrase-root"><div class="phrase-phrase">First sentence.</div><div class="phrase-interpretation">第一句。</div></div>
    <div class="phrase-root"><div class="phrase-phrase hidden">A hidden sentence.</div><div class="phrase-interpretation">不能错配这一句。</div></div>
    <div class="phrase-root"><div class="phrase-phrase">Second sentence.</div><div class="phrase-interpretation">第二句。</div></div>`;
  const translations = Array.from(w.document.querySelectorAll('.phrase-interpretation'));
  const reads = [0, 0, 0];
  const getter = Object.getOwnPropertyDescriptor(w.Node.prototype, 'textContent').get;
  translations.forEach((element, index) => Object.defineProperty(element, 'textContent', {
    get() { reads[index]++; return getter.call(this); }
  }));
  const state = api.read();
  assert.deepEqual(reads, [0, 0, 0]);
  const command = { stamp: state.stamp, id: state.examples[1].id };
  const result = api.translation(command);
  assert.equal(result.stamp, command.stamp); assert.equal(result.id, command.id); assert.equal(result.text, '第二句。');
  assert.equal(reads[0], 0); assert.equal(reads[1], 0); assert.ok(reads[2] > 0);
  assert.equal(api.read().stamp, state.stamp);
  assert.deepEqual(Array.from(api.read().examples, example => example.text), ['First sentence.', 'Second sentence.']);
});

test('translation is a read-only action and does not consume the next feedback', () => {
  const { api, $, click, w } = setup({ answer: true });
  let clicks = 0, inputs = 0, feedback = 0;
  w.document.body.addEventListener('click', () => clicks++);
  w.document.body.addEventListener('input', () => inputs++);
  $('.reset-button').onclick = () => feedback++;
  const state = api.read();
  const command = { stamp: state.stamp, id: state.examples[0].id };
  assert.equal(api.translation(command).text, '一次美好的偶遇。');
  assert.equal(api.translation(command).text, '一次美好的偶遇。');
  assert.equal(clicks, 0); assert.equal(inputs, 0); assert.equal(api.read().waiting, false);
  assert.equal(click('familiar').accepted, true); assert.equal(feedback, 1);
});

test('translation rejects an old word snapshot and unknown example IDs', () => {
  const { api, $ } = setup({ answer: true });
  const state = api.read(); const command = { stamp: state.stamp, id: state.examples[0].id };
  assert.equal(api.translation({ stamp: state.stamp, id: 'example-999999' }), null);
  assert.equal(api.translation({ stamp: state.stamp, id: 'audio' }), null);
  for (const value of [null, {}, { id: command.id }, { stamp: state.stamp }, { stamp: 3, id: command.id }])
    assert.equal(api.translation(value), null);
  $('.spelling').textContent = 'another';
  assert.equal(api.translation(command), null);
});

test('sentence order, content and DOM identity changes invalidate prior translation requests', () => {
  for (const change of ['order', 'text', 'identity']) {
    const { api, $ } = setup({ answer: true });
    $('.rev-scroller').insertAdjacentHTML('beforeend', '<div class="phrase-root"><div class="phrase-phrase">Another example.</div><div class="phrase-interpretation">另一个例句。</div></div>');
    const before = api.read();
    const command = { stamp: before.stamp, id: before.examples[0].id };
    const first = $('.phrase-root');
    if (change === 'order') $('.rev-scroller').append(first);
    if (change === 'text') first.querySelector('.phrase-phrase').textContent = 'An updated example.';
    if (change === 'identity') first.replaceWith(first.cloneNode(true));
    const after = api.read();
    assert.notEqual(after.stamp, before.stamp, change);
    assert.equal(api.translation(command), null, change);
    assert.equal(api.translation({ stamp: after.stamp, id: after.examples[0].id }).text,
      change === 'order' ? '另一个例句。' : '一次美好的偶遇。', change);
  }
});

test('missing, empty and hidden translations return empty text without reading hidden content', () => {
  for (const variant of ['missing', 'empty', 'hidden', 'aria-hidden']) {
    const { api, $ } = setup({ answer: true });
    const translation = $('.phrase-interpretation');
    if (variant === 'missing') translation.remove();
    if (variant === 'empty') translation.textContent = '';
    if (variant === 'hidden' || variant === 'aria-hidden') {
      if (variant === 'hidden') translation.classList.add('hidden');
      else translation.setAttribute('aria-hidden', 'true');
      Object.defineProperty(translation, 'textContent', { get() { throw new Error('Read hidden Chinese text'); } });
    }
    const state = api.read();
    const result = api.translation({ stamp: state.stamp, id: state.examples[0].id });
    assert.equal(result.text, '', variant); assert.equal(result.id, state.examples[0].id, variant);
  }
});

test('translation uses current rendered Chinese at request time without invalidating the English snapshot', () => {
  const { api, $ } = setup({ answer: true });
  const before = api.read();
  $('.phrase-interpretation').textContent = '刚更新的译文。';
  assert.equal(api.read().stamp, before.stamp);
  const result = api.translation({ stamp: before.stamp, id: before.examples[0].id });
  assert.equal(result.text, '刚更新的译文。');
});

test('translation is unavailable during pending feedback, a modal or recall', () => {
  for (const variant of ['waiting', 'modal', 'recall']) {
    const { api, $, w, click } = setup({ answer: true });
    const before = api.read(); const id = before.examples[0].id;
    if (variant === 'waiting') assert.equal(click('familiar').accepted, true);
    if (variant === 'modal') w.document.body.insertAdjacentHTML('beforeend', '<div role="dialog">请确认</div>');
    if (variant === 'recall') {
      $('.interp-ctn').hidden = true;
      $('.rev-root').insertAdjacentHTML('beforeend', '<div class="rev-recall-mask">点击屏幕显示答案</div>');
    }
    const current = api.read();
    assert.equal(api.translation({ stamp: current.stamp, id }), null, variant);
    if (variant !== 'waiting') assert.equal(current.examples.length, 0, variant);
  }
});

test('changing example content while feedback is pending never unlocks another feedback', () => {
  const { api, $, click } = setup({ answer: true });
  let feedback = 0; $('.reset-button').onclick = () => feedback++;
  assert.equal(click('familiar').accepted, true);
  let before = api.read();
  for (const change of ['text', 'identity', 'addition']) {
    const first = $('.phrase-root');
    if (change === 'text') first.querySelector('.phrase-phrase').textContent = 'An updated sentence.';
    if (change === 'identity') first.replaceWith(first.cloneNode(true));
    if (change === 'addition') $('.rev-scroller').insertAdjacentHTML('beforeend', '<div class="phrase-root"><div class="phrase-phrase">A second sentence.</div><div class="phrase-interpretation">第二句。</div></div>');
    const after = api.read();
    assert.notEqual(after.stamp, before.stamp, change); assert.equal(after.waiting, true, change);
    assert.equal(api.translation({ stamp: after.stamp, id: after.examples[0].id }), null, change);
    assert.equal(click('familiar').accepted, false, change); assert.equal(click('forget').accepted, false, change);
    before = after;
  }
  assert.equal(feedback, 1);
});

test('spelling prompts expose rendered English examples and request only available translations', () => {
  for (const stage of ['spelling', 'spelling-recall']) {
    const { api, $ } = setup({ answer: true });
    $('.spelling').remove();
    if (stage === 'spelling') {
      $('.verify-input').classList.remove('hidden'); $('.verify-input').innerHTML = '<input type="text">';
    } else $('.rev-top').insertAdjacentHTML('beforeend', '<div class="spelling-hint">回忆拼写</div>');
    // Official show: "en" renders an empty interpretation container during spelling.
    $('.phrase-interpretation').textContent = '';
    const state = api.read();
    assert.equal(state.stage, stage); assert.equal(state.word, '');
    assert.deepEqual(Array.from(state.examples, example => example.text), ['A moment of serendipity.']);
    assert.equal(Object.hasOwn(state, 'translation'), false);
    assert.equal(api.translation({ stamp: state.stamp, id: state.examples[0].id }).text, '');
  }
});

// The official note component has an unclassified view inside the content container.
function addAssociation($, { type = '联想', text = 'serene 平静的\nserendipity 意外的幸运' } = {}) {
  $('.rev-scroller').insertAdjacentHTML('beforeend', '<taro-view-core class="rev-content-container"><taro-view-core><taro-text-core class="note-type"></taro-text-core><taro-text-core class="note-note"></taro-text-core></taro-view-core></taro-view-core>');
  const container = $('.rev-scroller').lastElementChild.firstElementChild;
  const note = container.querySelector('.note-note');
  const label = container.querySelector('.note-type');
  if (type === null) label.remove(); else label.textContent = type;
  note.textContent = text;
  return { container, note, label };
}

test('answer reads the complete official association note with nested words and original line breaks', () => {
  const { api, $ } = setup({ answer: true });
  const { note } = addAssociation($, { type: '  联想\t 助记  ' });
  note.innerHTML = '  <taro-text-core class="memo-word">serene</taro-text-core>\t  平静的\n<taro-text-core><taro-text-core class="memo-word">serendipity</taro-text-core>  意外的幸运</taro-text-core>\n\n记住这个联系。  ';
  const state = api.read();
  assert.equal(state.stage, 'answer');
  assert.deepEqual(Object.keys(state.association).sort(), ['text', 'type']);
  assert.equal(state.association.type, '联想 助记');
  assert.equal(state.association.text, 'serene 平静的\nserendipity 意外的幸运\n\n记住这个联系。');
});

test('association type belongs to its note parent and never comes from another module', () => {
  const { api, $ } = setup({ answer: true });
  $('.rev-scroller').insertAdjacentHTML('afterbegin', '<div class="rev-content-container"><span class="note-type">另一个模块的类型</span></div>');
  const { container } = addAssociation($, { type: null, text: '正文没有类别也应保留。' });
  assert.equal(api.read().association.type, '');
  assert.equal(api.read().association.text, '正文没有类别也应保留。');
  container.insertAdjacentHTML('afterbegin', '<span class="note-type hidden">隐藏类型</span><span class="note-type">词根</span>');
  assert.equal(api.read().association.type, '词根');
});

test('missing, empty and official placeholder notes have no association', () => {
  for (const variant of ['missing', '', ' \t\n ', '暂无助记', ' \t暂无助记\n ']) {
    const { api, $ } = setup({ answer: true });
    if (variant !== 'missing') addAssociation($, { text: variant });
    assert.equal(api.read().association, null, variant);
  }
});

test('hidden note bodies and note parents are not read', () => {
  for (const variant of ['body-hidden', 'body-css', 'body-aria', 'parent-css', 'parent-visibility', 'parent-opacity']) {
    const { api, $ } = setup({ answer: true });
    const { container, note } = addAssociation($);
    if (variant === 'body-hidden') note.hidden = true;
    if (variant === 'body-css') note.classList.add('hidden');
    if (variant === 'body-aria') note.setAttribute('aria-hidden', 'true');
    if (variant === 'parent-css') container.classList.add('hidden');
    if (variant === 'parent-visibility') container.style.visibility = 'hidden';
    if (variant === 'parent-opacity') container.style.opacity = '0';
    Object.defineProperty(note, 'textContent', { get() { throw new Error('Read hidden association'); } });
    assert.equal(api.read().association, null, variant);
  }
});

test('association skips hidden notes and hidden types while keeping an untyped visible note', () => {
  const { api, $ } = setup({ answer: true });
  const hidden = addAssociation($, { text: '隐藏正文' }); hidden.container.hidden = true;
  Object.defineProperty(hidden.note, 'textContent', { get() { throw new Error('Read hidden association'); } });
  const shown = addAssociation($, { type: '隐藏类型', text: '可见正文' }); shown.label.hidden = true;
  Object.defineProperty(shown.label, 'textContent', { get() { throw new Error('Read hidden association type'); } });
  assert.equal(api.read().association.text, '可见正文');
  assert.equal(api.read().association.type, '');
});

test('association is scoped to the active study root and its scroller', () => {
  const { api, $, w } = setup({ answer: true });
  const root = $('.rev-root');
  const hidden = root.cloneNode(true); hidden.classList.add('hidden');
  hidden.querySelector('.rev-scroller').insertAdjacentHTML('beforeend', '<div><span class="note-type">隐藏卡片</span><span class="note-note">隐藏答案</span></div>');
  w.document.body.prepend(hidden);
  const inactive = hidden.cloneNode(true); inactive.classList.remove('hidden'); w.document.body.append(inactive);
  root.insertAdjacentHTML('beforeend', '<div><span class="note-type">滚动区域之外</span><span class="note-note">不能读取这里</span></div>');
  assert.equal(api.read().association, null);
  root.querySelector('.rev-scroller').insertAdjacentHTML('beforeend', '<div class="rev-content-container"><div><span class="note-type">联想</span><span class="note-note">当前卡片正文</span></div></div>');
  assert.equal(api.read().association.text, '当前卡片正文');
  assert.equal(api.read().association.type, '联想');
});

test('association bounds the displayed body and category without flattening newlines', () => {
  const { api, $ } = setup({ answer: true });
  addAssociation($, { type: '类'.repeat(45), text: '词\t  义\n' + '文'.repeat(6100) });
  const association = api.read().association;
  assert.equal(association.type, '类'.repeat(40));
  assert.equal(association.text, ('词 义\n' + '文'.repeat(6100)).slice(0, 6000));
});

test('recall, spelling and modal stages never read or expose association answers', () => {
  for (const stage of ['recall', 'spelling', 'spelling-recall', 'blocked']) {
    const { api, $, w } = setup({ answer: stage !== 'recall' });
    const { note, label } = addAssociation($);
    if (stage === 'spelling') {
      $('.spelling').remove(); $('.verify-input').classList.remove('hidden');
      $('.verify-input').innerHTML = '<input type="text">';
    }
    if (stage === 'spelling-recall') {
      $('.spelling').remove(); $('.rev-top').insertAdjacentHTML('beforeend', '<div class="spelling-hint">回忆拼写</div>');
    }
    if (stage === 'blocked') w.document.body.insertAdjacentHTML('beforeend', '<div role="dialog">请确认</div>');
    for (const element of [note, label]) Object.defineProperty(element, 'textContent', {
      get() { throw new Error('Read association before the answer stage'); }
    });
    const state = api.read();
    assert.equal(state.stage, stage); assert.equal(state.association, null, stage);
  }
});

test('unsupported feedback controls clear association when answer detection falls back to page', () => {
  for (const variant of ['label', 'missing']) {
    const { api, $ } = setup({ answer: true }); addAssociation($);
    assert.ok(api.read().association);
    if (variant === 'label') $('.reset-button').textContent = '删除';
    else $('.reset-button').remove();
    const state = api.read();
    assert.equal(state.stage, 'page'); assert.equal(state.association, null, variant);
  }
});

test('repeated association reads never click memo words or request definitions', () => {
  const { api, $, w } = setup({ answer: true });
  const { note } = addAssociation($);
  note.innerHTML = '<span class="memo-word">serene</span> 平静的\n<span class="memo-word">serendipity</span> 幸运';
  let clicks = 0, inputs = 0, requests = 0;
  w.document.body.addEventListener('click', () => clicks++);
  w.document.body.addEventListener('input', () => inputs++);
  w.fetch = () => { requests++; throw new Error('Association read requested the network'); };
  w.XMLHttpRequest = class { constructor() { requests++; throw new Error('Association read opened XHR'); } };
  w.WebSocket = class { constructor() { requests++; throw new Error('Association read opened a socket'); } };
  const first = api.read();
  for (let i = 0; i < 3; i++) {
    assert.equal(api.read().association.text, first.association.text);
    assert.equal(api.read().stamp, first.stamp);
  }
  assert.equal(clicks, 0); assert.equal(inputs, 0); assert.equal(requests, 0);
  assert.deepEqual(Object.keys(api).sort(), ['act', 'read', 'translation']);
});

test('association updates preserve the snapshot stamp and existing example translation requests', () => {
  const { api, $ } = setup({ answer: true });
  const before = api.read(); const command = { stamp: before.stamp, id: before.examples[0].id };
  const { note, label, container } = addAssociation($);
  for (const change of ['addition', 'body', 'type', 'identity', 'removal']) {
    if (change === 'body') note.textContent = '更新后的联想正文';
    if (change === 'type') label.textContent = '派生';
    if (change === 'identity') container.replaceWith(container.cloneNode(true));
    if (change === 'removal') $('.note-note').parentElement.remove();
    const current = api.read();
    assert.equal(current.stamp, before.stamp, change);
    assert.equal(api.translation(command).text, '一次美好的偶遇。', change);
  }
});

test('association changes never unlock pending feedback or allow a second submission', () => {
  const { api, $, click } = setup({ answer: true });
  const { note, label, container } = addAssociation($);
  let feedback = 0; $('.reset-button').onclick = () => feedback++;
  const before = api.read(); assert.equal(click('familiar').accepted, true);
  for (const change of ['body', 'type', 'identity', 'removal']) {
    if (change === 'body') note.textContent = '待提交时更新的助记';
    if (change === 'type') label.textContent = '近义';
    if (change === 'identity') container.replaceWith(container.cloneNode(true));
    if (change === 'removal') $('.note-note').parentElement.remove();
    const current = api.read();
    assert.equal(current.stamp, before.stamp, change); assert.equal(current.waiting, true, change);
    assert.equal(api.translation({ stamp: current.stamp, id: current.examples[0].id }), null, change);
    assert.equal(click('familiar').accepted, false, change); assert.equal(click('forget').accepted, false, change);
  }
  assert.equal(feedback, 1);
});
