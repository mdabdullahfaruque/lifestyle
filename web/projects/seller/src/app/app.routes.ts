import { Routes } from '@angular/router';
import { authGuard } from 'auth';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./login/login').then((m) => m.Login),
    title: 'Sign in · Seller Console',
  },
  {
    path: 'register',
    loadComponent: () => import('./login/register').then((m) => m.Register),
    title: 'Open your shop · Seller Console',
  },
  {
    path: 'forgot-password',
    loadComponent: () => import('./login/forgot-password').then((m) => m.ForgotPassword),
    title: 'Forgot your password · Seller Console',
  },
  {
    // Reached from the emailed link, which carries ?token=. Anonymous by necessity: the whole
    // point is that the owner cannot sign in.
    path: 'reset-password',
    loadComponent: () => import('./login/reset-password').then((m) => m.ResetPassword),
    title: 'Choose a new password · Seller Console',
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./shell/shell').then((m) => m.Shell),
    children: [
      {
        path: '',
        loadComponent: () => import('./dashboard/dashboard').then((m) => m.Dashboard),
        title: 'Overview · Seller Console',
      },
      {
        path: 'products',
        loadComponent: () => import('./products/products').then((m) => m.Products),
        title: 'Products · Seller Console',
      },
      {
        // Before :productId, or "import" would be read as a product id.
        path: 'products/import',
        loadComponent: () =>
          import('./products/import/product-import').then((m) => m.ProductImport),
        title: 'Bulk import · Seller Console',
      },
      {
        path: 'media',
        loadComponent: () => import('./media/library').then((m) => m.Library),
        title: 'Image library · Seller Console',
      },
      {
        path: 'products/new',
        loadComponent: () => import('./products/product-editor').then((m) => m.ProductEditor),
        title: 'New product · Seller Console',
      },
      {
        path: 'products/:productId',
        loadComponent: () => import('./products/product-edit').then((m) => m.ProductEdit),
        title: 'Edit product · Seller Console',
      },
      {
        path: 'settings',
        loadComponent: () => import('./settings/settings').then((m) => m.Settings),
        title: 'Shop settings · Seller Console',
      },
      {
        path: 'apply',
        loadComponent: () => import('./onboarding/onboarding').then((m) => m.Onboarding),
        title: 'Apply for a shop · Seller Console',
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
