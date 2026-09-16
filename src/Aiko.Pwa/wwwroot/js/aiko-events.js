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
