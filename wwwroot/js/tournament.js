(function () {
    'use strict';

    const container = document.getElementById('tournament-board');
    if (!container) return;
    const tournamentId = Number(container.dataset.tournamentId);

    const connection = initHub();
    let updating = false;

    async function refresh() {
        if (updating) return;
        updating = true;
        try {
            const res = await fetch('/Tournament/DetailPartial?id=' + tournamentId, {
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (res.ok) {
                const html = await res.text();
                if (container.innerHTML !== html) {
                    container.innerHTML = html;
                }
            }
        } catch (err) {
            /* keep last known content; retry on next poll */
        } finally {
            updating = false;
        }
    }

    connection.on('TournamentUpdate', id => {
        if (Number(id) === tournamentId) refresh();
    });

    connection.start().catch(() => { /* polling still works */ });

    setInterval(refresh, 15000);
    refresh();
})();