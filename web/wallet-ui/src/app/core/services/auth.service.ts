import { inject, Inject, Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { NotificationService } from './notification.service';
import { API_BASE_URL } from './api-base-url.token';

export interface RefreshResponse { userId: string; accessToken: string; }

@Injectable({
  providedIn: 'root',
})

export class AuthService {
  private authenticated = signal<boolean>(false);
  private notificationService = inject(NotificationService);
  readonly isAuthenticated$: Observable<boolean> | undefined;

  // Inject HttpClient to handle dynamic backend requests safely
  constructor(
    @Inject(API_BASE_URL) private baseUrl: string,
    private http: HttpClient,
  ) {}

  isAuthenticated(): boolean {
    const token = localStorage.getItem('access_token');
    return !!token;
  }

  login(): void {
    // Keep the direct relative redirect for OAuth initialization
    window.location.href = `${this.baseUrl}/auth/google/login`;
  }

  refreshToken(): Observable<RefreshResponse> {
    return this.http.post<RefreshResponse>(`${this.baseUrl}/auth/refresh`,
      {}, 
      {
        // CRITICAL: Force the browser to pass the secure HttpOnly 'refreshToken' cookie to Nginx
        withCredentials: true,
      })
      .pipe(
        tap((response) => {
          localStorage.setItem('access_token', response.accessToken);
          this.authenticated.set(true);

          this.notificationService
            .connect(response.accessToken)
              .catch((err) => console.error('Failed to establish real-time notification pipeline', err));
        }),
      );
  }

  logout(): void
  {
    this.notificationService.disconnect();
    this.http.post(
      '/api/auth/logout',
      {},
      {
        withCredentials: true
      }
    )
    .subscribe({
        next: () => {
          localStorage.clear();
          this.authenticated.set(false);
          window.location.href = '/login';
        },

        error: () => {
          localStorage.clear();
          this.authenticated.set(false);
          window.location.href = '/login';
        }
    });
  }

  setSession(accessToken: string) {
    localStorage.setItem('access_token', accessToken);
    this.authenticated.set(true);

    this.notificationService
      .connect(accessToken)
      .catch((err) => console.error('Failed to establish real-time notification pipeline', err));
  }
}
