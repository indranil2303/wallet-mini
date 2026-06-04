import { Inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, shareReplay, catchError, EMPTY, tap, BehaviorSubject } from 'rxjs';
import { API_BASE_URL } from './api-base-url.token';

export interface CurrencyRecord {
  code: string;
  name: string;
}

export interface SendMoneyResponse {
  id: string;
}

export interface SendMoneyRequest {
  receiverWalletId: string;
  sourceAmount: number;
  destinationCurrency: string;
  destinationAmount: number;
  fxRate: number;
  feeCurrency: string;
  transactionFee: number;
}

export interface Transaction {
  id: string;
  type: 'CR' | 'DR'; // Credit or Debit
  destinationCurrency: string;
  destinationAmount: number;
  exchangeRate: number;
  senderAliasName: string;
  senderWalletId: string;
  receiverAliasName: string;
  receiverWalletId: string;
  status: string;
  createdAtUtc: string;
}

export interface WalletData {
  currencyCode: string;
  balance: number;
  status: string;
  isDefaultCurrencySet: boolean;
}

export interface WalletStatusRequest {
  currencyCode: string;
}

export interface FXQuoteRequest {
  sourceCurrency: string;
  receivingAmount: number;
  destinationCurrency: string;
}

export interface FxQuoteResponse {
  finalAmount: number;
  exchangeRate: number;
  transactionFee: number;
}

export interface RecipientLookupResponse {
  walletId: string;
  maskedName: string;
  alias: string;
}

@Injectable({
  providedIn: 'root',
})
export class WalletService {
  private walletSubject = new BehaviorSubject<WalletData | null>(null);
  public walletState$ = this.walletSubject.asObservable();
  public isDefaultCurrencySet$ = false; // Placeholder, should be derived from walletState$

  constructor(
    @Inject(API_BASE_URL) private baseUrl: string,
    private http: HttpClient,
  ) {}

  refreshWalletSummary(): void {
    this.http
      .get<WalletData>(`${this.baseUrl}/wallet/summary`, { withCredentials: true })
      .pipe(
        tap((data) => this.walletSubject.next(data)),
        catchError((err) => {
          console.error('Wallet fetch failed', err);
          return EMPTY;
        }),
      )
      .subscribe();
  }

  getCurrencies(): Observable<CurrencyRecord[]> {
    return this.http.get<CurrencyRecord[]>(`${this.baseUrl}/wallet/supported-currencies`, {
      withCredentials: true,
    });
  }

  getCurrencySymbol(currencyCode: string): string {
    const symbols: Record<string, string> = {
      INR: '₹',
      USD: '$',
      EUR: '€',
      GBP: '£',
      AUD: 'A$',
      CAD: 'C$',
    };
    return symbols[currencyCode] || currencyCode; // Fallback to code if symbol isn't mapped
  }

  getFxQuote(payload: FXQuoteRequest): Observable<FxQuoteResponse> {
    return this.http.post<FxQuoteResponse>(`${this.baseUrl}/wallet/fx/quote`, payload, {
      withCredentials: true,
    });
  }

  updateDefaultCurrency(payload: WalletStatusRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/wallet/update-status`, payload, {
      withCredentials: true,
    });
  }

  lookupRecipient(alias: string): Observable<RecipientLookupResponse> {
    // Strips the @ if the user types it to match backend sanitization
    const cleanAlias = alias.trim().replace(/^@/, '');
    return this.http.get<RecipientLookupResponse>(`${this.baseUrl}/wallet/lookup/${cleanAlias}`, {
      withCredentials: true, // CRITICAL: Ensures session cookies pass through Nginx
    });
  }

  sendMoney(request: SendMoneyRequest): Observable<SendMoneyResponse> {
    return this.http.post<SendMoneyResponse>(`${this.baseUrl}/wallet/send`, request, {
      withCredentials: true, // CRITICAL: Added missing credentials flag
    });
  }

  getTransactions(
    startDate?: Date | null,
    endDate?: Date | null,
    pageIndex?: number | undefined,
    pageSize?: number | undefined,
  ): Observable<Transaction[]> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', startDate.toISOString());
    if (endDate) params = params.set('endDate', endDate.toISOString());
    if (pageIndex !== undefined && pageIndex !== null)
      params = params.set('pageIndex', pageIndex.toString());
    if (pageSize !== undefined && pageSize !== null)
      params = params.set('pageSize', pageSize.toString());

    return this.http.get<Transaction[]>(`${this.baseUrl}/wallet/transactions`, {
      params,
      withCredentials: true,
    });
  }
}