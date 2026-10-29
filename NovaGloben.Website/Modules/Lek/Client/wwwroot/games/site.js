// The Lek site a game page belongs to. A game page is a static file served from the module's library, the same address
// on every site of the host, so the frame that opens it says which site it is in (?base=/lek). Every address a game
// reaches - its hub, its API, a page of the site, an invite link - and every key it keeps in the browser goes through
// here. Links in the page that point at the site carry data-site (and data-return for a sign-in's way back).
(function () {
  var raw = new URLSearchParams(location.search).get('base') || '';
  // only a folder name: never a host, a scheme or a path that climbs
  var base = /^\/[a-z0-9-]{1,40}$/i.test(raw) ? raw : '';
  var prefix = (base ? base.slice(1) : 'root') + ':';

  window.LekSite = {
    base: base,
    // an address on the site: LekSite.url('/hubs/fuse') -> '/lek/hubs/fuse'
    url: function (path) { return base + path; },
    // a browser storage key of this site: LekSite.key('fuse-name') -> 'lek:fuse-name'
    key: function (name) { return prefix + name; }
  };

  function links() {
    var all = document.querySelectorAll('a[data-site]');
    for (var i = 0; i < all.length; i++) {
      var a = all[i];
      var href = base + a.getAttribute('data-site');
      var back = a.getAttribute('data-return');
      if (back) href += (href.indexOf('?') < 0 ? '?' : '&') + 'returnurl=' + encodeURIComponent(base + back);
      a.setAttribute('href', href);
    }
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', links); else links();
})();
