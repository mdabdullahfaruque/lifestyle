import { Routes } from '@angular/router';
import { authGuard } from 'auth';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./login/login').then((m) => m.Login),
    title: 'Sign in · Seller Console',
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
