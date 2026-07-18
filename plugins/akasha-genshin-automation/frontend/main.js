var AUTO_PICK_KEY = "autoPickEnabled";
var AUTO_DIALOGUE_KEY = "autoDialogueEnabled";
var AUTO_PICK_HOTKEY_KEY = "autoPickHotkey";
var AUTO_DIALOGUE_HOTKEY_KEY = "autoDialogueHotkey";

function readBoolean(key, defaultValue) {
    var value = config.get(key, defaultValue);
    if (typeof value !== "boolean") {
        log.warn("配置 " + key + " 不是布尔值，已使用默认值");
        return defaultValue;
    }

    return value;
}

function readString(key, defaultValue) {
    var value = config.get(key, defaultValue);
    if (typeof value !== "string") {
        log.warn("配置 " + key + " 不是文本，已使用默认值");
        return defaultValue;
    }

    return value;
}

function readInteger(key, defaultValue, minimum, maximum) {
    var value = config.get(key, defaultValue);
    if (typeof value !== "number" || !isFinite(value)) {
        log.warn("配置 " + key + " 不是数字，已使用默认值");
        return defaultValue;
    }

    return Math.max(minimum, Math.min(maximum, Math.round(value)));
}

function readChoice(key, defaultValue, choices) {
    var value = readString(key, defaultValue);
    return choices.indexOf(value) >= 0 ? value : defaultValue;
}

function parseList(value) {
    return value
        .split(/[\r\n,，;；]+/)
        .map(function (item) { return item.trim(); })
        .filter(function (item) { return item.length > 0; });
}

function buildAutoPickOptions() {
    var whitelist = parseList(readString("userWhitelist", ""));
    return {
        enabled: readBoolean(AUTO_PICK_KEY, false),
        pickKey: readChoice("pickKey", "F", ["E", "F", "G"]),
        ocrEngine: "Paddle",
        blackListEnabled: readBoolean("blackListEnabled", true),
        whiteListEnabled: whitelist.length > 0,
        userExactBlacklist: parseList(readString("userExactBlacklist", "")),
        userFuzzyBlacklist: parseList(readString("userFuzzyBlacklist", "")),
        userWhitelist: whitelist,
        itemIconLeftOffset: 60,
        itemTextLeftOffset: 115,
        itemTextRightOffset: 400
    };
}

function buildAutoDialogueOptions() {
    var interactionKey = readChoice("pickKey", "F", ["E", "F", "G"]);
    return {
        enabled: readBoolean(AUTO_DIALOGUE_KEY, false),
        quicklyAdvanceEnabled: readBoolean("quicklyAdvanceEnabled", true),
        advanceKey: readChoice("advanceKey", "Space", ["Space", "Interaction"]),
        interactionKey: interactionKey,
        optionStrategy: readChoice("optionStrategy", "First", ["First", "Last", "Random", "None"]),
        customPriorityOptionsEnabled: false,
        customPriorityOptions: [],
        skipBuiltInPriority: false,
        beforeAdvanceDelayMilliseconds: readInteger("beforeAdvanceDelayMilliseconds", 0, 0, 5000),
        afterOptionDelayMilliseconds: readInteger("afterOptionDelayMilliseconds", 0, 0, 5000),
        autoWaitDialogueVoiceEnabled: false,
        dialogueVoiceMaxWaitSeconds: 30,
        closePopupPagesEnabled: readBoolean("closePopupPagesEnabled", true),
        submitGoodsEnabled: readBoolean("submitGoodsEnabled", true),
        autoGetDailyRewardsEnabled: readBoolean("autoGetDailyRewardsEnabled", true),
        autoReExploreEnabled: readBoolean("autoReExploreEnabled", true),
        autoHangoutEnabled: false,
        hangoutEnding: "",
        autoHangoutSkipEnabled: true
    };
}

function ensureSucceeded(result, operation) {
    if (!result || !result.success) {
        var message = result && result.error ? result.error : "未知错误";
        throw new Error(operation + "失败: " + message);
    }

    return result;
}

function applyFeatureOptions(method, options, displayName) {
    return companion.invoke(method, options)
        .then(function (result) {
            ensureSucceeded(result, "应用" + displayName + "设置");
            log.info(displayName + (options.enabled ? "已启用" : "已关闭") + "，配置已同步");
        });
}

function showOsd(message, icon) {
    if (typeof osd !== "undefined" && osd && typeof osd.show === "function") {
        osd.show(message, icon);
    }
}

function toggleFeature(configKey, method, optionsBuilder, displayName) {
    var enabled = !readBoolean(configKey, false);
    var options = optionsBuilder();
    options.enabled = enabled;

    return applyFeatureOptions(method, options, displayName)
        .then(function () {
            config.set(configKey, enabled);
            log.info("已通过快捷键" + (enabled ? "启用" : "关闭") + displayName);
            showOsd(
                displayName + (enabled ? "已启用" : "已关闭"),
                enabled ? "✅" : "⏸");
        })
        .catch(function (error) {
            log.error("快捷键切换" + displayName + "失败: " + error);
            showOsd(displayName + "切换失败", "⚠️");
        });
}

function registerFeatureHotkey(configKey, defaultHotkey, callback, displayName) {
    var keyCombo = readString(configKey, defaultHotkey);
    var registrationId = hotkey.register(keyCombo, callback);
    if (registrationId < 0) {
        log.warn(displayName + "快捷键注册失败，可能与其它快捷键冲突: " + keyCombo);
        showOsd(displayName + "快捷键注册失败", "⚠️");
        return;
    }

    log.info("已注册" + displayName + "快捷键: " + keyCombo);
}

function registerFeatureHotkeys() {
    registerFeatureHotkey(
        AUTO_PICK_HOTKEY_KEY,
        "F9",
        function () {
            toggleFeature(
                AUTO_PICK_KEY,
                "features.autoPick.setOptions",
                buildAutoPickOptions,
                "自动拾取");
        },
        "自动拾取");

    registerFeatureHotkey(
        AUTO_DIALOGUE_HOTKEY_KEY,
        "F12",
        function () {
            toggleFeature(
                AUTO_DIALOGUE_KEY,
                "features.autoDialogue.setOptions",
                buildAutoDialogueOptions,
                "自动剧情");
        },
        "自动剧情");
}

function onLoad() {
    var autoPickOptions = buildAutoPickOptions();
    var autoDialogueOptions = buildAutoDialogueOptions();
    registerFeatureHotkeys();

    log.info("正在启动 Akasha Automation Worker...");
    companion.start()
        .then(function (startResult) {
            ensureSucceeded(startResult, "Worker 启动");
            return Promise.all([
                applyFeatureOptions("features.autoPick.setOptions", autoPickOptions, "自动拾取"),
                applyFeatureOptions("features.autoDialogue.setOptions", autoDialogueOptions, "自动剧情"),
                companion.invoke("worker.getStatus")
            ]);
        })
        .then(function (results) {
            var statusResult = ensureSucceeded(results[2], "读取 Worker 状态");
            var status = statusResult.data;
            log.info(
                "Worker 已连接，状态=" + status.state +
                "，真实输入=" + (status.realInputEnabled ? "可用" : "不可用"));
        })
        .catch(function (error) {
            log.error("Worker 初始化异常: " + error);
            showOsd("原神自动化启动失败", "⚠️");
            return companion.stop().catch(function (stopError) {
                log.warn("初始化失败后的 Worker 清理异常: " + stopError);
            });
        });
}

function onUnload() {
    hotkey.unregisterAll();
    companion.stop().catch(function (error) {
        log.warn("Worker 停止异常: " + error);
    });
}
