import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';

export const appRoutes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/auth/auth').then((m) => m.AuthComponent),
  },
  {
    path: 'login/callback',
    loadComponent: () =>
      import('./features/auth-callback/auth-callback').then((m) => m.AuthCallbackComponent),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./layout/mobile-shell/mobile-shell').then((m) => m.MobileShellComponent),
    children: [
      {
        path: 'currency-setup',
        loadComponent: () =>
          import('./features/currency-setup/currency-setup').then((m) => m.CurrencySetupComponent),
      },
      {
        path: '',
        redirectTo: 'dashboard',
        pathMatch: 'full',
      },
      {
        path: 'dashboard',
        loadComponent: () =>
          import('./features/dashboard/dashboard').then((m) => m.DashboardComponent),
      },
      {
        path: 'send',
        loadComponent: () =>
          import('./features/send-money/send-money').then((m) => m.SendMoneyComponent),
      },
      {
        path: 'transactions',
        loadComponent: () =>
          import('./features/transactions/transactions').then((m) => m.TransactionsComponent),
      },
    ],
  },
  {
    path: '**',
    redirectTo: 'login',
  },
];