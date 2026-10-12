/* ==========================================================================
   站点交互（无依赖）
   1) 移动端导航开关
   2) 滚动进入视口的淡入动效（IntersectionObserver）
   3) 页脚年份
   4) 锚点平滑滚动兜底（不支持 scroll-behavior 的旧浏览器）
   5) 代码块一键复制
   ========================================================================== */
(function () {
  'use strict';

  /* ------------------------------------------------ 1. 移动端导航 */
  var toggle = document.querySelector('.nav-toggle');
  var nav = document.getElementById('site-nav');
  if (toggle && nav) {
    toggle.addEventListener('click', function () {
      var open = nav.classList.toggle('is-open');
      toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
    });
    nav.addEventListener('click', function (e) {
      if (e.target.tagName === 'A') {
        nav.classList.remove('is-open');
        toggle.setAttribute('aria-expanded', 'false');
      }
    });
  }

  /* ------------------------------------------- 2. 滚动淡入动效 */
  var items = document.querySelectorAll('.reveal');
  if ('IntersectionObserver' in window && items.length) {
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting) {
          entry.target.classList.add('is-visible');
          io.unobserve(entry.target);
        }
      });
    }, { rootMargin: '0px 0px -8% 0px', threshold: 0.08 });
    items.forEach(function (el) { io.observe(el); });
  } else {
    items.forEach(function (el) { el.classList.add('is-visible'); });
  }

  /* ------------------------------------------------ 3. 页脚年份 */
  var year = document.getElementById('year');
  if (year) { year.textContent = new Date().getFullYear(); }

  /* ------------------------------------------ 4. 平滑滚动兜底 */
  if (!('scrollBehavior' in document.documentElement.style)) {
    document.querySelectorAll('a[href^="#"]').forEach(function (a) {
      a.addEventListener('click', function (e) {
        var target = document.querySelector(a.getAttribute('href'));
        if (!target) { return; }
        e.preventDefault();
        window.scrollTo(0, target.getBoundingClientRect().top + window.pageYOffset - 76);
      });
    });
  }

  /* -------------------------------------------- 5. 复制代码块 */
  document.querySelectorAll('[data-copy]').forEach(function (btn) {
    btn.addEventListener('click', function () {
      var sel = btn.getAttribute('data-copy');
      var node = document.querySelector(sel);
      if (!node) { return; }
      var text = node.innerText;
      var done = function () {
        var old = btn.textContent;
        btn.textContent = btn.getAttribute('data-copied') || 'Copied';
        setTimeout(function () { btn.textContent = old; }, 1600);
      };
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).then(done, done);
      } else {
        var ta = document.createElement('textarea');
        ta.value = text; document.body.appendChild(ta); ta.select();
        try { document.execCommand('copy'); } catch (err) { /* 忽略 */ }
        document.body.removeChild(ta); done();
      }
    });
  });
})();
