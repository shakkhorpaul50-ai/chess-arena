function showToast(message, type) {
    const root = document.getElementById('toast-root');
    if (!root) return;
    const colors = {
        success: 'border-emerald-500 bg-emerald-950/95 text-emerald-100',
        error: 'border-red-500 bg-red-950/95 text-red-100',
        info: 'border-sky-500 bg-sky-950/95 text-sky-100'
    };
    const el = document.createElement('div');
    el.className = `toast-item border px-4 py-3 rounded-lg shadow-lg text-sm max-w-sm ${colors[type] || colors.info}`;
    el.textContent = message;
    root.appendChild(el);
    setTimeout(() => {
        el.style.opacity = '0';
        el.style.transition = 'opacity 0.3s';
        setTimeout(() => el.remove(), 320);
    }, 4000);
}

function formatClock(ms) {
    if (ms == null || ms < 0) ms = 0;
    const totalSeconds = Math.ceil(ms / 1000);
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    return `${minutes}:${seconds.toString().padStart(2, '0')}`;
}

function escapeHtml(str) {
    if (!str) return '';
    const div = document.createElement('div');
    div.textContent = str;
    return div.innerHTML;
}

function initHub(baseUrl) {
    const connection = new signalR.HubConnectionBuilder()
        .withUrl((baseUrl || '') + '/hubs/game')
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();
    return connection;
}

window.showToast = showToast;
window.formatClock = formatClock;
window.escapeHtml = escapeHtml;
window.initHub = initHub;
