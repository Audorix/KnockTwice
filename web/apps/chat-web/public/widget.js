/*!
 * Audorix.ChatX website widget loader (MIT). Embeds the chat in an iframe served by your
 * Audorix.ChatX installation; this script only creates, positions and resizes that frame.
 *
 *   <script src="https://chat.example.org/widget.js" data-widget-id="WIDGET-ID" async></script>
 *
 * Optional attributes: data-language="en|es", data-open="true".
 * JavaScript API (after load, or queue calls with window.ChatXWidget = window.ChatXWidget || [];):
 *   ChatXWidget.init({ widgetId, identityToken, language, open })
 *   ChatXWidget.identify(identityToken)   // a JWT your server signed (HS256, widget secret)
 *   ChatXWidget.logout()                  // ends the visitor session (call when your user signs out)
 *   ChatXWidget.open() / close() / toggle() / destroy()
 *   ChatXWidget.on('ready' | 'open' | 'close' | 'unread', callback)
 */
(function () {
  'use strict';

  var script = document.currentScript;
  var base = script && script.src ? new URL(script.src).origin : null;
  if (!base) return;

  var GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  var LAUNCHER = 72;
  var frame = null;
  var ready = false;
  var pending = [];
  var listeners = {};
  var state = { open: false, position: 'right', unread: 0 };
  var options = null;
  var media = window.matchMedia ? window.matchMedia('(max-width: 480px)') : null;

  function emit(name, value) {
    (listeners[name] || []).forEach(function (callback) {
      try { callback(value); } catch (error) { setTimeout(function () { throw error; }); }
    });
  }

  function post(message) {
    if (!frame) return;
    if (!ready) { pending.push(message); return; }
    message.source = 'chatx-host';
    frame.contentWindow.postMessage(message, base);
  }

  function layout() {
    if (!frame) return;
    var s = frame.style;
    var side = state.position === 'left' ? 'left' : 'right';
    var other = side === 'left' ? 'right' : 'left';
    var small = media && media.matches;
    s[other] = 'auto';
    if (state.open && small) {
      s.top = '0'; s.bottom = '0'; s[side] = '0';
      s.width = '100vw'; s.height = '100%'; s.maxHeight = 'none';
      s.borderRadius = '0';
    } else if (state.open) {
      s.top = 'auto'; s.bottom = '16px'; s[side] = '16px';
      s.width = 'min(400px, calc(100vw - 32px))'; s.height = 'min(640px, calc(100vh - 32px))';
      s.borderRadius = '16px';
    } else {
      s.top = 'auto'; s.bottom = '16px'; s[side] = '16px';
      s.width = LAUNCHER + 'px'; s.height = LAUNCHER + 'px';
      s.borderRadius = '50%';
    }
  }

  function onMessage(event) {
    // Only the frame we created, served from the installation's origin, may talk to us.
    if (!frame || event.source !== frame.contentWindow || event.origin !== base) return;
    var data = event.data;
    if (!data || data.source !== 'chatx' || typeof data.type !== 'string') return;
    switch (data.type) {
      case 'ready':
        ready = true;
        frame.contentWindow.postMessage({
          source: 'chatx-host', type: 'init',
          identityToken: options.identityToken || null,
          open: !!options.open,
        }, base);
        pending.splice(0).forEach(post);
        emit('ready');
        break;
      case 'state':
        var wasOpen = state.open;
        state.open = !!data.open;
        state.position = data.position === 'left' ? 'left' : 'right';
        frame.style.display = data.hidden ? 'none' : 'block';
        layout();
        if (state.open !== wasOpen) emit(state.open ? 'open' : 'close');
        break;
      case 'unread':
        state.unread = Math.max(0, Number(data.count) || 0);
        emit('unread', state.unread);
        break;
    }
  }

  function init(config) {
    if (frame) return api;
    config = config || {};
    if (!GUID.test(String(config.widgetId || ''))) {
      if (window.console) console.warn('ChatXWidget: a valid widgetId is required.');
      return api;
    }
    options = config;
    var params = new URLSearchParams({ widget: config.widgetId, parent: window.location.origin });
    if (config.language) params.set('lang', String(config.language));
    frame = document.createElement('iframe');
    frame.src = base + '/embed.html?' + params.toString();
    frame.title = 'Chat';
    frame.setAttribute('sandbox', 'allow-scripts allow-same-origin allow-forms allow-popups allow-popups-to-escape-sandbox allow-downloads');
    frame.setAttribute('referrerpolicy', 'no-referrer');
    var s = frame.style;
    s.position = 'fixed'; s.zIndex = '2147483000'; s.border = '0'; s.background = 'transparent';
    s.colorScheme = 'normal'; s.overflow = 'hidden'; s.display = 'none';
    s.boxShadow = 'none'; s.maxWidth = '100vw';
    layout();
    window.addEventListener('message', onMessage);
    if (media && media.addEventListener) media.addEventListener('change', layout);
    (document.body || document.documentElement).appendChild(frame);
    return api;
  }

  var api = {
    version: 1,
    init: init,
    identify: function (identityToken) { post({ type: 'identify', identityToken: String(identityToken || '') }); return api; },
    logout: function () { post({ type: 'logout' }); return api; },
    open: function () { post({ type: 'open' }); return api; },
    close: function () { post({ type: 'close' }); return api; },
    toggle: function () { post({ type: state.open ? 'close' : 'open' }); return api; },
    on: function (name, callback) {
      if (typeof callback === 'function') (listeners[name] = listeners[name] || []).push(callback);
      return api;
    },
    destroy: function () {
      window.removeEventListener('message', onMessage);
      if (media && media.removeEventListener) media.removeEventListener('change', layout);
      if (frame && frame.parentNode) frame.parentNode.removeChild(frame);
      frame = null; ready = false; pending = []; state.open = false;
      return api;
    },
  };

  // Calls queued before the script loaded: window.ChatXWidget = [['init', {...}], ...]
  var queued = Array.isArray(window.ChatXWidget) ? window.ChatXWidget : [];
  window.ChatXWidget = api;
  queued.forEach(function (call) {
    if (call && typeof api[call[0]] === 'function') api[call[0]].apply(api, Array.prototype.slice.call(call, 1));
  });

  if (script && script.dataset && script.dataset.widgetId && !frame) {
    init({ widgetId: script.dataset.widgetId, language: script.dataset.language, open: script.dataset.open === 'true' });
  }
})();
