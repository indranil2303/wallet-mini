import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { WalletService } from '../../core/services/wallet.service';
import { NotificationService, WalletNotification } from '../../core/services/notification.service';
import { CurrencySymbolPipe } from '../../core/pipes/currency-symbol-pipe';
import { filter } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterModule, CurrencySymbolPipe],
  templateUrl: './dashboard.html',
  styleUrls: ['./dashboard.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DashboardComponent implements OnInit {
  readonly walletService = inject(WalletService);
  readonly notificationService = inject(NotificationService);

  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  wallet$ = this.walletService.walletState$;
  readonly activities = computed(() => this.notificationService.notifications());

  isSyncing = signal(false);
  private lastProcessedNotificationId = '';

  constructor() {
  // Trigger sync state when a new SignalR notification hits
    effect(() => {
      const notes = this.notificationService.notifications();
      if (notes.length > 0 && notes[0].requestId !== this.lastProcessedNotificationId) {
        this.lastProcessedNotificationId = notes[0].requestId;
        this.isSyncing.set(true);
        this.walletService.refreshWalletSummary(); // Ask API for new balance
      }
    }, { allowSignalWrites: true });

    // When wallet state actually updates, kill the sync spinner
    this.wallet$.subscribe(() => {
      this.isSyncing.set(false);
    });
  }

  ngOnInit(): void {
    this.walletService.refreshWalletSummary();
    this.wallet$
      .pipe(
        filter((wallet) => wallet !== null), // Wait until data actually loads
        takeUntilDestroyed(this.destroyRef), // Prevent memory leaks
      )
      .subscribe((wallet) => {
        if (!wallet?.isDefaultCurrencySet) {
          this.router.navigate(['/currency-setup']);
        }
        else {
          sessionStorage.setItem('defaultCurrency', wallet.currencyCode || 'INR');
        }
      });
  }

  addMoney(): void {
    console.log('Add money clicked');
  }

  getStatusMessage(status: string): string {
    const normalized = (status || '').toLowerCase();
    switch (normalized) {
      case 'inactive':
        return 'Note: Your wallet is currently inactive. Please complete setup or contact support to unlock all features.';
      case 'frozen':
        return 'Note: Your wallet has been frozen for security reasons. Withdrawals and transfers are disabled.';
      case 'closed':
        return 'Note: Your wallet is closed. You can no longer perform transactions.';
      case 'active':
      default:
        return ''; // Returns empty string to hide the banner
    }
  }

  isActionDisabled(status: string): boolean {
    const normalized = (status || '').toLowerCase();
    return normalized === 'inactive' || normalized === 'frozen' || normalized === 'closed';
  }
}