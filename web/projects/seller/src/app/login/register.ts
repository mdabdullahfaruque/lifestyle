import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthStore, GoogleButton } from 'auth';
import { ProblemDetails } from 'data-access';
import { BrandLogo } from 'ui';

/**
 * Create an account, then go straight to the shop application.
 *
 * Registering does not make anyone a seller — it creates an ordinary account with the Buyer role.
 * The account becomes a seller when an admin approves its vendor application, which is why this
 * signs in on the **buyer** surface: the seller surface would refuse a user with no vendor, and
 * the application form is what gives them one.
 */
@Component({
  selector: 'app-register',
  imports: [FormsModule, RouterLink, GoogleButton, BrandLogo],
  templateUrl: './register.html',
  styleUrl: './login.scss',
})
export class Register {
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);

  protected fullName = '';
  protected email = '';
  protected password = '';
  protected phoneNumber = '';

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected async submit(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);

    try {
      await this.auth.register(
        this.email.trim(),
        this.password,
        this.fullName.trim(),
        this.phoneNumber.trim() || null,
      );

      await this.auth.login(this.email.trim(), this.password, undefined, undefined, 'buyer');
      await this.router.navigateByUrl('/apply');
    } catch (err) {
      this.handle(err);
    } finally {
      this.busy.set(false);
    }
  }

  /** Google is the same journey with one fewer form: new account, then straight to the application. */
  protected async signUpWithGoogle(credential: string): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);

    try {
      await this.auth.loginWithGoogle(credential, undefined, 'buyer');
      await this.router.navigateByUrl('/apply');
    } catch (err) {
      this.handle(err);
    } finally {
      this.busy.set(false);
    }
  }

  private handle(err: unknown): void {
    const problem = err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;

    switch (problem?.code) {
      case 'identity.email_taken':
        // Not an error worth a dead end: they already have an account, so send them to sign in.
        this.error.set('That email already has an account. Sign in instead — you can apply for a shop from there.');
        return;
      case 'identity.phone_taken':
        this.error.set('That phone number is already on another account. Use a different one, or leave it blank.');
        return;
      case 'identity.password_too_short':
        this.error.set('Use a longer password — at least 10 characters.');
        return;
      case 'identity.google_email_unverified':
        this.error.set("This Google account's email address is not verified, so it cannot be used.");
        return;
      default:
        if (problem?.errors) {
          this.error.set(Object.values(problem.errors).flat().join(' '));
          return;
        }
        this.error.set(problem?.detail ?? 'Could not create the account. Please try again.');
    }
  }
}
