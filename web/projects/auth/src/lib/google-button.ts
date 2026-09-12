import { Component, ElementRef, InjectionToken, OnInit, inject, input, output, signal, viewChild } from '@angular/core';

/**
 * The Google OAuth web client id. Provided per app; leave it null and the button renders nothing,
 * which is how a deployment without Google configured degrades — no dead button, no console noise.
 */
export const GOOGLE_CLIENT_ID = new InjectionToken<string | null>('lifestyle.googleClientId');

interface GoogleAccounts {
  accounts: {
    id: {
      initialize(config: { client_id: string; callback: (r: { credential: string }) => void }): void;
      renderButton(parent: HTMLElement, options: Record<string, unknown>): void;
    };
  };
}

const SCRIPT_SRC = 'https://accounts.google.com/gsi/client';

/**
 * Renders Google's own sign-in button and emits the ID token it produces.
 *
 * Google's script is loaded on demand rather than in index.html: most visits to a login page never
 * use it, and an unconditional third-party script on every page is both a performance cost and a
 * request to Google on behalf of someone who never asked to involve them.
 *
 * The component deliberately does no verification. The credential it emits is meaningless until the
 * server checks its signature, audience and issuer — treating a token as proof because it arrived
 * from a Google-branded button is exactly the mistake that makes "sign in with Google" unsafe.
 */
@Component({
  selector: 'lib-google-button',
  standalone: true,
  template: `
    @if (clientId) {
      <div class="google-button">
        <div #target></div>
        @if (failed()) {
          <p class="google-button__error">{{ unavailableText() }}</p>
        }
      </div>
    }
  `,
  styles: `
    .google-button { display: grid; justify-items: center; gap: 0.4rem; }
    .google-button__error { margin: 0; font-size: 0.8rem; color: #5b6472; text-align: center; }
  `,
})
export class GoogleButton implements OnInit {
  protected readonly clientId = inject(GOOGLE_CLIENT_ID, { optional: true }) ?? null;

  /** Emitted with Google's ID token. Worthless until the server validates it. */
  readonly credential = output<string>();

  readonly unavailableText = input('Google sign-in is unavailable right now.');
  readonly width = input(320);

  private readonly target = viewChild<ElementRef<HTMLElement>>('target');
  protected readonly failed = signal(false);

  async ngOnInit(): Promise<void> {
    if (!this.clientId) return;

    try {
      await loadScript();
      const google = (window as unknown as { google?: GoogleAccounts }).google;
      const host = this.target()?.nativeElement;
      if (!google || !host) {
        this.failed.set(true);
        return;
      }

      google.accounts.id.initialize({
        client_id: this.clientId,
        callback: (r) => this.credential.emit(r.credential),
      });

      google.accounts.id.renderButton(host, {
        type: 'standard',
        theme: 'outline',
        size: 'large',
        text: 'continue_with',
        shape: 'pill',
        logo_alignment: 'center',
        width: this.width(),
      });
    } catch {
      // Blocked script, offline, or a privacy extension. The password form is still there, so the
      // page stays usable — say so rather than leaving an empty gap where a button should be.
      this.failed.set(true);
    }
  }
}

let scriptPromise: Promise<void> | null = null;

/** Loads Google's script once per page, however many buttons ask for it. */
function loadScript(): Promise<void> {
  if (scriptPromise) return scriptPromise;

  scriptPromise = new Promise<void>((resolve, reject) => {
    if (typeof document === 'undefined') {
      reject(new Error('No document — server-side render.'));
      return;
    }

    const existing = document.querySelector<HTMLScriptElement>(`script[src="${SCRIPT_SRC}"]`);
    if (existing) {
      resolve();
      return;
    }

    const script = document.createElement('script');
    script.src = SCRIPT_SRC;
    script.async = true;
    script.defer = true;
    script.onload = () => resolve();
    script.onerror = () => reject(new Error('Google sign-in script failed to load.'));
    document.head.appendChild(script);
  });

  return scriptPromise;
}
