import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./home/home').then((m) => m.Home),
    title: 'Lifestyle — shop Bangladesh',
  },
  {
    // A product is keyed by vendorId + slug in the public catalog, which is what leaves two shops
    // free to use the same slug.
    path: 'p/:vendorId/:slug',
    loadComponent: () => import('./product/product').then((m) => m.ProductPage),
  },
  { path: '**', redirectTo: '' },
];
