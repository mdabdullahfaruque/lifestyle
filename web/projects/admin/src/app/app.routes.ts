import { Routes } from '@angular/router';
import { authGuard } from 'auth';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./login/login').then((m) => m.Login),
    title: 'Sign in · Admin Console',
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./shell/shell').then((m) => m.Shell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'vendors' },
      {
        path: 'vendors',
        loadComponent: () => import('./vendors/vendors').then((m) => m.Vendors),
        title: 'Vendor applications · Admin Console',
      },
      {
        path: 'categories',
        loadComponent: () => import('./categories/categories').then((m) => m.Categories),
        title: 'Categories · Admin Console',
      },
      {
        path: 'audit',
        loadComponent: () => import('./audit/audit').then((m) => m.Audit),
        title: 'Audit log · Admin Console',
      },
      {
        path: 'moderation',
        loadComponent: () => import('./moderation/moderation').then((m) => m.Moderation),
        title: 'Catalogue moderation · Admin Console',
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
