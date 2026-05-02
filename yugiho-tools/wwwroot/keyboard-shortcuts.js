window.yugihoKeyboard = (() => {
    let handler = null;

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
        // For the settings page: capture next key on a specific element
        captureNext(elementId, dotnetRef) {
            const el = document.getElementById(elementId);
            if (!el) return;
            const oneShot = (e) => {
                e.preventDefault();
                e.stopPropagation();
                const combo = buildCombo(e);
                if (!combo) return;
                el.removeEventListener('keydown', oneShot);
                dotnetRef.invokeMethodAsync('OnCaptureKey', combo);
            };
            el.addEventListener('keydown', oneShot);
            el.focus();
        },
    };
})();
