import { Injectable, Inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from './api-base-url.token';

export interface CurrencyRecord { code: string; name: string; }

@Injectable({
  providedIn: 'root',
})

export class CurrencyService {
  private readonly storageKey = 'wallet_currencies';
  private readonly refreshIntervalMs = 1000; // 30 mins
  private refreshTimerId?: any;

  constructor(
    private readonly http: HttpClient,
    @Inject(API_BASE_URL) private readonly baseUrl: string,
  ) {}

  async initialize(): Promise<void> {
    const cache = sessionStorage.getItem(this.storageKey);
    if (cache) {
      this.startRefreshTimer();
      return Promise.resolve();
    }

    return this.refreshCurrencies();
  }

  getSymbol(code?: string | null): string {
    if (!code) return '';

    const normalizedCode = code.toUpperCase();
    const currencies = this.getCachedCurrencies();
    const record = currencies.find((x) => x.code.toUpperCase() === normalizedCode);

    // 2. Fallback dictionary
    const fallbackSymbols: Record<string, string> = {
      INR: '₹',
      USD: '$',
      EUR: '€',
      GBP: '£',
      AUD: 'A$',
      CAD: 'C$',
    };

    // 3. Return mapped symbol, or default to the text code if not found
    return fallbackSymbols[normalizedCode] || normalizedCode;
  }

  async getCurrencies(): Promise<CurrencyRecord[]> {
    let cached = this.getCachedCurrencies();
    
    // If the cache is empty (because the startup fetch failed before login), fetch it now!
    if (!cached || cached.length === 0) {
      await this.refreshCurrencies();
      cached = this.getCachedCurrencies();
    }
    
    return cached;
  }

  async refreshCurrencies(): Promise<void> {
    try {
      const currencies = await firstValueFrom(
        this.http.get<CurrencyRecord[]>(`${this.baseUrl}/wallet/supported-currencies`, {
          withCredentials: true,
        }),
      );
      console.info('Fetched supported currencies', currencies);
      if (currencies?.length) {
        sessionStorage.setItem(this.storageKey, JSON.stringify(currencies));
      }

      this.startRefreshTimer();
    } catch (err) {
      console.error('Failed loading currencies', err);
    }
  }

  private getCachedCurrencies(): CurrencyRecord[] {
    const raw = sessionStorage.getItem(this.storageKey);
    return raw ? JSON.parse(raw) : [];
  }

  private startRefreshTimer(): void {
    if (this.refreshTimerId) {
      clearInterval(this.refreshTimerId);
    }
    this.refreshTimerId = setInterval(() => this.refreshCurrencies(), this.refreshIntervalMs);
  }
}