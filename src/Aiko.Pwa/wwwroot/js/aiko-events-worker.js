// One event stream per project per browser, shared by every tab.
//
// The daemon serves plain HTTP on loopback, so the transport is HTTP/1.1 and the browser allows about six
// connections to that origin. A tab that holds its own EventSource spends one of them for as long as it is
// open, and those connections end with the tab: the sixth tab has none left, every request of every tab
// queues behind streams that never finish, and the app freezes. This worker owns the EventSource instead -
// a tab subscribes and unsubscribes, and the number of connections stops depending on the number of tabs.
//
// The worker also decides why a stream dropped. EventSource reconnects on its own and never says why, and
// the probe for the HTTP status is a second connection on the same address: run by every tab, one dropped
// stream turned into one extra connection per tab.
const channels = new Map();

self.onconnect = function (event) {
    const port = event.ports[0];
    port.onmessage = function (message) {
        const request = message.data || {};
        if (request.type === 'subscribe') {
            subscribe(request.url, port);
        } else if (request.type === 'unsubscribe') {
            unsubscribe(request.url, port);
        }
    };
    port.start();
};

// One channel per stream address: the single EventSource for it, and the tabs that listen to it.
function subscribe(url, port) {
    let channel = channels.get(url);
    if (!channel) {
        channel = { url: url, source: null, ports: new Set(), open: false, reason: null, probing: false };
        channels.set(url, channel);
    }
    channel.ports.add(port);
    if (!channel.source) {
        start(channel);
    } else if (channel.open) {
        // The stream is already up: this tab is live from this moment rather than from the next event.
        port.postMessage({ type: 'open' });
    } else if (channel.reason) {
        // The stream is down and this tab arrived mid-failure; it hears the reason the others already have.
        port.postMessage({ type: 'error', reason: channel.reason });
    }
}

function unsubscribe(url, port) {
    const channel = channels.get(url);
    if (!channel) {
        return;
    }
    channel.ports.delete(port);
    if (channel.ports.size === 0) {
        // Nobody is listening any more, so the stream goes with them: a browser with no tab open holds no
        // connection at all.
        stop(channel);
    }
}

function start(channel) {
    const source = new EventSource(channel.url);
    channel.source = source;
    source.onopen = function () {
        channel.open = true;
        channel.reason = null;
        broadcast(channel, { type: 'open' });
    };
    source.onmessage = function (message) {
        broadcast(channel, { type: 'message', data: message.data });
    };
    source.onerror = function () {
        channel.open = false;
        describe(channel);
    };
}

function stop(channel) {
    if (channel.source) {
        channel.source.close();
        channel.source = null;
    }
    channels.delete(channel.url);
}

function broadcast(channel, message) {
    channel.ports.forEach(function (port) {
        try {
            port.postMessage(message);
        } catch (error) {
            // A port whose page is gone cannot be told anything, and keeping it would hold the stream open
            // for a tab that no longer exists.
            channel.ports.delete(port);
        }
    });
    if (channel.ports.size === 0) {
        stop(channel);
    }
}

// EventSource reconnects by itself, so what the tabs need is the reason it failed: the HTTP status is the
// difference between "this browser is not paired" and "the daemon is gone". One probe answers that once for
// every tab, and only one probe runs at a time.
function describe(channel) {
    if (channel.probing || channel.ports.size === 0) {
        return;
    }
    channel.probing = true;
    const controller = new AbortController();
    const timer = setTimeout(function () { controller.abort(); }, 2000);
    fetch(channel.url, { headers: { 'Accept': 'text/event-stream' }, signal: controller.signal })
        .then(
            function (response) {
                clearTimeout(timer);
                // The body of an event stream never ends, so the probe is aborted as soon as its status is
                // known - otherwise the probe itself becomes a connection that never finishes.
                controller.abort();
                channel.probing = false;
                channel.reason = response.ok
                    ? 'The event stream dropped; the browser is reconnecting.'
                    : 'The daemon answered ' + response.status + ' for the event stream.';
                broadcast(channel, { type: 'error', reason: channel.reason });
            },
            function (error) {
                clearTimeout(timer);
                channel.probing = false;
                channel.reason = 'The daemon is not reachable: ' +
                    (error && error.message ? error.message : 'no reason given');
                broadcast(channel, { type: 'error', reason: channel.reason });
            });
}
