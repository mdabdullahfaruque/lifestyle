import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthStore, GoogleButton } from 'auth';
import { BrandLogo } from 'ui';
import { ProblemDetails } from 'data-access';

@Component({
  selector: 'app-login',
  imports: [FormsModule, RouterLink, GoogleButton, BrandLogo],
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

  /**
   * Google hands us a signed ID token; the server does every check that matters. A failure here is
   * shown in the same place as a password failure, because to the seller it is the same event:
   * "I tried to sign in and could not."
   */
  protected async signInWithGoogle(credential: string): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);

    try {
      try {
        await this.auth.loginWithGoogle(credential);
        await this.router.navigateByUrl('/');
      } catch (err) {
        const problem = err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;
        if (problem?.code !== 'identity.surface_not_permitted') throw err;
        // Same closed loop as the password path: let an applicant through to the form.
        await this.auth.loginWithGoogle(credential, undefined, 'buyer');
        await this.router.navigateByUrl('/apply');
      }
    } catch (err) {
      this.handle(err);
    } finally {
      this.busy.set(false);
    }
  }

  protected async submit(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);

    try {
      await this.signIn(this.email.trim(), this.password, this.totpCode || undefined);
    } catch (err) {
      this.handle(err);
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * Signs in on the seller surface, falling back to buyer for an applicant.
   *
   * Someone who has not been approved yet holds no vendor, so PermissionResolver refuses the seller
   * surface — and the application form that would give them one lives behind this login. Without
   * the fallback that is a closed loop: you cannot apply because you are not a seller, and you
   * cannot become a seller without applying. The vendor-application endpoints accept any signed-in
   * token exactly so this is solvable here.
   */
  private async signIn(email: string, password: string, totpCode?: string): Promise<void> {
    try {
      await this.auth.login(email, password, totpCode);
      await this.router.navigateByUrl('/');
    } catch (err) {
      const problem = err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;
      if (problem?.code !== 'identity.surface_not_permitted') throw err;

      // Not a seller yet — sign in as themselves and take them to the application.
      await this.auth.login(email, password, totpCode, undefined, 'buyer');
      await this.router.navigateByUrl('/apply');
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
      case 'identity.google_not_configured':
        this.error.set('Google sign-in is not switched on for this deployment yet.');
        return;
      case 'identity.google_token_invalid':
        this.error.set('That Google sign-in could not be verified. Please try again.');
        return;
      case 'identity.google_email_unverified':
        this.error.set(
          "This Google account's email address is not verified, so it cannot be used to sign in.",
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
        // Reaching here means the buyer fallback in signIn() also failed, which is a suspended or
        // deleted account rather than an applicant.
        this.error.set('This account cannot sign in. Please contact support.');
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
