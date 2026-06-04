import { bootstrapApplication } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { provideAnimations } from '@angular/platform-browser/animations';
import { ApplicationConfig } from '@angular/core';
import { AppComponent } from './app/app';
import { appRoutes } from './app/app.routes';
import { authInterceptor } from './app/core/interceptors/auth.interceptor';

const appConfig: ApplicationConfig = {
  providers: [
    provideAnimations(),
    provideRouter(appRoutes),
    provideHttpClient(
      withInterceptors([authInterceptor]),
      // withXsrfConfiguration({
      //   cookieName: 'X-CSRF-TOKEN',
      //   headerName: 'X-CSRF-TOKEN'
      // })
    )
  ]
};

bootstrapApplication(AppComponent, appConfig).catch((err) => console.error(err));