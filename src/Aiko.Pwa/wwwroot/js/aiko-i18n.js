// Localization helpers for the parts of the page that exist before .NET does.
//
// The UI language itself is resolved in C# (Program.cs) from the list and the saved choice this file
// publishes, because resource lookup lives there. What cannot wait for .NET is the document language
// and Blazor's own error bar: index.html is static, so both are settled here, on the first frame.

// The browser's ordered language preference, for the .NET side to pick a UI culture from.
window.aikoLanguages = function () {
    var list = navigator.languages && navigator.languages.length
        ? navigator.languages
        : [navigator.language || ''];
    return list.filter(Boolean).join(',');
};

// The language chosen in the interface, if any: the same local-storage key the switcher writes and
// the .NET side reads, so the document language set here and the culture set there cannot disagree.
// Storage may be unavailable at all - private mode throws on access - which reads as "none chosen".
window.aikoSavedLanguage = function () {
    try {
        return window.localStorage.getItem('aiko-language') || '';
    } catch (e) {
        return '';
    }
};

// Corrects the document language once .NET has resolved the culture the UI actually renders in.
window.aikoSetLanguage = function (language) {
    if (language) {
        document.documentElement.lang = language;
    }
};

(function () {
    // The languages this app ships. English is the neutral fallback: a visitor who reads neither gets
    // English rather than raw resource keys. Keep in step with Loc.resx / Loc.<culture>.resx and with
    // UiLanguages.Supported - a spec fails when the three drift apart.
    var shipped = ['en', 'ru'];
    var errorBar = {
        en: { text: 'The interface has stopped.', action: 'Reload' },
        ru: { text: 'Интерфейс остановлен.', action: 'Перезагрузить' }
    };

    // The precedence the .NET side applies as well: the saved choice, then the browser's preferences,
    // then English. A saved language we no longer ship is skipped, not honoured.
    var languages = window.aikoLanguages().split(',');
    var chosen = 'en';
    var saved = (window.aikoSavedLanguage() || '').trim().toLowerCase().split('-')[0];
    if (shipped.indexOf(saved) >= 0) {
        chosen = saved;
    } else {
        for (var i = 0; i < languages.length; i++) {
            var base = languages[i].trim().toLowerCase().split('-')[0];
            if (shipped.indexOf(base) >= 0) {
                chosen = base;
                break;
            }
        }
    }

    // Nothing below the body has been parsed yet when this runs from the head, so the bar is filled
    // only if it is already there.
    var text = document.querySelector('[data-aiko-error-text]');
    var action = document.querySelector('[data-aiko-error-action]');
    if (text) { text.textContent = errorBar[chosen].text; }
    if (action) { action.textContent = errorBar[chosen].action; }
    document.documentElement.lang = chosen;
})();

