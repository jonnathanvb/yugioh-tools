window.yugihoKeyboard = (() => {
    let handler = null;
    let padDotnet = null;
    let padFrame = 0;
    let padPrev = {};
    const PAD_NAMES = [
        "A", "B", "X", "Y",
        "LB", "RB", "LT", "RT",
        "Back", "Start",
        "LStick", "RStick",
        "DPadUp", "DPadDown", "DPadLeft", "DPadRight",
    ];

    function buildCombo(e) {
        const parts = [];
        if (e.ctrlKey)  parts.push('Ctrl');
        if (e.shiftKey) parts.push('Shift');
        if (e.altKey)   parts.push('Alt');
        const key = (e.key || '').toUpperCase();
        // Skip when only modifiers are pressed
        if (key === 'CONTROL' || key === 'SHIFT' || key === 'ALT' || key === 'META') return null;
        parts.push(key);
        return parts.join('+');
    }

    function isInputFocused(target) {
        if (!target) return false;
        const tag = target.tagName;
        return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || target.isContentEditable;
    }

    return {
        register(dotnetRef) {
            if (handler) document.removeEventListener('keydown', handler);
            handler = (e) => {
                if (isInputFocused(e.target)) return;
                const combo = buildCombo(e);
                if (!combo) return;
                dotnetRef.invokeMethodAsync('OnGlobalKey', combo);
            };
            document.addEventListener('keydown', handler);
        },
        unregister() {
            if (handler) {
                document.removeEventListener('keydown', handler);
                handler = null;
            }
        },

        // Continuous gamepad watcher (foreground). Fires "Pad:NAME" on press transitions.
        startGamepadWatch(dotnetRef) {
            this.stopGamepadWatch();
            padDotnet = dotnetRef;
            padPrev = {};
            const tick = () => {
                if (!padDotnet) return;
                const pads = navigator.getGamepads ? navigator.getGamepads() : [];
                for (const pad of pads) {
                    if (!pad) continue;
                    const limit = Math.min(pad.buttons.length, PAD_NAMES.length);
                    for (let i = 0; i < limit; i++) {
                        const pressed = pad.buttons[i].pressed;
                        const k = pad.index + ":" + i;
                        if (pressed && !padPrev[k]) {
                            try {
                                padDotnet.invokeMethodAsync('OnGamepadButton', "Pad:" + PAD_NAMES[i]);
                            } catch (_) { /* dotnet ref may have been disposed */ }
                        }
                        padPrev[k] = pressed;
                    }
                }
                padFrame = requestAnimationFrame(tick);
            };
            padFrame = requestAnimationFrame(tick);
        },

        stopGamepadWatch() {
            if (padFrame) cancelAnimationFrame(padFrame);
            padFrame = 0;
            padDotnet = null;
            padPrev = {};
        },
        // For the settings page: capture next key OR gamepad button.
        captureNext(elementId, dotnetRef) {
            const el = document.getElementById(elementId);
            if (!el) return;

            let stopped = false;
            const finish = (combo) => {
                if (stopped) return;
                stopped = true;
                el.removeEventListener('keydown', oneShot);
                dotnetRef.invokeMethodAsync('OnCaptureKey', combo);
            };
            const oneShot = (e) => {
                e.preventDefault();
                e.stopPropagation();
                const combo = buildCombo(e);
                if (combo) finish(combo);
            };
            el.addEventListener('keydown', oneShot);
            el.focus();

            // Standard browser Gamepad API mapping (Xbox/XInput-like layout)
            const padNames = [
                "A", "B", "X", "Y",
                "LB", "RB", "LT", "RT",
                "Back", "Start",
                "LStick", "RStick",
                "DPadUp", "DPadDown", "DPadLeft", "DPadRight",
            ];
            const initial = {};

            const poll = () => {
                if (stopped) return;
                const pads = navigator.getGamepads ? navigator.getGamepads() : [];
                for (const pad of pads) {
                    if (!pad) continue;
                    const limit = Math.min(pad.buttons.length, padNames.length);
                    for (let i = 0; i < limit; i++) {
                        const pressed = pad.buttons[i].pressed;
                        const k = pad.index + ":" + i;
                        if (initial[k] === undefined) {
                            initial[k] = pressed;
                        } else if (pressed && !initial[k]) {
                            finish("Pad:" + padNames[i]);
                            return;
                        }
                        initial[k] = pressed;
                    }
                }
                requestAnimationFrame(poll);
            };
            requestAnimationFrame(poll);
        },
    };
})();
