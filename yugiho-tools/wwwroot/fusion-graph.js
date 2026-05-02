let _mermaidReady = false;

async function _ensureMermaid() {
    if (_mermaidReady) return;

    await new Promise((resolve, reject) => {
        if (window.mermaid) { resolve(); return; }
        const s = document.createElement('script');
        s.src = 'https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.min.js';
        s.onload = resolve;
        s.onerror = reject;
        document.head.appendChild(s);
    });

    window.mermaid.initialize({
        startOnLoad: false,
        theme: 'base',
        fontFamily: 'Roboto, sans-serif',
        flowchart: { curve: 'basis', htmlLabels: false, padding: 20 },
        themeVariables: {
            darkMode: true,
            background:           '#0d0d1a',
            primaryColor:         '#1a1a3e',
            primaryTextColor:     '#e8e8e8',
            primaryBorderColor:   '#4a4aae',
            lineColor:            '#c8a415',
            secondaryColor:       '#141428',
            tertiaryColor:        '#0d0d1a',
            edgeLabelBackground:  '#0d0d1a',
            clusterBkg:           '#141428',
        }
    });

    _mermaidReady = true;
}

window.mermaidRender = async function (elementId, code) {
    await _ensureMermaid();
    const el = document.getElementById(elementId);
    if (!el) return;

    try {
        const uid = 'mgr_' + elementId + '_' + Date.now();
        const { svg } = await window.mermaid.render(uid, code);
        el.innerHTML = svg;
        const svgEl = el.querySelector('svg');
        if (svgEl) {
            svgEl.removeAttribute('height');
            svgEl.style.width  = '100%';
            svgEl.style.height = 'auto';
        }
    } catch (e) {
        el.innerHTML = '<p style="color:#666;padding:1rem;">Não foi possível renderizar o grafo.</p>';
        console.warn('mermaid render error:', e);
    }
};
