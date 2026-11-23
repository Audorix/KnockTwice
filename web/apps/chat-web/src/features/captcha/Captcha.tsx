import { useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';

/**
 * Provider-based human verification. The provider and public site key come from
 * GET /api/v1/auth/config; secrets never reach the browser.
 *  - turnstile: Cloudflare Turnstile widget (script loaded on demand from challenges.cloudflare.com)
 *  - none:      no widget; the server accepts any token
 * Tokens are single-use: call the `reset` handle after each submission attempt.
 */

export interface CaptchaProps {
  provider: 'turnstile' | 'none';
  siteKey: string | null;
  /** Server-side action the token is bound to (register, forgot_password, resend_confirmation). */
  action: string;
  onToken: (token: string | null) => void;
  /** Incremented by the parent to request a fresh challenge. */
  resetKey: number;
}

interface TurnstileApi {
  render: (container: HTMLElement, options: Record<string, unknown>) => string;
  reset: (widgetId: string) => void;
  remove: (widgetId: string) => void;
}

declare global {
  interface Window {
    turnstile?: TurnstileApi;
  }
}

const turnstileScript = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';
let turnstileLoading: Promise<TurnstileApi> | null = null;

function loadTurnstile(): Promise<TurnstileApi> {
  if (window.turnstile) return Promise.resolve(window.turnstile);
  turnstileLoading ??= new Promise<TurnstileApi>((resolve, reject) => {
    const script = document.createElement('script');
    script.src = turnstileScript;
    script.async = true;
    script.defer = true;
    script.onload = () => (window.turnstile ? resolve(window.turnstile) : reject(new Error('Turnstile unavailable')));
    script.onerror = () => {
      turnstileLoading = null;
      reject(new Error('Turnstile script failed to load'));
    };
    document.head.appendChild(script);
  });
  return turnstileLoading;
}

export function Captcha({ provider, siteKey, action, onToken, resetKey }: CaptchaProps) {
  const { t, i18n } = useTranslation();
  const container = useRef<HTMLDivElement>(null);
  const widget = useRef<string | null>(null);
  const tokenCallback = useRef(onToken);
  tokenCallback.current = onToken;

  useEffect(() => {
    if (provider === 'none') {
      tokenCallback.current('none');
      return;
    }
    if (!siteKey || !container.current) return;

    let cancelled = false;
    const element = container.current;
    loadTurnstile()
      .then((turnstile) => {
        if (cancelled) return;
        widget.current = turnstile.render(element, {
          sitekey: siteKey,
          action,
          language: i18n.resolvedLanguage ?? 'auto',
          callback: (token: string) => tokenCallback.current(token),
          'expired-callback': () => tokenCallback.current(null),
          'error-callback': () => tokenCallback.current(null),
        });
      })
      .catch(() => tokenCallback.current(null));

    return () => {
      cancelled = true;
      if (widget.current && window.turnstile) window.turnstile.remove(widget.current);
      widget.current = null;
    };
  }, [provider, siteKey, action, i18n.resolvedLanguage]);

  useEffect(() => {
    if (resetKey === 0) return;
    if (provider === 'none') {
      tokenCallback.current('none');
    } else if (widget.current && window.turnstile) {
      tokenCallback.current(null);
      window.turnstile.reset(widget.current);
    }
  }, [resetKey, provider]);

  if (provider === 'none') return null;
  return <div ref={container} className="cx-captcha" role="group" aria-label={t('auth.captchaLabel')} />;
}
