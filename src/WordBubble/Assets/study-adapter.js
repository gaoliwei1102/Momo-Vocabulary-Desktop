(() => {
  'use strict';
  // This adapter reads rendered study UI only. No cookies, storage, network requests,
  // internal API calls, React internals or hidden answers are accessed.
  const allowedOrigin = 'https://tc-apis.maimemo.com';
  const allowed = () => location.origin === allowedOrigin &&
    (location.pathname === '/webstudy/app' || location.pathname.startsWith('/webstudy/app/'));
  if (!allowed()) return null;
  if (window.__wordBubbleStudyV3) return window.__wordBubbleStudyV3.read();

  const visible = element => {
    if (!element || !element.isConnected || element.getClientRects().length === 0) return false;
    for (let node = element; node && node.nodeType === 1; node = node.parentElement) {
      const style = getComputedStyle(node);
      if (node.hidden || node.getAttribute('aria-hidden') === 'true' ||
          style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0') return false;
    }
    return true;
  };
  const text = (element, max = 1200) => (element?.textContent || '').replace(/[ \t]+/g, ' ').trim().slice(0, max);
  const find = (selector, parent = document) => Array.from(parent.querySelectorAll(selector)).find(visible);
  const enabled = element => visible(element) && element.disabled !== true &&
    (!element.hasAttribute('disabled') || element.getAttribute('disabled') === 'false') &&
    element.getAttribute('aria-disabled') !== 'true' && getComputedStyle(element).pointerEvents !== 'none';
  const identifiers = new WeakMap();
  let nextId = 1;
  const identity = element => {
    if (!element) return 0;
    if (!identifiers.has(element)) identifiers.set(element, nextId++);
    return identifiers.get(element);
  };
  const epoch = Date.now().toString(36) + '-' + Math.random().toString(36).slice(2, 8);
  let revision = 0;
  let previousSignature = '';
  let pendingTransition = null;
  let lastAudio = 0;
  let lastInput = 0;
  const controls = (parent) => Array.from(parent.querySelectorAll('button, taro-button-core, .taroify-button, .taro-modal__btn, .taro-model__btn, .weui-dialog__btn')).filter(visible);
  const setInput = (input, value) => {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
    setter.call(input, value);
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
  };

  // Pair each English sentence with its own rendered example container. Chinese
  // text is deliberately not read here or included in the polling snapshot.
  const examplesIn = root => Array.from(root.querySelectorAll('.rev-scroller .phrase-root'))
    .filter(visible).map(container => {
      const english = find('.phrase-phrase', container);
      return { id: 'example-' + identity(english), text: text(english, 700), container };
    }).filter(example => example.text);
  const exampleContent = examples => examples.map(({ id, text }) => ({ id, text }));
  const associationIn = root => {
    const note = find('.rev-scroller .note-note', root);
    const content = text(note, 6000);
    if (!content || content === '暂无助记') return null;
    return { type: text(find('.note-type', note.parentElement), 40), text: content };
  };

  function inspect() {
    const state = { stage: 'page', stamp: '', word: '', phonetic: '', meaning: '', examples: [], association: null,
      progress: '', message: '', waiting: false, actions: [] };
    const targets = {};
    let examples = [];
    let responseElements = [];
    if (!allowed()) { state.message = '学习连接已变化，请重新连接。'; return { state, targets, signature: 'origin' }; }
    const modal = find('.taroify-dialog, .weui-dialog, .taro-modal, .taro__modal, [role="dialog"], [aria-modal="true"]');
    if (modal) {
      state.stage = 'blocked';
      state.message = modal.matches('.taro__modal')
        ? [text(find('.taro-modal__title', modal), 200), text(find('.taro-modal__text', modal), 700)].filter(Boolean).join('\n')
        : text(find('.taroify-dialog__message, .taroify-dialog__content, .taro-modal__content, .weui-dialog__bd', modal) || modal, 700);
      // Forward only explicitly displayed choices, with their full original labels.
      if (!find('input, iframe', modal)) controls(modal).slice(0, 4).forEach((button, i) => {
        if (enabled(button) && text(button)) {
          const id = 'dialog-' + i; targets[id] = button;
          state.actions.push({ id, label: text(button, 20), hint: '' });
        }
      });
      return { state, targets, signature: JSON.stringify(['modal', identity(modal), state.message, state.actions]) };
    }
    if (find('.taroify-popup, .taroify-backdrop, .taroify-overlay, .weui-mask')) {
      state.stage = 'blocked'; state.message = '当前连接需要额外处理，请重试连接。';
      return { state, targets, signature: 'overlay' };
    }
    const entry = find('.index');
    if (entry) {
      const access = find('.index__access', entry);
      if (access) {
        state.stage = find('.taroify-loading', access) ? 'loading' : 'blocked';
        state.message = [text(find('.index__access-status', access), 200),
          text(find('.index__access-message', access), 700)].filter(Boolean).join('\n');
        if (state.stage === 'blocked') for (const button of controls(access)) {
          const label = text(button, 20);
          const id = label === '重试检查' ? 'entry-retry' : label === '重新登录' ? 'entry-login' : null;
          if (id && enabled(button)) { targets[id] = button; state.actions.push({ id, label, hint: '' }); }
        }
      } else {
        const consent = find('.index__checkbox', entry);
        const checkbox = consent?.querySelector('input.index__native-checkbox[type="checkbox"]');
        const start = Array.from(entry.querySelectorAll('.index__action-btn')).find(el => visible(el) && text(el) === '开始学习');
        if (consent && checkbox && start) {
          state.stage = 'blocked';
          const notice = Array.from(entry.children).filter(el => visible(el) &&
            !el.matches('.index__terms-row, .index__action-btn') && !el.querySelector('.index__terms-row, .index__action-btn'))
            .map(el => text(el, 1200)).filter(Boolean).join('\n\n');
          state.message = notice + '\n\n' + text(find('.index__terms-row', entry), 240) +
            '\n\n用户协议：https://www.maimemo.com/terms\n隐私政策：https://www.maimemo.com/privcay';
          // The official native checkbox is transparent over its visible label.
          // Forward an explicit label click; never accept its terms while reading.
          if (enabled(consent) && !checkbox.disabled && !checkbox.hidden && checkbox.getClientRects().length > 0 &&
              getComputedStyle(checkbox).display !== 'none' && getComputedStyle(checkbox).visibility !== 'hidden') {
            targets['entry-consent'] = consent;
            state.actions.push({ id: 'entry-consent', label: checkbox.checked ? '取消同意' : '同意服务条款与隐私政策', hint: '' });
          }
          if (checkbox.checked && enabled(start)) {
            targets['entry-start'] = start;
            state.actions.push({ id: 'entry-start', label: '开始学习', hint: '' });
          }
        } else {
          state.stage = 'loading'; state.message = '正在准备墨墨学习入口';
        }
      }
      return { state, targets, signature: JSON.stringify(['entry', state.stage, state.message, state.actions]) };
    }
    const root = find('.rev-root');
    if (!root) {
      const finished = find('.finished-status');
      if (finished) {
        state.stage = 'complete'; state.message = text(find('.finished-review-more', finished), 500) || '这一轮学完啦，休息一下吧';
        for (const button of controls(finished)) {
          const label = text(button, 20);
          const quantity = find('.finished-input input, input.finished-input', finished);
          const count = quantity && /^\d{1,5}$/.test(quantity.value) ? Number(quantity.value) : 0;
          const id = label === '继续学习' || (label === '确定' && count > 0) ? 'continue' : label === '签到' ? 'sign-in' : null;
          if (id && enabled(button)) { targets[id] = button; state.actions.push({ id, label: label === '确定' ? `继续复习 ${count} 词` : label, hint: '' }); }
        }
      }
      else if (find('.rev-status, .home-loading')) { state.stage = 'loading'; state.message = '下一张单词卡正在路上'; }
      else {
        const review = Array.from(document.querySelectorAll('.taroify-tabbar-item, [role="tab"]')).find(el => visible(el) && text(el) === '复习');
        if (review && !find('.welcome')) { state.stage = 'ready'; targets['review-tab'] = review; state.actions.push({ id: 'review-tab', label: '载入今日单词', hint: '' }); state.message = '学习空间已连接'; }
        else state.message = find('.welcome') ? '还没有可学习的单词。请先在墨墨 App 选好词书并同步，再回来刷新。' : '暂时没有读取到学习内容，请重试连接；若仍无内容，请检查墨墨 App 中的学习计划与同步状态。';
      }
      return { state, targets, signature: JSON.stringify([state.stage, state.message, state.actions]) };
    }
    const progress = Array.from(root.querySelectorAll('.rev-top-stats > *'))
      .filter(visible).map(el => text(el, 40)).find(value => /^\d+\s*\/\s*\d+$/.test(value));
    state.progress = progress || '';
    const spellingHint = find('.spelling-hint', root);
    const verify = find('.verify-input:not(.hidden) input, input.verify-input:not(.hidden)', root);
    if (spellingHint || verify) {
      state.stage = verify ? 'spelling' : 'spelling-recall';
      state.meaning = text(find('.rev-top .interp-ctn', root));
      examples = examplesIn(root);
      state.examples = exampleContent(examples);
      if (spellingHint && enabled(spellingHint)) { targets.reveal = spellingHint; state.actions.push({ id: 'reveal', label: '看看答案', hint: '' }); }
      const begin = Array.from(root.querySelectorAll('.ask-spelling .reset-button')).find(el => visible(el) && text(el) === '验证拼写');
      if (begin && enabled(begin)) { targets['spelling-start'] = begin; state.actions.push({ id: 'spelling-start', label: '试着拼出来', hint: '' }); }
      if (verify && enabled(verify)) {
        targets['spelling-input'] = verify; state.actions.push({ id: 'spelling-input', label: '核对拼写', hint: '' });
        state.message = verify.closest('.verify-input')?.classList.contains('wrong') ? '还差一点，再试试。' : '输入你记得的拼写';
      }
      return { state, targets, examples, signature: JSON.stringify([state.stage, state.meaning, state.examples, identity(root), identity(verify)]) };
    }
    const spelling = find('.rev-top .spelling', root);
    if (!spelling || !text(spelling)) {
      state.message = '当前题型暂不支持迷你卡片';
      return { state, targets, signature: 'unsupported' };
    }
    state.word = text(spelling, 120);
    state.phonetic = text(find('.rev-top > .phonetic', root), 180);
    const reveal = find('.rev-recall-mask, .rev-recall-mask-partial', root);
    const meaning = find('.rev-top .interp-ctn', root);
    state.stage = reveal ? 'recall' : meaning ? 'answer' : 'page';
    if (state.stage === 'page') {
      state.message = '暂时没有识别到当前学习步骤，请重新连接。';
      return { state, targets, signature: 'unknown:' + identity(root) };
    }
    const audio = find('.rev-top > .phonetic', root);
    if (audio) { targets.audio = audio; state.actions.push({ id: 'audio', label: '发音', hint: '' }); }
    if (reveal && enabled(reveal)) {
      targets.reveal = reveal;
      state.actions.push({ id: 'reveal', label: '看看释义', hint: '' });
    }
    if (state.stage === 'answer') {
      state.meaning = text(meaning);
      examples = examplesIn(root);
      state.examples = exampleContent(examples);
      const responses = Array.from(root.querySelectorAll('.rev-resp-btns .reset-button')).filter(visible);
      responseElements = responses;
      const expected = [ ['familiar', ['认识']], ['vague', ['模糊', '不确定']], ['forget', ['忘记', '不认识']] ];
      if (responses.length !== 3) {
        state.stage = 'page'; state.message = '墨墨学习控件已变化，当前版本暂时无法继续，请反馈此状态以更新适配。'; state.actions = [];
        return { state, targets: {}, signature: 'unsupported-buttons' };
      }
      for (let i = 0; i < 3; i++) {
        const button = responses[i];
        const copy = button.cloneNode(true);
        copy.querySelectorAll('.predict').forEach(node => node.remove());
        const label = text(copy, 20);
        if (!expected[i][1].includes(label)) {
          state.stage = 'page'; state.message = '墨墨学习控件已变化，当前版本暂时无法继续，请反馈此状态以更新适配。'; state.actions = [];
          return { state, targets: {}, signature: 'unsupported-labels' };
        }
        if (enabled(button)) {
          targets[expected[i][0]] = button;
          state.actions.push({ id: expected[i][0], label, hint: text(find('.predict', button), 40) });
        }
      }
      state.association = associationIn(root);
    }
    // Mnemonic updates are read-only and must not invalidate examples or feedback.
    // Time/audio/progress-only mutations must not unlock another feedback click.
    const signature = JSON.stringify([state.stage, state.word, state.meaning, state.examples,
      identity(root), identity(spelling), identity(reveal), identity(meaning),
      ...responseElements.map(identity)]);
    return { state, targets, examples, signature };
  }

  function current() {
    const result = inspect();
    if (result.signature !== previousSignature) {
      previousSignature = result.signature;
      revision++;
    }
    result.transition = JSON.stringify([result.state.stage, result.state.word, result.state.meaning,
      ['blocked', 'complete', 'ready'].includes(result.state.stage) ? [result.state.message, result.state.actions] : null]);
    // Replacing equivalent DOM controls is not evidence of an accepted answer.
    if (pendingTransition !== null && pendingTransition !== result.transition) pendingTransition = null;
    result.state.stamp = epoch + ':' + revision;
    result.state.waiting = pendingTransition === result.transition;
    return result;
  }
  function read() { return current().state; }
  function translation(command) {
    if (!allowed() || !command || typeof command.id !== 'string' || typeof command.stamp !== 'string') return null;
    const result = current();
    if (result.state.stamp !== command.stamp || result.state.waiting ||
        !['answer', 'spelling', 'spelling-recall'].includes(result.state.stage)) return null;
    const example = result.examples?.find(item => item.id === command.id);
    if (!example) return null;
    // A missing/hidden translation remains unavailable. Never reveal answers by
    // clicking the official example, which can advance the recall stage.
    return { stamp: result.state.stamp, id: example.id,
      text: text(find('.phrase-interpretation', example.container), 700) };
  }
  function act(command) {
    if (!allowed() || !command || typeof command.id !== 'string' || typeof command.stamp !== 'string') return { accepted: false };
    const result = current();
    if (result.state.stamp !== command.stamp || result.state.waiting || !result.state.actions.some(a => a.id === command.id)) return { accepted: false };
    const target = result.targets[command.id];
    if (!enabled(target)) return { accepted: false };
    if (command.id === 'spelling-input') {
      if (typeof command.value !== 'string' || !command.value.trim() || command.value.length > 120 || Date.now() - lastInput < 500) return { accepted: false };
      lastInput = Date.now();
      setInput(target, command.value.trim());
      return { accepted: true };
    }
    if (command.id === 'audio') {
      if (Date.now() - lastAudio < 400) return { accepted: false };
      lastAudio = Date.now();
    } else pendingTransition = result.transition;
    try { target.click(); return { accepted: true }; }
    catch { return { accepted: false }; }
  }
  window.__wordBubbleStudyV3 = Object.freeze({ read, act, translation });
  return read();
})();
