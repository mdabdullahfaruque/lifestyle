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
        path: 'moderation',
        loadComponent: () => import('./moderation/moderation').then((m) => m.Moderation),
        title: 'Catalogue moderation · Admin Console',
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
