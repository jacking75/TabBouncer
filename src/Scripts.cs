#nullable enable

namespace TabBouncer;

internal static class Scripts
{
    internal const string IntentBindingName = "__tabBouncerIntent";
    internal const string CommandBindingName = "__tabBouncerCommand";
    internal const string IntentWorldName = "TabBouncerIntent";

    // 페이지 스크립트가 클릭 의도를 꾸며 넣지 못하도록 기본적으로 격리 월드에서 실행한다.
    // 격리 월드를 쓸 수 없을 때만 메인 월드에서 실행하며, 이때는 전역 바인딩 참조를 지운다.
    internal static string ClickTracker(bool hideBinding) =>
        ClickTrackerTemplate.Replace("__HIDE_BINDING__", hideBinding ? "true" : "false");

    private const string ClickTrackerTemplate = """
        (() => {
          const installed = '__tabBouncerIntentTrackerInstalledV2';
          if (document[installed]) return;
          Object.defineProperty(document, installed, { value: true });

          let binding = globalThis.__tabBouncerIntent;
          if (__HIDE_BINDING__) {
            try { delete globalThis.__tabBouncerIntent; } catch (_) {}
          }

          // DOM은 두 월드가 공유하므로 설치 여부 표시는 문서 속성으로 남긴다.
          const mark = () => {
            try { document.documentElement?.setAttribute('data-tabbouncer-tracker', '1'); } catch (_) {}
          };
          mark();
          document.addEventListener('DOMContentLoaded', mark, { once: true });

          const clean = value => String(value || '').replace(/\s+/g, ' ').trim().slice(0, 160);
          const send = value => {
            if (typeof binding !== 'function') binding = globalThis.__tabBouncerIntent;
            if (typeof binding !== 'function') return;
            try {
              binding(JSON.stringify({
                ...value,
                pageUrl: location.href,
                at: Date.now()
              }));
            } catch (_) {}
          };

          const pathElement = (event, selector) => {
            for (const item of event.composedPath()) {
              if (item instanceof Element && item.matches(selector)) return item;
            }
            return null;
          };

          const labelOf = element => clean(
            element.innerText ||
            element.getAttribute('aria-label') ||
            element.getAttribute('title') ||
            element.querySelector('img')?.getAttribute('alt') || ''
          );

          const visiblyDescribed = element => {
            const style = getComputedStyle(element);
            const rect = element.getBoundingClientRect();
            return style.display !== 'none' &&
              style.visibility !== 'hidden' &&
              Number(style.opacity || 1) > 0.05 &&
              rect.width >= 4 && rect.height >= 4 &&
              labelOf(element).length > 0;
          };

          const recordPointer = event => {
            if (!event.isTrusted) return;

            const link = pathElement(event, 'a[href],area[href]');
            if (link) {
              const explicit = visiblyDescribed(link);
              send({
                kind: explicit ? 'link' : 'passive-link',
                url: link.href || '',
                label: labelOf(link),
                opensNewContext: link.target === '_blank' || event.button === 1 ||
                  event.ctrlKey || event.metaKey || event.shiftKey
              });
              return;
            }

            const control = pathElement(event,
              'button,input,select,textarea,[role="button"],[role="link"],[contenteditable="true"]');
            if (control) {
              send({
                kind: 'control',
                url: '',
                label: labelOf(control) || clean(control.value),
                opensNewContext: false
              });
              return;
            }

            send({
              kind: 'passive',
              url: '',
              label: '',
              opensNewContext: false
            });
          };

          addEventListener('pointerdown', recordPointer, true);
          addEventListener('auxclick', recordPointer, true);
          addEventListener('click', event => {
            if (event.detail === 0) recordPointer(event);
          }, true);

          addEventListener('submit', event => {
            if (!event.isTrusted || !(event.target instanceof HTMLFormElement)) return;
            const form = event.target;
            const submitter = event.submitter;
            send({
              kind: 'form',
              url: submitter?.formAction || form.action || location.href,
              label: submitter ? labelOf(submitter) : '',
              opensNewContext: (submitter?.formTarget || form.target) === '_blank'
            });
          }, true);
        })();
        """;

