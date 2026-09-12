import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthStore } from 'auth';
import { BrandLogo } from 'ui';
import { ProblemDetails } from 'data-access';

@Component({
  selector: 'app-login',
  imports: [FormsModule, BrandLogo],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class Login {
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);

  protected email = '';
  protected password = '';
  protected totpCode = '';

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /**
   * Whether two-factor applies here is a deployment setting
   * (Identity:RequireTwoFactorOnAdmin), so the client cannot know up front. Show the field only
   * once the server asks for a code — that keeps the form to two fields where 2FA is off, and
   * costs one extra round trip where it is on.
   */
  protected readonly needsTotp = signal(false);

  protected async submit(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);

    try {
      await this.auth.login(this.email.trim(), this.password, this.totpCode || undefined);
      await this.router.navigateByUrl('/');
    } catch (err) {
      this.handle(err);
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * Branch on the API's stable `code`, never on the message text (docs/04 §3.5). The three TOTP
   * states are genuinely different situations and each needs its own wording.
   */
  private handle(err: unknown): void {
    const problem = err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;

    switch (problem?.code) {
      case 'identity.totp_required':
        this.needsTotp.set(true);
        this.error.set('Enter the 6-digit code from your authenticator app.');
        return;
      case 'identity.totp_invalid':
        this.needsTotp.set(true);
        this.error.set('That code was not accepted. Codes expire every 30 seconds — try the current one.');
        return;
      case 'identity.totp_enrolment_required':
        this.error.set(
          'This account must be enrolled in two-factor authentication before it can use the admin console.',
        );
        return;
      case 'identity.invalid_credentials':
        this.error.set('Email or password is incorrect.');
        return;
      case 'identity.account_locked':
        this.error.set('Too many failed attempts. Please try again in a few minutes.');
        return;
      case 'identity.account_suspended':
        this.error.set('This account has been suspended.');
        return;
      case 'identity.surface_not_permitted':
        this.error.set('This account does not have platform administrator access.');
        return;
      default:
        this.error.set(
          problem?.detail ?? 'Could not sign in just now. Please check your connection and try again.',
        );
    }
  }
}
