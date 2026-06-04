import {
  HttpInterceptorFn,
  HttpErrorResponse,
  HttpClient,
  HttpResponse,
  HttpRequest,
  HttpBackend,
} from '@angular/common/http';

import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, throwError, BehaviorSubject, of } from 'rxjs';
import { catchError, filter, take, switchMap, tap, timeout, finalize } from 'rxjs/operators';
import { API_BASE_URL } from '../services/api-base-url.token';
import { RefreshResponse } from '../services/auth.service';

const MAX_CACHE_ENTRIES = 100;
const CACHE_TTL_MS = 300_000;
const responseCache = new Map<string, HttpResponse<unknown>>();
const cacheTimers = new Map<string, ReturnType<typeof setTimeout>>();

const refreshTokenSubject = new BehaviorSubject<string | null>(null);
let isRefreshing = false;

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const router = inject(Router);
  const baseUrl = inject(API_BASE_URL);

  // Use raw HttpClient without interceptors for the refresh request
  const httpBackend = inject(HttpBackend);
  const rawHttp = new HttpClient(httpBackend);

  if (isExcludedRoute(request.url)) {
    return next(request);
  }

  const token = getAccessToken();

  // 1. Core Request Pipeline (Handles Caching & Reactive Fallbacks)
  const processRequest = (validToken: string | null) => {
    const req = validToken ? attachBearerToken(request, validToken) : request;

    if (shouldUseCache(req)) {
      const cached = responseCache.get(req.urlWithParams);
      if (cached) return of(cached);
    }

    return next(req).pipe(
      tap((event) => {
        if (event instanceof HttpResponse && shouldUseCache(req)) {
          manageCache(req.urlWithParams, event);
        }
      }),
      catchError((error: HttpErrorResponse) => {
        // Reactive Fallback: Just in case the server rejects a mathematically valid token
        if (error.status === 401 && !req.url.includes('/auth/refresh')) {
          localStorage.removeItem('access_token');
          return triggerRefresh(router, rawHttp, baseUrl).pipe(
            switchMap((newToken) => {
              const retryReq = attachBearerToken(request, newToken);
              return next(retryReq);
            }),
          );
        }
        return throwError(() => error);
      }),
    );
  };

  // 2. PROACTIVE REFRESH: Stop 401s before they happen
  if (token && isTokenAboutToExpire(token)) {
    return triggerRefresh(router, rawHttp, baseUrl).pipe(
      switchMap((newToken) => processRequest(newToken)),
    );
  }

  // 3. Normal Execution
  return processRequest(token);
};

// --- Helper Functions ---

function triggerRefresh(router: Router, http: HttpClient, baseUrl: string): Observable<string> {
  // If a refresh is already in progress, wait for it to finish and use that token
  if (isRefreshing) {
    return refreshTokenSubject.pipe(
      filter((t): t is string => t !== null),
      take(1),
    );
  }

  isRefreshing = true;
  refreshTokenSubject.next(null);

  return http.post<RefreshResponse>(`${baseUrl}/auth/refresh`, {}, { withCredentials: true }).pipe(
    timeout(10000),
    switchMap((response) => {
      const token = response.accessToken;
      if (!token) throw new Error('Token missing');

      storeAccessToken(token);
      refreshTokenSubject.next(token);
      return of(token);
    }),
    catchError((error) => {
      clearAccessToken();
      refreshTokenSubject.next(null);
      void router.navigate(['/login']);
      return throwError(() => error);
    }),
    finalize(() => {
      isRefreshing = false;
    }),
  );
}

function isTokenAboutToExpire(token: string): boolean {
  try {
    // Decode the JWT payload safely
    const payloadBase64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const payload = JSON.parse(atob(payloadBase64));

    // Check if the token expires in less than 60 seconds
    const timeRemainingSeconds = payload.exp - Math.floor(Date.now() / 1000);
    return timeRemainingSeconds < 60;
  } catch {
    return true; // If parsing fails, assume it's invalid/expired
  }
}

function attachBearerToken(request: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return request.clone({
    setHeaders: {
      Authorization: `Bearer ${token}`,
    },
  });
}

// Switched back to localStorage to prevent multi-tab disconnects
function getAccessToken(): string | null {
  return localStorage.getItem('access_token');
}

function storeAccessToken(token: string): void {
  localStorage.setItem('access_token', token);
}

function clearAccessToken(): void {
  localStorage.removeItem('access_token');
}

function isExcludedRoute(url: string): boolean {
  return ['/auth/refresh', '/auth/google/login', '/health'].some((x) => url.includes(x));
}

function shouldUseCache(request: HttpRequest<unknown>): boolean {
  return request.method === 'GET' && !request.url.includes('/api/wallet');
}

function manageCache(url: string, response: HttpResponse<unknown>) {
  if (responseCache.size >= MAX_CACHE_ENTRIES) {
    const oldestKey = responseCache.keys().next().value;
    if (oldestKey) {
      responseCache.delete(oldestKey);
      clearTimeout(cacheTimers.get(oldestKey));
      cacheTimers.delete(oldestKey);
    }
  }

  responseCache.set(url, response);
  if (cacheTimers.has(url)) clearTimeout(cacheTimers.get(url));

  const timer = setTimeout(() => {
    responseCache.delete(url);
    cacheTimers.delete(url);
  }, CACHE_TTL_MS);

  cacheTimers.set(url, timer);
}