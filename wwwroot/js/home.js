(function () {
    'use strict';

    let connection = null;

    function renderGames(games) {
        const container = document.getElementById('home-live-games');
        if (!container) return;
        const noGames = document.getElementById('home-no-games');
        if (!games || games.length === 0) {
            if (noGames) noGames.style.display = '';
            else container.innerHTML = '<p class="text-sm text-slate-500 py-4 text-center">No live games right now.</p>';
            return;
        }
        if (noGames) noGames.style.display = 'none';
        container.innerHTML = games.map(g => `
            <a href="/Game/${g.status === 'Waiting' ? 'Play' : 'Watch'}?key=${g.gameKey}"
               class="flex items-center justify-between px-4 py-3 rounded-xl bg-slate-800/60 border border-slate-800 hover:border-emerald-500/50 transition group">
                <div>
                    <p class="font-medium text-sm text-slate-100">${escapeHtml(g.whiteName)} <span class="text-slate-500 text-xs">vs</span> ${escapeHtml(g.blackName)}</p>
                    <p class="text-xs text-slate-500">${escapeHtml(g.timeControl)}</p>
                </div>
                <span class="text-xs px-2 py-1 rounded-md ${g.status === 'Active' ? 'bg-emerald-500/15 text-emerald-400' : 'bg-amber-500/15 text-amber-400'}">${g.status === 'Active' ? 'Live &rarr;' : 'Waiting &rarr;'}</span>
            </a>`).join('');
    }

    async function connect() {
        connection = initHub();
        connection.on('LobbyRefresh', ev => renderGames(ev.games || []));
        try {
            await connection.start();
            const games = await connection.invoke('GetLobbyGames');
            renderGames(games);
        } catch (err) {
            /* presence is a nice-to-have on the landing page */
        }
    }

    connect();
})();