    // {{LANG}}, {{STRINGS}}, {{STATE}}, {{TOKEN}}을 WriteGuidePage에서 채운다.
    internal const string GuidePageHtml = """
        <!doctype html>
        <html lang="{{LANG}}">
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>TabBouncer</title>
        <style>
          body { margin: 0; background: #f6f7f9; color: #181f2a;
                 font-family: "Segoe UI", "Malgun Gothic", sans-serif; }
          main { max-width: 680px; margin: 56px auto; padding: 32px 36px; background: #fff;
                 border-radius: 12px; box-shadow: 0 1px 3px rgba(0, 0, 0, .08); }
          h1 { margin: 0 0 12px; font-size: 24px; }
          h2 { margin: 28px 0 8px; font-size: 16px; }
          p, li { color: #475467; line-height: 1.8; }
          strong { color: #2563eb; }
          #state { margin: 0 0 14px; padding: 16px 18px; border-radius: 10px;
                   font-size: 18px; font-weight: 600; line-height: 1.6; }
          #state.off { background: #fef3c7; color: #92400e; }
          #state.on { background: #dcfce7; color: #166534; }
          #state.dry { background: #e0f2fe; color: #075985; }
          .controls { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; margin-bottom: 20px; }
          button { font: inherit; padding: 8px 16px; border-radius: 8px; cursor: pointer;
                   border: 1px solid #d0d5dd; background: #fff; color: #181f2a; }
          button.primary { background: #2563eb; border-color: #2563eb; color: #fff; }
          button:disabled { opacity: .5; cursor: default; }
          #count { color: #475467; }
          #favorites { padding-left: 20px; }
          #favorites a { color: #2563eb; }
          #no-favorites { color: #98a2b3; }
        </style>
        <main>
          <div id="state"></div>
          <div class="controls">
            <button id="toggle" class="primary" type="button"></button>
            <button id="dry" type="button"></button>
            <span id="count"></span>
          </div>
          <h1 id="title"></h1>
          <p id="intro"></p>
          <ul>
            <li id="tip-address"></li>
            <li id="tip-default"></li>
            <li id="tip-closed"></li>
          </ul>
          <h2 id="favorites-title"></h2>
          <ul id="favorites"></ul>
          <p id="no-favorites"></p>
        </main>
        <script>
          const T = {{STRINGS}};
          const TOKEN = "{{TOKEN}}";
          let state = {{STATE}};
          const byId = id => document.getElementById(id);

          const command = (name, value) => {
            const binding = window.__tabBouncerCommand;
            if (typeof binding !== 'function') return;
            binding(JSON.stringify({ token: state.token || TOKEN, command: name, value }));
          };

          byId('title').textContent = T.title;
          byId('intro').innerHTML = T.intro;
          byId('tip-address').innerHTML = T.tipAddress;
          byId('tip-default').innerHTML = T.tipDefault;
          byId('tip-closed').innerHTML = T.tipClosed;
          byId('favorites-title').textContent = T.favoritesTitle;
          byId('toggle').addEventListener('click', () => command('toggleMonitoring'));
          byId('dry').addEventListener('click', () => command('setDryRun', !state.dryRun));

          const render = () => {
            const box = byId('state');
            box.className = !state.monitoring ? 'off' : state.dryRun ? 'dry' : 'on';
            box.textContent = !state.monitoring ? T.stateOff : state.dryRun ? T.stateDry : T.stateOn;
            document.title = (state.monitoring ? '' : T.titlePrefixOff) + T.title;

            const connected = typeof window.__tabBouncerCommand === 'function';
            byId('toggle').textContent = state.monitoring ? T.pause : T.start;
            byId('toggle').disabled = !connected;
            byId('dry').textContent = state.dryRun ? T.dryOff : T.dryOn;
            byId('dry').disabled = !connected;
            byId('count').textContent = T.count.replace('{0}', state.closedCount);

            const list = byId('favorites');
            list.replaceChildren();
            for (const url of state.favorites || []) {
              const item = document.createElement('li');
              const link = document.createElement('a');
              link.href = url;
              link.target = '_blank';
              link.textContent = url;
              item.append(link);
              list.append(item);
            }
            byId('no-favorites').textContent = (state.favorites || []).length ? '' : T.noFavorites;
          };

          window.__tabBouncerSetState = next => { state = { ...state, ...next }; render(); };
          window.__tabBouncerSetMonitoring = on => window.__tabBouncerSetState({ monitoring: !!on });
          if (window.__tabBouncerPendingState) window.__tabBouncerSetState(window.__tabBouncerPendingState);
          render();
        </script>
        </html>
        """;
}
