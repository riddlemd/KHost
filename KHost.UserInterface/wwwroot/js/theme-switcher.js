window.availableThemes = [];

window.loadTheme = function (themeName) {
    console.info(`Loading theme ${themeName}`)

    const themeLink = document.querySelector('.kh-theme-link');
    if (!themeLink) return;

    if (window.availableThemes.length > 0 && !window.availableThemes.includes(themeName)) {
        console.warn(`[Theme] Invalid theme: ${themeName}, defaulting to grape`);
        themeName = 'grape';
    }

    themeLink.href = `/css/themes/${themeName}.css`;
    localStorage.setItem('khost-theme', themeName);
};

window.themeInit = function() {
    const select = document.querySelector('.kh-theme-select');
    if (!select) {
        console.warn('[Theme Init] Select element not found');
        return;
    }

    let savedTheme = localStorage.getItem('khost-theme') || 'grape';

    const populateThemes = (themes) => {
        select.innerHTML = '';
        themes.forEach(theme => {
            const opt = document.createElement('option');
            opt.value = theme;
            opt.textContent = theme.charAt(0).toUpperCase() + theme.slice(1);
            select.appendChild(opt);
        });

        if (!themes.includes(savedTheme)) {
            console.warn(`[Theme Init] Saved theme "${savedTheme}" not found, defaulting to first available`);
            savedTheme = themes[0] || 'grape';
            localStorage.setItem('khost-theme', savedTheme);
        }

        select.value = savedTheme;
        window.loadTheme(savedTheme);
    };

    const setupChangeListener = () => {
        select.addEventListener('change', (e) => {
            const themeName = e.target.value;
            console.log('[Theme Init] Changed to:', themeName);
            window.loadTheme(themeName);
        });
    };

    try {
        fetch('/api/themes')
            .then(r => r.json())
            .then(themes => {
                if (!Array.isArray(themes) || themes.length === 0) {
                    console.warn('[Theme Init] No themes from API');
                    return;
                }
                window.availableThemes = themes;
                populateThemes(themes);
                setupChangeListener();
            })
            .catch(err => {
                console.error('[Theme Init] Failed to fetch themes:', err);
            });
    } catch (e) {
        console.error('[Theme Init] Exception:', e);
    }
};
