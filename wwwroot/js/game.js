(function () {
    'use strict';

    const snapEl = document.getElementById('snapshot-data');
    if (!snapEl) return;
    const pageData = JSON.parse(document.getElementById('page-data').textContent);
    let snap = JSON.parse(snapEl.textContent);
    const gameKey = snap.gameKey;
    const isPlayer = pageData.isPlayer;
    const isSpectator = pageData.isSpectator;
    const isBotGame = pageData.isBotGame;

    let connection = null;
    let chess = null;
    let board = null;
    let myTurn = false;
    let gameOver = snap.status === 'Ended' || snap.status === 'Aborted';
    let interactionLocked = false;
    let pendingPromo = null;

    const $boardWrap = $('#board-wrap');
    const boardEl = document.getElementById('board');

    /* ---------- helpers ---------- */

    function myColor() {
        return snap.isWhite ? 'w' : 'b';
    }

    function enemyName() {
        return snap.isWhite ? snap.blackName : snap.whiteName;
    }

    function enemyRating() {
        return snap.isWhite ? snap.blackRating : snap.whiteRating;
    }

    function updateInteraction() {
        $('#btn-resign').prop('disabled', !(isPlayer && !gameOver && snap.status === 'Active'));
        $('#btn-draw').prop('disabled', !(isPlayer && !gameOver && snap.status === 'Active'));
    }

    function renderClocks(whiteMs, blackMs, turn) {
        const myMs = snap.isWhite ? whiteMs : blackMs;
        const oppMs = snap.isWhite ? blackMs : whiteMs;
        const myLow = myMs < 30000;
        const oppLow = oppMs < 30000;
        const myClock = $('#my-clock');
        const oppClock = $('#opponent-clock');
        myClock.find('#my-clock-value').text(formatClock(myMs));
        oppClock.find('#opponent-clock-value').text(formatClock(oppMs));
        myClock.toggleClass('low-time', myLow);
        oppClock.toggleClass('low-time', oppLow);
        myClock.toggleClass('turn-active', turn === myColor() && snap.status === 'Active' && !gameOver);
        oppClock.toggleClass('turn-active', turn !== myColor() && snap.status === 'Active' && !gameOver);
    }

    function renderMoveList(moves) {
        const list = $('#move-list');
        const rows = [];
        for (let i = 0; i < moves.length; i += 2) {
            rows.push(`<div class="flex gap-2 text-sm">
                <span class="w-8 text-slate-500 text-right">${(i / 2) + 1}.</span>
                <span class="w-16 font-medium text-slate-100">${escapeHtml(moves[i])}</span>
                <span class="w-16 text-slate-300">${i + 1 < moves.length ? escapeHtml(moves[i + 1]) : ''}</span>
            </div>`);
        }
        list.html(rows.join(''));
        const el = list[0];
        if (el) el.scrollTop = el.scrollHeight;
    }

    function highlightLastMove(from, to) {
        $('.square-55d63.last-move').removeClass('last-move');
        if (from) $('.square-' + from).addClass('last-move');
        if (to) $('.square-' + to).addClass('last-move');
    }

    function showWaitingOverlay() {
        $('#waiting-overlay').removeClass('hidden');
        document.getElementById('share-link').textContent = window.location.origin + '/Game/Play?key=' + gameKey;
        $('#status-line').text('Waiting for your opponent to arrive.');
    }

    function applySnapshot(snapshot) {
        snap = snapshot;

        chess = new Chess(snapshot.fen);
        if (!board) {
            board = Chessboard('board', {
                position: snapshot.fen,
                orientation: snapshot.isWhite ? 'white' : 'black',
                draggable: true,
                pieceTheme: '/lib/chessboardjs/img/chesspieces/wikipedia/{piece}.png',
                onDrop: handleDrop,
                onDragStart: handleDragStart,
                showNotation: true
            });
        } else {
            board.position(snapshot.fen, false);
        }

        $('#opponent-name').text(enemyName());
        $('#opponent-rating').text('rating ' + enemyRating());
        $('#my-name').text(snapshot.isWhite ? snapshot.whiteName : snapshot.blackName);
        $('#my-rating').text('rating ' + (snapshot.isWhite ? snapshot.whiteRating : snapshot.blackRating));

        renderClocks(snapshot.whiteMs, snapshot.blackMs, chess.turn());
        renderMoveList(snapshot.moveHistory || []);

        gameOver = snapshot.status === 'Ended' || snapshot.status === 'Aborted';
        const wasPlayer = isPlayer;

        if (snapshot.status === 'Waiting' && isPlayer && !isBotGame) {
            showWaitingOverlay();
        } else {
            $('#waiting-overlay').addClass('hidden');
        }

        myTurn = isPlayer && !gameOver && chess.turn() === myColor() && snapshot.status === 'Active';

        if (gameOver) {
            showGameOver(snapshot.result, snapshot.reason);
        } else {
            $('#gameover-overlay').addClass('hidden');
        }

        if (snapshot.drawOfferByUserId) {
            if (snapshot.drawOfferByUserId !== pageData.currentUserId) {
                $('#draw-offer-box').removeClass('hidden');
                $('#draw-offered-info').addClass('hidden');
            } else {
                $('#draw-offer-box').addClass('hidden');
                $('#draw-offered-info').removeClass('hidden');
            }
        } else {
            $('#draw-offer-box').addClass('hidden');
            $('#draw-offered-info').addClass('hidden');
        }

        updateInteraction();
        resizeBoard();
    }

    function resizeBoard() {
        const w = $boardWrap.width() || 600;
        if (board) board.resize(Math.min(w, 640));
    }

    function showGameOver(result, reason) {
        gameOver = true;
        $('#waiting-overlay').addClass('hidden');
        $('#gameover-overlay').removeClass('hidden');

        let title = 'Game over';
        const winnerId = snap.winnerUserId;
        if (result === 'draw') {
            title = 'Draw';
        } else if (result === 'aborted') {
            title = 'Game cancelled';
        } else if (winnerId === pageData.currentUserId) {
            title = 'You won!';
        } else if (winnerId === snap.whiteUserId || winnerId === snap.blackUserId) {
            title = isPlayer ? 'You lost' : (winnerId === snap.whiteUserId ? snap.whiteName : snap.blackName) + ' won';
        }
        $('#gameover-title').text(title);
        $('#gameover-reason').text(reasonText(reason));
        $('#btn-rematch').toggleClass('hidden', !isPlayer || result === 'aborted');
        $('#btn-leave-tournament').toggleClass('hidden', !pageData.tournamentGame);
        $('#rematch-waiting').addClass('hidden');
    }

    function reasonText(reason) {
        switch (reason) {
            case 'checkmate': return 'by checkmate';
            case 'timeout': return 'on time';
            case 'resignation': return 'by resignation';
            case 'draw agreement': return 'by agreement';
            case 'draw': return 'by draw';
            case 'The game was cancelled.': return '';
            default: return '';
        }
    }

    /* ---------- drag & drop ---------- */

    function handleDragStart(source) {
        if (!isPlayer || gameOver || !myTurn || snap.status !== 'Active' || interactionLocked) return false;
        const piece = chess.get(source);
        return piece && piece.color === myColor();
    }

    function handleDrop(source, target) {
        if (source === target) return 'snapback';
        if (!isPlayer || gameOver || !myTurn || interactionLocked) {
            return 'snapback';
        }
        const moves = chess.moves({ square: source, verbose: true });
        const move = moves.find(m => m.to === target);
        if (!move) return 'snapback';

        if (move.promotion) {
            showPromotionPicker(source, target, move.promotion);
            return 'snapback';
        }

        interactionLocked = true;
        updateInteraction();
        connection.invoke('PlayMove', gameKey, source, target, null).then(outcome => {
            if (!outcome.ok) {
                showToast(outcome.error || 'Illegal move', 'error');
                board.position(chess.fen(), false);
                interactionLocked = false;
                updateInteraction();
            }
        }).catch(err => {
            showToast('Failed to send move: ' + err.message, 'error');
            interactionLocked = false;
            updateInteraction();
        });
        return undefined;
    }

    function showPromotionPicker(source, target) {
        pendingPromo = { source, target };
        const color = myColor();
        const pieces = ['q', 'r', 'b', 'n'];
        $('#promo-pieces').html(pieces.map(p => `
            <button data-promo="${p}" class="p-2 rounded-lg bg-slate-800 hover:bg-emerald-600 border border-slate-700 transition">
                <img src="/lib/chessboardjs/img/chesspieces/wikipedia/${color}${p.toUpperCase()}.png" class="w-12 h-12" alt="${p}" />
            </button>`).join(''));
        $('#promo-overlay').removeClass('hidden');
        $('#promo-pieces button').on('click', async function () {
            $('#promo-overlay').addClass('hidden');
            const promo = this.dataset.promo;
            interactionLocked = true;
            updateInteraction();
            try {
                const outcome = await connection.invoke('PlayMove', gameKey, pendingPromo.source, pendingPromo.target, promo);
                if (!outcome.ok) {
                    showToast(outcome.error || 'Illegal move', 'error');
                    board.position(chess.fen(), false);
                }
            } catch (err) {
                showToast('Failed to send move: ' + err.message, 'error');
            } finally {
                interactionLocked = false;
                updateInteraction();
            }
            pendingPromo = null;
        });
    }

    /* ---------- actions ---------- */

    function bindActions() {
        $('#btn-resign').on('click', async function () {
            if (!confirm('Resign the game?')) return;
            await connection.invoke('Resign', gameKey);
        });

        $('#btn-draw').on('click', async function () {
            const result = await connection.invoke('OfferDraw', gameKey);
            if (result && !result.ok) showToast(result.error, 'error');
            else {
                $('#draw-offered-info').removeClass('hidden');
                $('#draw-offer-box').addClass('hidden');
            }
        });

        $('#btn-accept-draw').on('click', async function () {
            const result = await connection.invoke('AcceptDraw', gameKey);
            if (result && !result.ok) showToast(result.error, 'error');
        });

        $('#btn-decline-draw').on('click', async function () {
            await connection.invoke('DeclineDraw', gameKey);
        });

        $('#btn-rematch').on('click', async function () {
            const snapshot = await connection.invoke('RequestRematch', gameKey);
            if (snapshot) {
                applySnapshot(snapshot);
            } else {
                $('#rematch-waiting').removeClass('hidden');
            }
        });

        $('#btn-leave-tournament').on('click', function () {
            window.location.href = '/Tournament/Detail?id=' + snap.dbGameId;
        });
    }

    /* ---------- hub ---------- */

    function registerHandlers() {
        connection.on('GameStarted', () => {
            snap.status = 'Active';
            $('#waiting-overlay').addClass('hidden');
            myTurn = isPlayer && chess.turn() === myColor();
            showToast('Game started! Good luck.', 'success');
            updateInteraction();
        });

        connection.on('MovePlayed', ev => {
            chess.load(ev.fen);
            board.position(ev.fen, false);
            highlightLastMove(ev.from, ev.to);
            renderClocks(ev.whiteMs, ev.blackMs, chess.turn());
            renderMoveList((snap.moveHistory || []).concat(ev.san));
            snap.moveHistory = (snap.moveHistory || []).concat(ev.san);
            snap.whiteMs = ev.whiteMs;
            snap.blackMs = ev.blackMs;
            myTurn = isPlayer && !gameOver && chess.turn() === myColor();
            interactionLocked = false;

            if (ev.isCheck) {
                const kingSquare = findKingSquare(chess.turn());
                if (kingSquare) $('.square-' + kingSquare).addClass('check-square');
                $('#status-line').text('Check!');
            } else {
                $('.check-square').removeClass('check-square');
                $('#status-line').text('');
            }
            updateInteraction();
        });

        connection.on('ClockTick', ev => {
            renderClocks(ev.whiteMs, ev.blackMs, ev.turn);
        });

        connection.on('GameOver', ev => {
            snap.status = 'Ended';
            snap.result = ev.result;
            snap.winnerUserId = ev.winnerUserId;
            snap.reason = ev.reason;
            snap.fen = ev.fen;
            if (chess) chess.load(ev.fen);
            showGameOver(ev.result, ev.reason);
            updateInteraction();
        });

        connection.on('DrawOffered', ev => {
            if (ev.drawOfferByUserId) {
                if (ev.drawOfferByUserId !== pageData.currentUserId) {
                    $('#draw-offer-box').removeClass('hidden');
                    $('#draw-offered-info').addClass('hidden');
                } else {
                    $('#draw-offer-box').addClass('hidden');
                    $('#draw-offered-info').removeClass('hidden');
                }
            } else {
                $('#draw-offer-box').addClass('hidden');
                $('#draw-offered-info').addClass('hidden');
            }
        });

        connection.on('PlayerDisconnected', ev => {
            if (isPlayer && !gameOver) {
                $('#status-line').text('Your opponent disconnected. Their clock is still running...');
            }
        });

        connection.on('SpectatorsChanged', ev => {
            $('#spectators-count').text('\u{1F465} ' + ev.count + ' watching');
        });

        connection.on('RematchRequested', () => {
            $('#rematch-waiting').removeClass('hidden');
        });

        connection.on('RematchStarted', async ev => {
            if (ev.newGameKey && ev.newGameKey !== gameKey) {
                const snapshot = await connection.invoke('JoinGame', ev.newGameKey);
                if (snapshot) applySnapshot(snapshot);
            }
        });

        connection.onreconnected(() => {
            if (isSpectator) {
                connection.invoke('SpectateGame', gameKey).then(s => s && applySnapshot(s));
            } else {
                connection.invoke('JoinGame', gameKey).then(s => s && applySnapshot(s));
            }
        });
    }

    function findKingSquare(turn) {
        const fen = chess.fen();
        const board = fen.split(' ')[0];
        const rows = board.split('/');
        const target = turn === 'w' ? 'K' : 'k';
        for (let r = 0; r < 8; r++) {
            let file = 0;
            for (const ch of rows[r]) {
                if (isNaN(ch)) {
                    if (ch === target) {
                        const sq = 'abcdefgh'[file] + (8 - r);
                        return sq;
                    }
                    file++;
                } else {
                    file += parseInt(ch, 10);
                }
            }
        }
        return null;
    }

    async function start() {
        bindActions();
        try {
            applySnapshot(snap);
        } catch (err) {
            showToast('Board setup failed: ' + err.message, 'error');
        }
        connection = initHub();
        registerHandlers();
        try {
            await connection.start();
        } catch (err) {
            showToast('Connection failed: ' + err.message, 'error');
            $('#status-line').text('Could not connect to the game server. Retrying...');
        }

        for (let attempt = 1; attempt <= 5; attempt++) {
            if (connection.state !== 'Connected') {
                await new Promise(r => setTimeout(r, 2000));
                continue;
            }
            try {
                const snapshot = isSpectator
                    ? await connection.invoke('SpectateGame', gameKey)
                    : await connection.invoke('JoinGame', gameKey);
                if (snapshot) {
                    applySnapshot(snapshot);
                    return;
                }
                $('#status-line').text('Joining game... (attempt ' + attempt + ' of 5)');
            } catch (err) {
                $('#status-line').text('Join failed, retrying (' + attempt + ' of 5): ' + err.message);
            }
            await new Promise(r => setTimeout(r, 2500));
        }
        showToast('Could not join the game. Please refresh the page.', 'error');
    }

    window.addEventListener('resize', resizeBoard);
    start();
})();
