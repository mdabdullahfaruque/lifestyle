import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthStore } from 'auth';
import { ProblemDetails } from 'data-access';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
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

  /** Shown only once the server says this account has 2FA — asking everyone up front is noise. */
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
        this.error.set('This account must set up two-factor authentication before signing in.');
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
        // The commonest confusion on this screen: a real buyer account with no shop attached.
        this.error.set(
          'This account cannot sign in to the seller console. If you have not applied for a shop yet, sign in to the marketplace first.',
        );
        return;
      case 'identity.vendor_required':
        this.error.set('This account staffs more than one shop. Multi-shop sign-in is not built yet.');
        return;
      default:
        this.error.set(
          problem?.detail ?? 'Could not sign in just now. Please check your connection and try again.',
        );
    }
  }
}
