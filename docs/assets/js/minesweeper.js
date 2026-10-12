/* ==========================================================================
   网页版扫雷试玩 —— 规则与桌面版《扫雷 2.0》保持一致：
     · 首次点击必定安全（不会踩雷，且首点的 8 邻格也没有雷）
     · 右键三态循环：旗 → 问号 → 空（触屏用「旗标模式」按钮代替右键）
     · 双击（或用匹配的旗数）已翻开的数字格 = chord 批量展开；旗插错会炸
     · 计时从第一次翻开开始；剩余雷数 = 总雷数 − 已插旗数
     · 胜利时自动把剩下的雷全部插旗
   纯原生 JS，无依赖。
   ========================================================================== */
(function () {
  'use strict';

  var boardEl = document.getElementById('gm-board');
  if (!boardEl) { return; }

  var minesEl = document.getElementById('gm-mines');
  var timeEl = document.getElementById('gm-time');
  var resetEl = document.getElementById('gm-reset');
  var flagBtn = document.getElementById('gm-flagmode');
  var msgEl = document.getElementById('gm-msg');
  var diffBtns = Array.prototype.slice.call(document.querySelectorAll('[data-diff]'));

  var IS_ZH = (document.documentElement.lang || 'zh').toLowerCase().indexOf('zh') === 0;
  var T = IS_ZH ? {
    win: function (s) { return '🎉 通关！用时 ' + s + ' 秒'; },
    lose: '💥 踩雷了 —— 点笑脸重开一局',
    flagOn: '🚩 旗标模式：左键插旗',
    flagOff: '🚩 旗标模式',
    ready: '左键翻开第一格开始计时。'
  } : {
    win: function (s) { return '🎉 Cleared in ' + s + 's'; },
    lose: '💥 Boom — hit the smiley to play again',
    flagOn: '🚩 Flag mode: ON',
    flagOff: '🚩 Flag mode',
    ready: 'Left-click any cell to start the timer.'
  };

  var DIFF = {
    beginner: { w: 9, h: 9, m: 10 },
    intermediate: { w: 16, h: 16, m: 40 },
    expert: { w: 30, h: 16, m: 99 }
  };

  var S = {
    key: 'beginner', w: 9, h: 9, mines: 10,
    mine: null, adj: null, open: null, mark: null,
    els: [], started: false, over: false, revealed: 0, flags: 0,
    t0: 0, timer: null, flagMode: false
  };

  /* ------------------------------------------------------------ 工具 */
  function pad3(n) { n = Math.max(-99, Math.min(999, n)); var s = String(Math.abs(n)); while (s.length < 3) { s = '0' + s; } return (n < 0 ? '-' : '') + s; }

  function idx(x, y) { return y * S.w + x; }

  function neighbors(i) {
    var x = i % S.w, y = (i - x) / S.w, out = [];
    for (var dy = -1; dy <= 1; dy++) {
      for (var dx = -1; dx <= 1; dx++) {
        if (!dx && !dy) { continue; }
        var nx = x + dx, ny = y + dy;
        if (nx >= 0 && nx < S.w && ny >= 0 && ny < S.h) { out.push(idx(nx, ny)); }
      }
    }
    return out;
  }

  /* -------------------------------------------------- 新局与布雷 */
  function newGame(key) {
    if (key && DIFF[key]) { S.key = key; }
    var d = DIFF[S.key];
    S.w = d.w; S.h = d.h; S.mines = d.m;
    var total = S.w * S.h;

    S.mine = new Uint8Array(total);
    S.adj = new Uint8Array(total);
    S.open = new Uint8Array(total);
    S.mark = new Uint8Array(total);   // 0 空 / 1 旗 / 2 问号
    S.started = false; S.over = false; S.revealed = 0; S.flags = 0;
    S.t0 = 0;
    stopTimer();

    diffBtns.forEach(function (b) { b.classList.toggle('is-active', b.getAttribute('data-diff') === S.key); });
    if (resetEl) { resetEl.textContent = '🙂'; }
    if (msgEl) { msgEl.className = 'game-msg'; msgEl.textContent = T.ready; }
    if (timeEl) { timeEl.textContent = pad3(0); }

    buildDom();
    layout();
    paint();
  }

  function placeMines(safeIndex) {
    var total = S.w * S.h, banned = {};
    banned[safeIndex] = 1;
    neighbors(safeIndex).forEach(function (n) { banned[n] = 1; });

    var pool = [];
    for (var i = 0; i < total; i++) { if (!banned[i]) { pool.push(i); } }
    // Fisher–Yates 部分洗牌，取前 mines 个
    for (var k = 0; k < S.mines && k < pool.length; k++) {
      var j = k + Math.floor(Math.random() * (pool.length - k));
      var t = pool[k]; pool[k] = pool[j]; pool[j] = t;
      S.mine[pool[k]] = 1;
    }
    // 邻雷数
    for (var i2 = 0; i2 < total; i2++) {
      if (S.mine[i2]) { S.adj[i2] = 0; continue; }
      var c = 0, nb = neighbors(i2);
      for (var n = 0; n < nb.length; n++) { if (S.mine[nb[n]]) { c++; } }
      S.adj[i2] = c;
    }
  }

  /* ------------------------------------------------------------ DOM */
  function buildDom() {
    boardEl.innerHTML = '';
    boardEl.style.setProperty('--cols', S.w);
    S.els = new Array(S.w * S.h);
    var frag = document.createDocumentFragment();
    for (var i = 0; i < S.w * S.h; i++) {
      var c = document.createElement('div');
      c.className = 'cell';
      c.setAttribute('role', 'gridcell');
      c.dataset.i = i;
      S.els[i] = c;
      frag.appendChild(c);
    }
    boardEl.appendChild(frag);
  }

  function layout() {
    var wrap = boardEl.parentElement;
    var avail = (wrap ? wrap.clientWidth : 640) - 10;
    var gap = S.w > 20 ? 2 : 3;
    var cell = Math.floor((avail - (S.w - 1) * gap) / S.w);
    cell = Math.max(16, Math.min(34, cell));
    boardEl.style.setProperty('--cell', cell + 'px');
    boardEl.style.setProperty('--gap', gap + 'px');
  }

  function paint() {
    for (var i = 0; i < S.els.length; i++) {
      var el = S.els[i], cls = 'cell', txt = '';
      if (S.open[i]) {
        cls += ' revealed';
        if (S.mine[i]) { cls += ' mine'; txt = '💣'; }
        else if (S.adj[i] > 0) { cls += ' n' + S.adj[i]; txt = String(S.adj[i]); }
      } else if (S.mark[i] === 1) {
        cls += ' flag'; txt = '🚩';
      } else if (S.mark[i] === 2) {
        cls += ' q'; txt = '?';
      }
      if (el.className !== cls) { el.className = cls; }
      if (el.textContent !== txt) { el.textContent = txt; }
    }
    if (minesEl) { minesEl.textContent = pad3(S.mines - S.flags); }
  }

  /* ------------------------------------------------------------ 计时 */
  function startTimer() {
    S.t0 = Date.now();
    stopTimer();
    S.timer = setInterval(function () {
      if (timeEl) { timeEl.textContent = pad3(Math.floor((Date.now() - S.t0) / 1000)); }
    }, 250);
  }
  function stopTimer() { if (S.timer) { clearInterval(S.timer); S.timer = null; } }

  /* ------------------------------------------------------- 翻开/插旗 */
  function reveal(i) {
    if (S.over || S.open[i] || S.mark[i] === 1) { return; }
    if (!S.started) { S.started = true; placeMines(i); startTimer(); }

    if (S.mine[i]) { S.open[i] = 1; boom(i); return; }

    // 迭代式洪水填充（和桌面版一样，绝不递归）
    var stack = [i];
    while (stack.length) {
      var cur = stack.pop();
      if (S.open[cur] || S.mark[cur] === 1) { continue; }
      if (S.mine[cur]) { continue; }
      if (!S.open[cur]) { S.open[cur] = 1; S.revealed++; }
      if (S.adj[cur] === 0) {
        var nb = neighbors(cur);
        for (var n = 0; n < nb.length; n++) {
          if (!S.open[nb[n]] && S.mark[nb[n]] !== 1) { stack.push(nb[n]); }
        }
      }
    }
    paint();
    checkWin();
  }

  function toggleMark(i) {
    if (S.over || S.open[i]) { return; }
    if (!S.started) { /* 首点前也允许插旗，和桌面版一致 */ }
    var m = S.mark[i];
    if (m === 0) { S.mark[i] = 1; S.flags++; }
    else if (m === 1) { S.mark[i] = 2; S.flags--; }
    else { S.mark[i] = 0; }
    paint();
  }

  function chord(i) {
    if (S.over || !S.open[i] || S.adj[i] === 0) { return; }
    var nb = neighbors(i), f = 0;
    for (var n = 0; n < nb.length; n++) { if (S.mark[nb[n]] === 1) { f++; } }
    if (f !== S.adj[i]) { return; }               // 旗数与数字不符 → 不展开（和桌面版一致）
    for (var k = 0; k < nb.length; k++) {
      var t = nb[k];
      if (S.mark[t] === 1 || S.open[t]) { continue; }
      if (S.mine[t]) { S.open[t] = 1; boom(t); return; }   // 旗插错 → 炸
      reveal(t);
      if (S.over) { return; }
    }
  }

  function boom(i) {
    S.over = true; stopTimer();
    for (var k = 0; k < S.els.length; k++) {
      if (S.mine[k] && S.mark[k] !== 1) { S.open[k] = 1; }
      if (!S.mine[k] && S.mark[k] === 1) { S.mark[k] = 3; }   // 插错的旗
    }
    paint();
    if (S.els[i]) { S.els[i].classList.add('boom'); }
    for (var m = 0; m < S.els.length; m++) { if (S.mark[m] === 3) { S.els[m].className = 'cell revealed wrong'; S.els[m].textContent = '❌'; } }
    if (resetEl) { resetEl.textContent = '😵'; }
    if (msgEl) { msgEl.className = 'game-msg is-lose'; msgEl.textContent = T.lose; }
  }

  function checkWin() {
    if (S.revealed !== S.w * S.h - S.mines) { return; }
    S.over = true; stopTimer();
    for (var i = 0; i < S.els.length; i++) {
      if (S.mine[i] && S.mark[i] !== 1) { S.mark[i] = 1; S.flags++; }
    }
    paint();
    var secs = Math.floor((Date.now() - S.t0) / 1000);
    if (resetEl) { resetEl.textContent = '😎'; }
    if (msgEl) { msgEl.className = 'game-msg is-win'; msgEl.textContent = T.win(secs); }
  }

  /* ------------------------------------------------------------ 事件 */
  boardEl.addEventListener('contextmenu', function (e) { e.preventDefault(); });

  boardEl.addEventListener('mousedown', function (e) {
    var el = e.target.closest ? e.target.closest('.cell') : null;
    if (!el) { return; }
    var i = parseInt(el.dataset.i, 10);
    if (e.button === 0) {
      if (S.flagMode) { toggleMark(i); } else { reveal(i); }
    } else if (e.button === 2) {
      toggleMark(i);
    }
  });

  // 双击 chord（自研判定：同一格、已翻开）
  var lastTap = { i: -1, t: 0 };
  boardEl.addEventListener('click', function (e) {
    var el = e.target.closest ? e.target.closest('.cell') : null;
    if (!el) { return; }
    var i = parseInt(el.dataset.i, 10);
    var now = Date.now();
    if (lastTap.i === i && now - lastTap.t < 320 && S.open[i]) { chord(i); lastTap.i = -1; return; }
    lastTap.i = i; lastTap.t = now;
  });

  // 触屏：长按插旗（200ms 以上），短按翻开
  var touchTimer = null, touchMoved = false;
  boardEl.addEventListener('touchstart', function (e) {
    var el = e.target.closest ? e.target.closest('.cell') : null;
    if (!el) { return; }
    var i = parseInt(el.dataset.i, 10);
    touchMoved = false;
    touchTimer = setTimeout(function () { toggleMark(i); touchTimer = null; }, 380);
  }, { passive: true });
  boardEl.addEventListener('touchmove', function () { touchMoved = true; }, { passive: true });
  boardEl.addEventListener('touchend', function (e) {
    var el = e.target.closest ? e.target.closest('.cell') : null;
    if (touchTimer) {
      clearTimeout(touchTimer); touchTimer = null;
      if (el && !touchMoved) {
        var i = parseInt(el.dataset.i, 10);
        if (S.open[i]) { chord(i); } else { reveal(i); }
      }
    }
  });

  if (resetEl) { resetEl.addEventListener('click', function () { newGame(); }); }

  if (flagBtn) {
    flagBtn.addEventListener('click', function () {
      S.flagMode = !S.flagMode;
      flagBtn.classList.toggle('is-on', S.flagMode);
      flagBtn.textContent = S.flagMode ? T.flagOn : T.flagOff;
    });
  }

  diffBtns.forEach(function (b) {
    b.addEventListener('click', function () { newGame(b.getAttribute('data-diff')); });
  });

  var rz;
  window.addEventListener('resize', function () {
    clearTimeout(rz);
    rz = setTimeout(function () { layout(); }, 120);
  });

  /* ------------------------------------------------------------ 启动 */
  newGame('beginner');
})();
