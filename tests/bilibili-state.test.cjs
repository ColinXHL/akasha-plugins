const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../plugins/bilibili-page-list/main.js'), 'utf8');

function setup() {
    const handlers = {};
    const context = vm.createContext({
        log: { info() {}, warn() {}, debug() {} },
        plugin: { name: 'test', version: 'test' },
        config: { get: (_, fallback) => fallback },
        hotkey: { register() {}, unregisterAll() {} },
        event: { on: (name, fn) => handlers[name] = fn, off: name => delete handlers[name] },
        panel: { on() {}, off() {} },
        player: { getUrl: () => 'https://www.bilibili.com/video/BV123?p=4' },
        subtitle: { on() {}, off() {} },
        Date, sleep() {}
    });
    vm.runInContext(source, context);
    context.fetchPageList = () => Array.from({ length: 8 }, (_, i) => ({ page: i + 1 }));
    context.detectCurrentPageFromPageState = () => 0;
    context.refreshActionButtons = () => {};
    context.renderPageList = () => {};
    context.hideOverlay = () => {};
    context.syncSubtitleStateFromPage = () => {};
    return { context, handlers };
}

test('first load and host URL events retain the requested part', () => {
    const { context: c, handlers } = setup();
    c.onLoad();
    assert.equal(c.state.currentPage, 4);
    handlers.urlChanged({ url: 'https://www.bilibili.com/video/BV456?p=6' });
    assert.equal(c.state.currentPage, 6);
    assert.equal(c.state.currentVideoId, 'BV456');
    c.onUnload();
    assert.deepEqual(Object.keys(handlers), []);
});

test('late player state refreshes the visible panel without reopening', () => {
    const { context: c } = setup();
    c.onLoad();
    c.state.isVisible = true;
    let renders = 0;
    c.renderPageList = () => renders++;
    c.detectCurrentPageFromPageState = () => 5;
    c.onPlaybackUpdate();
    assert.equal(c.state.currentPage, 5);
    assert.equal(renders, 1);
});

test('subtitle preference survives clear and waits for the language menu', () => {
    const { context: c } = setup();
    c.state.subtitleEnabled = true;
    c.onSubtitleCleared();
    c.onSubtitleCleared();
    assert.equal(c.state.pendingSubtitleEnable, true);
    c.getPreferredSubtitleLanguageFromDom = () => '';
    c.onSubtitleLoaded({ language: 'ai-zh', body: [{}] });
    assert.equal(c.state.pendingSubtitleEnable, true);
    let enabled = 0;
    c.getPreferredSubtitleLanguageFromDom = () => 'ai-zh';
    c.enableSubtitleByDomPriority = () => enabled++;
    c.restoreSubtitlePreference();
    c.restoreSubtitlePreference();
    assert.equal(enabled, 1);
});

test('turning off a pending subtitle request prevents late re-enabling', () => {
    const { context: c } = setup();
    c.state.subtitlePreferred = true;
    c.state.pendingSubtitleEnable = true;
    c.setBilibiliSubtitleSwitch = () => {};
    c.toggleSubtitle();
    c.onSubtitleCleared();
    c.enableSubtitleByDomPriority = () => assert.fail('must stay disabled');
    c.onSubtitleLoaded({ language: 'ai-zh', body: [{}] });
    assert.equal(c.state.subtitlePreferred, false);
    assert.equal(c.state.pendingSubtitleEnable, false);
});

test('current player CID wins over the stale initial part number', () => {
    const { context: c } = setup();
    vm.runInContext(source, c); // Exercise the actual injected page-state reader.
    const page = vm.createContext({
        location: { href: 'https://www.bilibili.com/video/BV123' },
        window: {
            __INITIAL_STATE__: { p: 1, videoData: { p: 1, pages: [
                { cid: 10, page: 1 }, { cid: 20, page: 2 }
            ] } },
            player: { getVideoMessage: () => ({ cid: 20 }) }
        },
        document: { querySelector: () => null }
    });
    c.webview = { executeScriptSync: script => vm.runInContext(script, page) };
    assert.equal(c.detectCurrentPageFromPageState(), 2);
});

test('a part without subtitles does not erase the preference for the next part', () => {
    const { context: c } = setup();
    c.state.subtitlePreferred = true;
    c.onSubtitleCleared();
    c.getPreferredSubtitleLanguageFromDom = () => '';
    c.restoreSubtitlePreference();
    c.onUrlChanged('https://www.bilibili.com/video/BV123?p=2');
    c.onSubtitleCleared();
    let enabled = false;
    c.getPreferredSubtitleLanguageFromDom = () => 'zh-CN';
    c.enableSubtitleByDomPriority = () => enabled = true;
    c.restoreSubtitlePreference();
    assert.equal(enabled, true);
});
