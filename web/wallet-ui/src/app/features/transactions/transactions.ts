import { Component, inject, signal, ChangeDetectionStrategy, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatTableDataSource, MatTableModule } from '@angular/material/table';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { WalletService, Transaction } from '../../core/services/wallet.service';
import { CurrencySymbolPipe } from '../../core/pipes/currency-symbol-pipe';
import { ShortenWalletIdPipe } from "../../core/pipes/shorten-wallet-id-pipe";

@Component({
  selector: 'app-transactions',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatTableModule,
    MatFormFieldModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    MatDatepickerModule,
    MatNativeDateModule,
    MatPaginatorModule,
    CurrencySymbolPipe,
    ShortenWalletIdPipe
],
  templateUrl: './transactions.html',
  styleUrls: ['./transactions.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TransactionsComponent {
  private walletService = inject(WalletService);
  private destroyRef = inject(DestroyRef); // Inject for automatic memory cleanup

  readonly dataSource = new MatTableDataSource<Transaction>([]);
  readonly defaultCurrency = signal<string>(sessionStorage.getItem('defaultCurrency') || '');
  readonly startDate = signal<Date | null>(null);
  readonly endDate = signal<Date | null>(null);
  readonly loading = signal(false);
  readonly errorMessage = signal('');
  readonly hasSearched = signal(false);

  readonly pageIndex = signal(0);
  readonly pageSize = signal(10);
  readonly totalPages = signal(0);
  readonly totalRecords = signal(0);

  displayedColumns: string[] = [
    'id',
    'type',
    'party',
    'amount',
    'fxrate',
    'status',
    'createdAtUtc',
  ];

  loadTransactions(resetPagination = false) {
    if (this.loading()) return;

    if (resetPagination) {
      this.pageIndex.set(0);
    }

    this.loading.set(true);
    this.errorMessage.set('');
    this.hasSearched.set(true);

    this.walletService
      .getTransactions(this.startDate(), this.endDate(), this.pageIndex() + 1, this.pageSize())
      .pipe(takeUntilDestroyed(this.destroyRef)) // Prevents memory leaks if user navigates away
      .subscribe({
        next: (response: any) => {
          console.info(response.items);
          this.dataSource.data = response.items ?? [];
          this.totalPages.set(Number(response.totalPages) || 0);
          this.totalRecords.set(Number(response.totalRecords) || 0);

          this.loading.set(false);
        },
        error: (error) => {
          this.errorMessage.set(error.error?.message || 'Failed to load transactions.');
          this.loading.set(false);
        },
      });
  }

  onPageChange(event: PageEvent) {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.loadTransactions(false);
  }

  // Prevents Angular Material from destroying/recreating DOM rows on every load
  trackById(index: number, item: Transaction): string | number {
    return item.id;
  }
}