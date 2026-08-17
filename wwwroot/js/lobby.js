(function () {
    'use strict';

    let connection = null;
    let pendingChallenge = null;

    const onlineContainer = document.getElementById('online-players');
    const onlineCount = document.getElementById('online-count');
    const gamesContainer = document.getElementById('lobby-games');
    const tcSelect = document.getElementById('time-control-select');
    const banner = document.getElementById('challenge-banner');
    const bannerBody = document.getElementById('challenge-banner-body');

    function getTimeControl() {
        const [min, inc] = tcSelect.value.split(',').map(Number);
        return { baseMinutes: min, incrementSeconds: inc };
    }

    function renderPlayers(users) {
        const me = window.__CURRENT_USER_ID__;
        const players = users.filter(u => u.userId !== me && u.name !== 'ChessBot');
        onlineCount.textContent = `${players.length} online`;
        if (players.length === 0) {
            onlineContainer.innerHTML = '<p class="text-sm text-slate-500 py-4 text-center">No other players online right now.</p>';
            return;
        }
        onlineContainer.innerHTML = players.map(u => `
            <div class="flex items-center justify-between px-4 py-3 rounded-xl bg-slate-800/60 border border-slate-800">
                <div class="flex items-center gap-3">
                    <span class="w-2 h-2 rounded-full bg-emerald-400 animate-pulse"></span>
                    <div>
                        <p class="font-medium text-sm text-slate-100">${escapeHtml(u.name)}</p>
                        <p class="text-xs text-slate-500">rating ${u.rating}</p>
                    </div>
                </div>
                <button data-challenge="${u.userId}" class="text-xs px-3 py-1.5 rounded-lg bg-emerald-500 hover:bg-emerald-400 text-slate-950 font-semibold transition">
                    Challenge
                </button>
            </div>`).join('');
    }

    function renderGames(games) {
        if (!games || games.length === 0) {
            gamesContainer.innerHTML = '<p class="text-sm text-slate-500 py-4 text-center">No games yet.</p>';
            return;
        }
        gamesContainer.innerHTML = games.map(g => `
            <a href="/Game/${g.status === 'Waiting' ? 'Play' : 'Watch'}?key=${g.gameKey}"
               class="block px-4 py-3 rounded-xl bg-slate-800/60 border border-slate-800 hover:border-emerald-500/50 transition">
                <p class="font-medium text-sm text-slate-100">${escapeHtml(g.whiteName)} <span class="text-slate-500 text-xs">vs</span> ${escapeHtml(g.blackName)}</p>
                <p class="text-xs text-slate-500 mt-1">${escapeHtml(g.timeControl)} &middot; ${g.status === 'Waiting' ? 'Waiting for opponent' : 'Live'} &middot; ${g.status === 'Active' ? 'Watch' : ''}</p>
            </a>`).join('');
    }

    function showChallengeBanner(ch) {
        pendingChallenge = ch;
        bannerBody.innerHTML = `
            <p class="text-sm text-slate-100"><span class="font-semibold">${escapeHtml(ch.fromName)}</span> challenges you to ${ch.baseMinutes}+${ch.incrementSeconds}</p>
            <div class="flex gap-2">
                <button id="accept-challenge" class="text-xs px-3 py-1.5 rounded-lg bg-emerald-500 hover:bg-emerald-400 text-slate-950 font-semibold">Accept</button>
                <button id="decline-challenge" class="text-xs px-3 py-1.5 rounded-lg border border-slate-600 hover:border-red-500 hover:text-red-400">Decline</button>
            </div>`;
        banner.classList.remove('hidden');
        document.getElementById('accept-challenge').addEventListener('click', async () => {
            const snap = await connection.invoke('AcceptChallenge', ch.gameKey);
            if (snap) {
                window.location.href = `/Game/Play?key=${snap.gameKey}`;
            }
        });
        document.getElementById('decline-challenge').addEventListener('click', async () => {
            await connection.invoke('DeclineChallenge', ch.gameKey);
            banner.classList.add('hidden');
            pendingChallenge = null;
        });
    }

    async function connect() {
        connection = initHub();
        connection.on('Presence', users => renderPlayers(users.users || []));
        connection.on('LobbyRefresh', ev => renderGames(ev.games || []));
        connection.on('ChallengeReceived', showChallengeBanner);
        connection.on('ChallengeAccepted', ev => {
            window.location.href = '/Game/Play?key=' + ev.gameKey;
        });

        connection.onclose(() => { /* automatic reconnect configured */ });

        try {
            await connection.start();
            const [users, games] = await Promise.all([
                connection.invoke('GetOnlineUsers'),
                connection.invoke('GetLobbyGames')
            ]);
            renderPlayers(users);
            renderGames(games);
        } catch (err) {
            onlineCount.textContent = 'offline';
            onlineContainer.innerHTML = `<p class="text-sm text-red-400 py-4 text-center">Connection failed: ${escapeHtml(err.message)}</p>`;
        }
    }

    document.addEventListener('click', async (e) => {
        const btn = e.target.closest('[data-challenge]');
        if (!btn) return;
        const tc = getTimeControl();
        const result = await connection.invoke('CreateChallenge', btn.dataset.challenge, tc.baseMinutes, tc.incrementSeconds);
        if (result && !result.ok) {
            showToast(result.error, 'error');
        } else {
            showToast('Challenge sent!', 'success');
        }
    });

    connect();
})();
