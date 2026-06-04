import { ApplicationConfig, APP_INITIALIZER } from '@angular/core';
import { provideRouter, withRouterConfig } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { appRoutes } from './app.routes';
import { CurrencyService } from './core/services/currency.service';

// Factory to initialize global data before app boot
export function initializeApp(currencyService: CurrencyService) {
  return () => currencyService.initialize();
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(
      appRoutes,
      withRouterConfig({
        onSameUrlNavigation: 'reload',
      }),
    ),
    provideHttpClient(withInterceptors([authInterceptor])),

    // Wire up Currency Service on startup
    {
      provide: APP_INITIALIZER,
      useFactory: initializeApp,
      deps: [CurrencyService],
      multi: true,
    },
  ],
};