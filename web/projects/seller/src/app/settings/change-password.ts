import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthStore } from 'auth';
import { AccountService, ProblemDetails } from 'data-access';
import { firstValueFrom } from 'rxjs';

/**
 * Change the account password.
 *
 * This exists for a specific reason: a shop an administrator opened starts with a password that
 * administrator generated and read out. Until the owner can replace it, someone else knows how to
 * sign in as them — so this is part of the onboarding path, not a settings nicety.
 *
 * Its own component rather than more of `Settings`: that screen is about the shop, this is about
 * the person, and the two have different failure modes.
 */
@Component({
  selector: 'app-change-password',
  imports: [FormsModule],
  template: `
    <section class="card">
      <h2>{{ hasPassword() ? 'Password' : 'Set a password' }}</h2>

      @if (hasPassword()) {
        <p class="muted tiny">
          If this shop was opened for you by the marketplace team, change the password they gave you —
          it was shared out loud or over a message, so it should not stay in use.
        </p>
      } @else {
        <p class="muted tiny">
          This account signs in with Google only. Adding a password gives you a second way in — worth
          doing, because it is what you will need if you ever lose access to that Google account.
        </p>
      }

      @if (error(); as message) {
        <p class="alert" role="alert">{{ message }}</p>
      }

      <form class="grid" novalidate (ngSubmit)="submit()">
        @if (hasPassword()) {
          <label class="field">
            <span class="field__label">Current password</span>
            <input type="password" name="currentPassword" autocomplete="current-password"
                   [(ngModel)]="current" [disabled]="busy()" />
            @if (fieldErrors()['currentPassword']; as message) {
              <span class="err" role="alert">{{ message }}</span>
            }
          </label>
        }

        <label class="field">
          <span class="field__label">{{ hasPassword() ? 'New password' : 'Password' }}</span>
          <input type="password" name="newPassword" autocomplete="new-password"
                 [(ngModel)]="next" [disabled]="busy()" />
          @if (fieldErrors()['newPassword']; as message) {
            <span class="err" role="alert">{{ message }}</span>
          } @else {
            <span class="hint muted">At least 10 characters. Length matters more than symbols.</span>
          }
        </label>

        <label class="field">
          <span class="field__label">Repeat it</span>
          <input type="password" name="confirmPassword" autocomplete="new-password"
                 [(ngModel)]="confirm" [disabled]="busy()" />
          @if (fieldErrors()['confirmPassword']; as message) {
            <span class="err" role="alert">{{ message }}</span>
          }
        </label>

        <div class="actions">
          <button class="btn btn--primary" type="submit" [disabled]="busy()">
            {{ busy() ? 'Saving…' : hasPassword() ? 'Change password' : 'Set password' }}
          </button>
          <span class="muted tiny">You will be signed out and asked to sign in again.</span>
        </div>
      </form>
    </section>
  `,
  styles: `
    :host { display: block; }
    h2 { font-size: 1.05rem; margin-bottom: 0.3rem; }
    .tiny { font-size: 0.82rem; }
    .grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0.75rem 1rem; margin-top: 0.9rem; }
    .grid .field { min-width: 0; margin: 0; }
    .actions { grid-column: 1 / -1; display: flex; align-items: center; gap: 0.75rem; flex-wrap: wrap; }
    .hint { display: block; font-size: 0.78rem; margin-top: 0.25rem; }
    .err { display: block; font-size: 0.8rem; color: var(--danger); margin-top: 0.25rem; }
    @media (max-width: 700px) { .grid { grid-template-columns: minmax(0, 1fr); } }
  `,
})
export class ChangePassword {
  private readonly account = inject(AccountService);
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);

  protected current = '';
  protected next = '';
  protected confirm = '';

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldErrors = signal<Record<string, string>>({});

  /**
   * A Google-only account has no password to verify, so this screen asks for one instead of two and
   * calls a different endpoint. Reading it from the session rather than discovering it from a failed
   * submit is the difference between the right form and a field the owner can never fill.
   */
  protected readonly hasPassword = computed(() => this.auth.user()?.hasPassword ?? true);

  protected async submit(): Promise<void> {
    if (this.busy()) return;

    this.error.set(null);
    const problems: Record<string, string> = {};
    const setting = !this.hasPassword();

    if (!setting && !this.current) problems['currentPassword'] = 'Enter the password you sign in with now.';
    if (!this.next) problems['newPassword'] = 'Choose a password.';
    else if (this.next.length < 10) problems['newPassword'] = 'Use at least 10 characters.';
    else if (!setting && this.next === this.current) problems['newPassword'] = 'The new password must be different.';
    if (this.next && this.confirm !== this.next) problems['confirmPassword'] = 'These two do not match.';

    this.fieldErrors.set(problems);
    if (Object.keys(problems).length) return;

    this.busy.set(true);
    try {
      await firstValueFrom(
        setting ? this.account.setPassword(this.next) : this.account.changePassword(this.current, this.next),
      );

      // The API revoked every session, this one included. Clearing locally and routing to the sign
      // -in page is the honest thing to do — leaving the console up would show a shell whose next
      // request fails with a 401.
      await this.auth.logout();
      await this.router.navigate(['/login'], { queryParams: { changed: '1' } });
    } catch (err) {
      this.handle(err);
    } finally {
      this.busy.set(false);
    }
  }

  private handle(err: unknown): void {
    const problem = err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;

    switch (problem?.code) {
      case 'identity.current_password_invalid':
        this.fieldErrors.set({ currentPassword: 'That is not your current password.' });
        return;
      case 'identity.password_not_set':
        // Reachable only if the session's hasPassword disagreed with the server — a stale profile.
        this.error.set('This account has no password yet. Reload the page and set one instead.');
        return;
      case 'identity.password_already_set':
        this.error.set('This account already has a password. Reload the page to change it instead.');
        return;
      default:
        if (problem?.errors) {
          const byField: Record<string, string> = {};
          for (const [key, messages] of Object.entries(problem.errors)) {
            byField[key.charAt(0).toLowerCase() + key.slice(1)] = messages.join(' ');
          }
          this.fieldErrors.set(byField);
          return;
        }
        this.error.set(problem?.detail ?? 'The password could not be changed. Please try again.');
    }
  }
}
