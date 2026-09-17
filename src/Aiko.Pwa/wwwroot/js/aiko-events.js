window.aikoEvents = {
    connect: function (url, dotnet) {
        const source = new EventSource(url);
        source.onmessage = function (message) {
            dotnet.invokeMethodAsync('OnEvent', message.data);
        };
        return source;
    },
    close: function (source) {
        if (source) {
            source.close();
        }
    }
};

// Focuses the element matched by `selector` when the visitor presses Ctrl+K (Cmd+K on macOS).
// The quick search sits in the shell's top bar, and reaching it from anywhere is the part of the
// design's command palette no component owns: Flare handles keys inside its own controls, not the
// document's. Registers once, whoever asks.
window.aikoHotkey = function (selector, key) {
    if (window.aikoHotkeyBound) {
        return;
    }
    window.aikoHotkeyBound = true;
    document.addEventListener('keydown', function (event) {
        if (!(event.ctrlKey || event.metaKey) || event.altKey) {
            return;
        }
        if (event.key.toLowerCase() !== key.toLowerCase()) {
            return;
        }
        const field = document.querySelector(selector);
        if (!field) {
            return;
        }
        event.preventDefault();
        field.focus();
        if (field.select) {
            field.select();
        }
    });
};

// Pairs the local browser with the daemon using a one-time code from the URL fragment
// (#pair=<code>), then reloads the page with the resulting HttpOnly session cookie.
window.aikoPair = function () {
    var match = location.hash.match(/pair=([^&]+)/);
    if (!match) {
        return;
    }
    fetch('/api/v1/auth/pair', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ code: decodeURIComponent(match[1]) })
    }).then(function (response) {
        if (response.ok) {
            history.replaceState(null, '', location.pathname + location.search);
            location.reload();
        }
    }).catch(function () {});
};

if (location.hash.indexOf('pair=') >= 0) {
    window.aikoPair();
}
