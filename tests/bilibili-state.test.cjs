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

function setupCombat() {
    const { context: c, handlers } = setup();
    let now = 0;
    let nextTimer = 0;
    let pauseCalls = 0;
    const timers = new Map();
    const videoEvents = new Map();
    const pageEvents = new Map();
    const video = {
        currentTime: 10, currentSrc: 'video-part-1', paused: false, ended: false, seeking: false,
        addEventListener: (name, fn) => videoEvents.set(name, fn),
        removeEventListener: name => videoEvents.delete(name),
        pause() { this.paused = true; pauseCalls++; videoEvents.get('pause')?.(); }
    };
    const page = vm.createContext({
        document: { querySelector: () => video },
        location: { href: c.player.getUrl() },
        window: {
            addEventListener: (name, fn) => pageEvents.set(name, fn),
            removeEventListener: name => pageEvents.delete(name)
        },
        setTimeout(fn, delay) { const id = ++nextTimer; timers.set(id, { fn, at: now + delay }); return id; },
        clearTimeout: id => timers.delete(id)
    });
    c.webview = {
        executeScriptSync: script => vm.runInContext(script, page),
        injectScript: script => vm.runInContext(script, page)
    };
    let requests = 0;
    const subtitleHandlers = {};
    c.subtitle = {
        on: (name, fn) => subtitleHandlers[name] = fn,
        off: name => delete subtitleHandlers[name],
        request: () => requests++
    };
    c.onLoad();
    return {
        c, handlers, page, video, timers, videoEvents, pageEvents, subtitleHandlers,
        pauseCalls: () => pauseCalls, requests: () => requests,
        advance(ms) {
            now += ms;
            for (const [id, timer] of [...timers]) {
                if (timer.at <= now) { timers.delete(id); timer.fn(); }
            }
        }
    };
}

test('combat phrases pause at 500ms once per subtitle, independent of playback rate', () => {
    for (const content of ['清怪', '清下怪', '打怪', '暂停清怪', '先清一下怪', '清理一下怪', '清 下 怪']) {
        const s = setupCombat();
        s.video.playbackRate = 2;
        const entry = { from: 10, to: 13, content };
        s.subtitleHandlers.change(entry);
        s.subtitleHandlers.change(entry);
        s.advance(499);
        assert.equal(s.pauseCalls(), 0, content);
        s.advance(1);
        assert.equal(s.pauseCalls(), 1, content);
        assert.equal(s.videoEvents.size, 0);
        assert.equal(s.pageEvents.size, 0);
        s.video.paused = false;
        s.subtitleHandlers.change(null);
        s.subtitleHandlers.change(entry);
        s.advance(500);
        assert.equal(s.pauseCalls(), 1, 'resume must not pause again');
        assert.ok(s.requests() > 0, 'requests subtitle data without forcing visible subtitles');
    }
});

test('combat matching ignores unrelated text, disabled setting and non-Bilibili pages', () => {
    const s = setupCombat();
    for (const content of ['往东北走', '打开地图', '暂停', 'UP主', '奇怪']) {
        s.subtitleHandlers.change({ from: 10, to: 13, content });
    }
    assert.equal(s.timers.size, 0);
    s.c.config.get = (key, fallback) => key === 'combatPause.enabled' ? false : fallback;
    s.c.onLoad();
    s.subtitleHandlers.change({ from: 10, to: 13, content: '清怪' });
    assert.equal(s.timers.size, 0);
    s.c.config.get = (_, fallback) => fallback;
    s.c.onLoad();
    s.c.player.getUrl = () => 'https://example.com';
    s.subtitleHandlers.change({ from: 10, to: 13, content: '清怪' });
    assert.equal(s.timers.size, 0);
});

test('navigation, subtitle clear, seek, manual pause and unload cancel pending combat pause', () => {
    for (const action of ['navigation', 'clear', 'seek', 'pause', 'unload']) {
        const s = setupCombat();
        s.subtitleHandlers.change({ from: 10, to: 13, content: '清怪' });
        assert.equal(s.timers.size, 1);
        if (action === 'navigation') s.handlers.urlChanged({ url: 'https://www.bilibili.com/video/BV456' });
        if (action === 'clear') s.subtitleHandlers.clear();
        if (action === 'seek') s.videoEvents.get('seeking')();
        if (action === 'pause') { s.video.pause(); s.video.paused = false; }
        if (action === 'unload') s.c.onUnload();
        const pauses = s.pauseCalls();
        s.advance(500);
        assert.equal(s.pauseCalls(), pauses, action);
        assert.equal(s.timers.size, 0, action);
        assert.equal(s.videoEvents.size, 0, action);
        assert.equal(s.pageEvents.size, 0, action);
        if (action === 'unload') assert.deepEqual(Object.keys(s.subtitleHandlers), []);
    }
});

test('a pending pause is not postponed by another cue and later cues still work', () => {
    const s = setupCombat();
    s.subtitleHandlers.change({ from: 10, to: 13, content: '清怪' });
    s.advance(300);
    s.subtitleHandlers.change({ from: 10.2, to: 13, content: '打怪' });
    s.advance(200);
    assert.equal(s.pauseCalls(), 1);
    s.video.paused = false;
    s.video.currentTime = 20;
    s.subtitleHandlers.change({ from: 20, to: 23, content: '清怪' });
    s.advance(500);
    assert.equal(s.pauseCalls(), 2);
});

test('stale cues and changed sources cannot pause a different video', () => {
    const s = setupCombat();
    s.subtitleHandlers.change({ from: 30, to: 33, content: '清怪' });
    assert.equal(s.timers.size, 0);
    s.subtitleHandlers.change({ from: 10, to: 13, content: '清怪' });
    s.video.currentSrc = 'video-part-2';
    s.advance(500);
    assert.equal(s.pauseCalls(), 0);
    s.subtitleHandlers.clear();
    s.subtitleHandlers.change({ from: 10, to: 13, content: '清怪' });
    s.page.location.href = 'https://www.bilibili.com/video/BV456';
    s.advance(500);
    assert.equal(s.pauseCalls(), 0);
});

test('an already paused video stays paused without a timer', () => {
    const s = setupCombat();
    s.video.paused = true;
    s.subtitleHandlers.change({ from: 10, to: 13, content: '清怪' });
    s.advance(500);
    assert.equal(s.pauseCalls(), 0);
    assert.equal(s.video.paused, true);
    assert.equal(s.timers.size, 0);
});

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
