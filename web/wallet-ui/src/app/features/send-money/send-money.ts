import {
  Component,
  inject,
  signal,
  ChangeDetectionStrategy,
  OnInit,
  DestroyRef,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule, FormControl } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import {
  catchError,
  debounceTime,
  distinctUntilChanged,
  filter,
  switchMap,
  tap,
} from 'rxjs/operators';
import { EMPTY, Subject, of } from 'rxjs';
import {
  WalletService,
  RecipientLookupResponse,
  CurrencyRecord,
  SendMoneyRequest,
} from '../../core/services/wallet.service';
import { CurrencySymbolPipe } from '../../core/pipes/currency-symbol-pipe';
import { ShortenWalletIdPipe } from '../../core/pipes/shorten-wallet-id-pipe';

@Component({
  selector: 'app-send-money',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    CurrencySymbolPipe,
    ShortenWalletIdPipe,
  ],
  templateUrl: './send-money.html',
  styleUrl: './send-money.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SendMoneyComponent implements OnInit {
  private walletService = inject(WalletService);
  private destroyRef = inject(DestroyRef);

  searchControl = new FormControl('');

  // UI State Signals
  isSearching = signal(false);
  isFetchingQuote = signal(false);
  recipient = signal<RecipientLookupResponse | null>(null);
  loading = signal(false);
  successMessage = signal('');
  errorMessage = signal('');

  // Cross-Border Signals
  supportedCurrencies = signal<CurrencyRecord[]>([]);
  senderCurrency = signal<string>(sessionStorage.getItem('defaultCurrency') || 'INR');
  receiverCurrency = signal<string>(sessionStorage.getItem('defaultCurrency') || 'INR');
  senderAmount = signal<number | null>(null);
  receiverAmount = signal<number | null>(null);

  // Fee Details
  fxRate = signal<number | null>(null);
  transactionFee = signal<number | null>(null);

  private messageTimer?: ReturnType<typeof setTimeout>;

  // Reactive Subject for rate limits and race condition cancellation
  private quoteSubject = new Subject<{
    sourceCurrency: string;
    receivingAmount: number;
    destinationCurrency: string;
  } | null>();

  private currentTransactionIdempotencyKey: string | null = null;

  ngOnInit() {
    this.fetchSupportedCurrencies();

    // 1. Recipient Lookup Stream
    this.searchControl.valueChanges
      .pipe(
        debounceTime(500),
        distinctUntilChanged(),
        filter((value) => (value ?? '').length >= 3),
        tap(() => {
          this.isSearching.set(true);
          this.errorMessage.set('');
          this.recipient.set(null);
        }),
        switchMap((alias) =>
          this.walletService.lookupRecipient(alias!).pipe(
            catchError((err) => {
              this.isSearching.set(false);
              if (err.status !== 404) {
                this.errorMessage.set('User not found. Check the handle and try again.');
              }
              return EMPTY;
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result: RecipientLookupResponse) => {
        this.isSearching.set(false);
        this.recipient.set(result);
      });

    // 2. Fx Quote Reactive Stream
    this.quoteSubject
      .pipe(
        debounceTime(400),
        distinctUntilChanged((prev, curr) => JSON.stringify(prev) === JSON.stringify(curr)),
        tap((req) => {
          if (req && req.sourceCurrency !== req.destinationCurrency) {
            this.isFetchingQuote.set(true);
            this.errorMessage.set('');
          }
        }),
        switchMap((request) => {
          if (!request) {
            return of(null);
          }

          if (request.sourceCurrency === request.destinationCurrency) {
            const fee = request.receivingAmount * 0.01;
            return of({
              exchangeRate: 1,
              transactionFee: fee,
              finalAmount: request.receivingAmount,
            });
          }

          return this.walletService.getFxQuote(request as any).pipe(
            catchError((err) => {
              this.isFetchingQuote.set(false);
              const errorMsg =
                err.error?.Message || err.error?.message || 'Failed to fetch FX quote.';
              this.errorMessage.set(errorMsg);
              return of(null);
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((quote: any) => {
        this.isFetchingQuote.set(false);
        if (quote) {
          // Account for both camelCase and PascalCase depending on C# JSON settings
          this.fxRate.set(quote.exchangeRate ?? quote.rate);
          this.transactionFee.set(quote.transactionFee ?? quote.fee);
          this.senderAmount.set(quote.finalAmount);
        } else {
          this.fxRate.set(null);
          this.transactionFee.set(null);
          this.senderAmount.set(null);
        }
      });
  }

  fetchSupportedCurrencies() {
    setTimeout(() => {
      this.walletService.getCurrencies().subscribe({
        next: (currencies) => this.supportedCurrencies.set(currencies),
      });
    }, 500);
  }

  onCurrencyChange(currency: string) {
    this.receiverCurrency.set(currency);
    this.triggerQuoteFetch();
  }

  onReceiverAmountChange(amount: number | null) {
    this.receiverAmount.set(amount);
    this.triggerQuoteFetch();
  }

  triggerQuoteFetch() {
    const amount = this.receiverAmount();
    if (!amount || amount <= 0) {
      this.quoteSubject.next(null);
      return;
    }

    // Correctly mapped source and destination
    this.quoteSubject.next({
      sourceCurrency: this.senderCurrency(),
      receivingAmount: amount,
      destinationCurrency: this.receiverCurrency(),
    } as any);
  }

  sendMoney() {
    const targetWalletId = this.recipient()?.walletId;
    const rAmount = this.receiverAmount();

    if (!targetWalletId || !rAmount) {
      this.errorMessage.set('Please fill in all fields and ensure recipient is selected');
      return;
    }

    this.loading.set(true);
    this.errorMessage.set('');
    this.successMessage.set('');

    if (!this.currentTransactionIdempotencyKey) {
      this.currentTransactionIdempotencyKey = crypto.randomUUID();
    }

    const request: SendMoneyRequest = {
      receiverWalletId: targetWalletId,
      sourceAmount: this.senderAmount()!,
      destinationCurrency: this.receiverCurrency(),
      destinationAmount: rAmount,
      fxRate: this.fxRate()!,
      feeCurrency: this.senderCurrency(),
      transactionFee: this.transactionFee()!,
    };

    this.walletService
      .sendMoney(request as SendMoneyRequest, this.currentTransactionIdempotencyKey)
      .subscribe({
        next: (response) => {
          this.successMessage.set(`Transfer initiated..`);
          this.cleanup();
        },
        error: (error) => {
          this.errorMessage.set(error.error?.message || `Temporary system issue. Try again later.`);
          this.cleanup();
        },
      });
  }

  resetForm() {
    this.loading.set(false);
    this.senderAmount.set(null);
    this.receiverAmount.set(null);
    this.fxRate.set(null);
    this.transactionFee.set(null);
    this.searchControl.setValue('', { emitEvent: false });
    this.recipient.set(null);
    this.successMessage.set('');
    this.errorMessage.set('');
    this.currentTransactionIdempotencyKey = null;
  }

  getSelectedCurrencyName(): string {
    const code = this.receiverCurrency();
    const currency = this.supportedCurrencies().find((c) => c.code === code);
    return currency ? currency.name : '';
  }

  private cleanup(): void {
    if (this.messageTimer) {
      clearTimeout(this.messageTimer);
    }

    this.messageTimer = setTimeout(() => {
      this.resetForm();
    }, 5000);
  }
}