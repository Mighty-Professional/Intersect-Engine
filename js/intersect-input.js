// DOM Input Interop for Intersect Engine
window.IntersectInput = (() => {
    const keysDown = new Set();
    const keysPrev = new Set();
    const mouseButtons = new Set();
    const mouseButtonsPrev = new Set();
    let mouseX = 0;
    let mouseY = 0;
    let scrollDeltaX = 0;
    let scrollDeltaY = 0;
    let textInputBuffer = [];
    let canvas = null;
    // Queue of mouse button events: { type: 'down'|'up', button: int, x: float, y: float }
    let mouseEventQueue = [];
    // Queue of key events: { type: 'down'|'up', key: int }
    let keyEventQueue = [];
    // Queued key state changes — applied in update() so prev/current are one frame apart
    let keyStateQueue = [];
    let mouseStateQueue = [];

    // DOM key code → Intersect Keys enum mapping
    const keyMap = {
        'KeyA': 65, 'KeyB': 66, 'KeyC': 67, 'KeyD': 68, 'KeyE': 69,
        'KeyF': 70, 'KeyG': 71, 'KeyH': 72, 'KeyI': 73, 'KeyJ': 74,
        'KeyK': 75, 'KeyL': 76, 'KeyM': 77, 'KeyN': 78, 'KeyO': 79,
        'KeyP': 80, 'KeyQ': 81, 'KeyR': 82, 'KeyS': 83, 'KeyT': 84,
        'KeyU': 85, 'KeyV': 86, 'KeyW': 87, 'KeyX': 88, 'KeyY': 89,
        'KeyZ': 90,
        'Digit0': 48, 'Digit1': 49, 'Digit2': 50, 'Digit3': 51, 'Digit4': 52,
        'Digit5': 53, 'Digit6': 54, 'Digit7': 55, 'Digit8': 56, 'Digit9': 57,
        'Numpad0': 96, 'Numpad1': 97, 'Numpad2': 98, 'Numpad3': 99, 'Numpad4': 100,
        'Numpad5': 101, 'Numpad6': 102, 'Numpad7': 103, 'Numpad8': 104, 'Numpad9': 105,
        'F1': 112, 'F2': 113, 'F3': 114, 'F4': 115, 'F5': 116, 'F6': 117,
        'F7': 118, 'F8': 119, 'F9': 120, 'F10': 121, 'F11': 122, 'F12': 123,
        'ArrowUp': 38, 'ArrowDown': 40, 'ArrowLeft': 37, 'ArrowRight': 39,
        'Enter': 13, 'NumpadEnter': 13, 'Escape': 27, 'Space': 32,
        'Tab': 9, 'Backspace': 8, 'Delete': 46,
        'ShiftLeft': 16, 'ShiftRight': 16,
        'ControlLeft': 17, 'ControlRight': 17,
        'AltLeft': 18, 'AltRight': 18,
        'Home': 36, 'End': 35, 'PageUp': 33, 'PageDown': 34,
        'Insert': 45,
        'CapsLock': 20, 'NumLock': 144, 'ScrollLock': 145,
        'Semicolon': 186, 'Equal': 187, 'Comma': 188, 'Minus': 189,
        'Period': 190, 'Slash': 191, 'Backquote': 192,
        'BracketLeft': 219, 'Backslash': 220, 'BracketRight': 221,
        'Quote': 222,
        'NumpadMultiply': 106, 'NumpadAdd': 107, 'NumpadSubtract': 109,
        'NumpadDecimal': 110, 'NumpadDivide': 111,
    };

    const mouseButtonMap = {
        0: 0, // Left
        1: 2, // Middle
        2: 1, // Right
        3: 3, // X1
        4: 4, // X2
    };

    return {
        init(canvasId) {
            canvas = document.getElementById(canvasId);
            if (!canvas) return false;

            canvas.addEventListener('keydown', (e) => {
                const key = keyMap[e.code];
                if (key !== undefined) {
                    // Queue state change — applied in update() so IsJustPressed works
                    keyStateQueue.push({ type: 'down', key });
                    keyEventQueue.push({ type: 'down', key });
                }
                // Prevent default for game keys (arrows, space, tab, backspace, enter, escape)
                if (['ArrowUp','ArrowDown','ArrowLeft','ArrowRight','Space','Tab','Backspace','Enter','Escape'].includes(e.code)) {
                    e.preventDefault();
                }
            });

            canvas.addEventListener('keyup', (e) => {
                const key = keyMap[e.code];
                if (key !== undefined) {
                    keyStateQueue.push({ type: 'up', key });
                    keyEventQueue.push({ type: 'up', key });
                }
            });

            canvas.addEventListener('mousemove', (e) => {
                const rect = canvas.getBoundingClientRect();
                mouseX = e.clientX - rect.left;
                mouseY = e.clientY - rect.top;
            });

            canvas.addEventListener('mousedown', (e) => {
                const btn = mouseButtonMap[e.button];
                if (btn !== undefined) {
                    mouseStateQueue.push({ type: 'down', button: btn });
                    const rect = canvas.getBoundingClientRect();
                    mouseEventQueue.push({ type: 'down', button: btn, x: e.clientX - rect.left, y: e.clientY - rect.top });
                }
                e.preventDefault();
                canvas.focus();
                if (window.IntersectAudio) window.IntersectAudio.resume();
            });

            canvas.addEventListener('mouseup', (e) => {
                const btn = mouseButtonMap[e.button];
                if (btn !== undefined) {
                    mouseStateQueue.push({ type: 'up', button: btn });
                    const rect = canvas.getBoundingClientRect();
                    mouseEventQueue.push({ type: 'up', button: btn, x: e.clientX - rect.left, y: e.clientY - rect.top });
                }
                e.preventDefault();
            });

            canvas.addEventListener('wheel', (e) => {
                scrollDeltaX += e.deltaX;
                scrollDeltaY += e.deltaY;
                e.preventDefault();
            }, { passive: false });

            canvas.addEventListener('contextmenu', (e) => e.preventDefault());

            // Text input via direct key events
            canvas.addEventListener('keypress', (e) => {
                if (e.key.length === 1) {
                    textInputBuffer.push(e.key);
                }
            });

            // Handle focus/blur
            canvas.addEventListener('blur', () => {
                keysDown.clear();
                mouseButtons.clear();
                keyStateQueue = [];
                mouseStateQueue = [];
            });

            canvas.focus();
            return true;
        },

        // Called once per frame BEFORE game logic reads input.
        // 1. Save current state as "previous frame"
        // 2. Apply queued DOM events to current state
        // This ensures IsJustPressed (isDown && !wasDown) works correctly.
        update() {
            // Save current → prev
            keysPrev.clear();
            for (const k of keysDown) keysPrev.add(k);
            mouseButtonsPrev.clear();
            for (const b of mouseButtons) mouseButtonsPrev.add(b);

            // Apply queued key state changes to current
            for (const evt of keyStateQueue) {
                if (evt.type === 'down') keysDown.add(evt.key);
                else keysDown.delete(evt.key);
            }
            keyStateQueue = [];

            // Apply queued mouse state changes to current
            for (const evt of mouseStateQueue) {
                if (evt.type === 'down') mouseButtons.add(evt.button);
                else mouseButtons.delete(evt.button);
            }
            mouseStateQueue = [];
        },

        isKeyDown(key) { return keysDown.has(key); },
        wasKeyDown(key) { return keysPrev.has(key); },
        isMouseButtonDown(button) { return mouseButtons.has(button); },
        wasMouseButtonDown(button) { return mouseButtonsPrev.has(button); },
        getMouseX() { return mouseX; },
        getMouseY() { return mouseY; },

        getScrollDelta() {
            const dx = scrollDeltaX;
            const dy = scrollDeltaY;
            scrollDeltaX = 0;
            scrollDeltaY = 0;
            return { x: dx, y: dy };
        },

        getTextInput() {
            const buf = textInputBuffer;
            textInputBuffer = [];
            return buf;
        },

        // Get queued mouse events (prevents losing clicks between polls)
        getMouseEvents() {
            const events = mouseEventQueue;
            mouseEventQueue = [];
            return events;
        },

        // Get queued key events (prevents losing key presses between polls)
        getKeyEvents() {
            const events = keyEventQueue;
            keyEventQueue = [];
            return events;
        },

        isCanvasFocused() {
            return document.activeElement === canvas;
        },

        setCursor(cursorStyle) {
            if (canvas) canvas.style.cursor = cursorStyle;
        }
    };
})();
