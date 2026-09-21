// The stream is shared by every tab where the browser can host a SharedWorker, and held per tab where it
// cannot. Five tabs with a stream each exhausted the browser's connections to the daemon - the daemon serves
// plain HTTP, so the transport is HTTP/1.1 with about six connections per origin - and the app froze; the
// shared worker is the fix, and the per-tab stream is what remains for a private window or an older engine.
window.aikoEvents = {
    connect: function (url, dotnet) {
        const worker = sharedWorker();
        if (worker) {
            return connectThroughWorker(worker, url, dotnet);
        }
        return connectDirectly(url, dotnet);
    },
    close: function (handle) {
        if (handle && handle.close) {
            handle.close();
        }
    }
};

// A SharedWorker belongs to the origin rather than to a tab, so the stream it holds is the only one to that
// address no matter how many tabs ask for it. Nothing else about it is special: a browser that refuses to
// create one returns null here and gets the per-tab stream below.
function sharedWorker() {
    if (typeof SharedWorker !== 'function') {
        return null;
    }
    try {
        return new SharedWorker('js/aiko-events-worker.js', { name: 'aiko-events' });
    } catch (error) {
        // A private window or an engine that has no SharedWorker: not a failure, just the fallback.
        return null;
    }
}

function connectThroughWorker(worker, url, dotnet) {
    const port = worker.port;
    port.onmessage = function (message) {
        const data = message.data || {};
        if (data.type === 'open') {
            // The worker's stream is genuinely open; the tabs share it, so every tab is told at once.
            dotnet.invokeMethodAsync('OnConnectionChanged', true, null).catch(function () {});
        } else if (data.type === 'message') {
            dotnet.invokeMethodAsync('OnEvent', data.data);
        } else if (data.type === 'error') {
            // Interop failures are swallowed on their own: reporting them here would blame the network for a
            // circuit that has already gone away.
            dotnet.invokeMethodAsync('OnConnectionChanged', false, data.reason).catch(function () {});
        }
    };
    port.start();
    port.postMessage({ type: 'subscribe', url: url });
    return {
        close: function () {
            try {
                port.postMessage({ type: 'unsubscribe', url: url });
            } catch (error) {
                // The worker is already gone; there is nothing left to unsubscribe from.
            }
            port.onmessage = null;
        }
    };
}

function connectDirectly(url, dotnet) {
    const source = new EventSource(url);
    source.onopen = function () {
        // The stream is genuinely open now; before this callback the connection was only requested.
        dotnet.invokeMethodAsync('OnConnectionChanged', true, null).catch(function () {});
    };
    source.onmessage = function (message) {
        dotnet.invokeMethodAsync('OnEvent', message.data);
    };
    source.onerror = function () {
        // EventSource reconnects on its own and never says why it failed, so the cause is fetched once: the
        // HTTP status of the stream is the difference between "this browser is not paired" and "the daemon is
        // gone", and the shell shows which of the two it is instead of going quiet. The shared worker asks
        // the same question once for every tab; this path is the one that has no worker to ask it.
        const controller = new AbortController();
        const timer = setTimeout(function () { controller.abort(); }, 2000);
        fetch(url, { headers: { 'Accept': 'text/event-stream' }, signal: controller.signal })
            .then(
                function (response) {
                    clearTimeout(timer);
                    controller.abort();
                    const reason = response.ok
                        ? 'The event stream dropped; the browser is reconnecting.'
                        : 'The daemon answered ' + response.status + ' for the event stream.';
                    return dotnet
                        .invokeMethodAsync('OnConnectionChanged', false, reason)
                        .catch(function () {});
                },
                function (error) {
                    clearTimeout(timer);
                    return dotnet
                        .invokeMethodAsync(
                            'OnConnectionChanged',
                            false,
                            'The daemon is not reachable: ' +
                                (error && error.message ? error.message : 'no reason given'))
                        .catch(function () {});
                });
    };
    return {
        close: function () {
            source.close();
        }
    };
}

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
