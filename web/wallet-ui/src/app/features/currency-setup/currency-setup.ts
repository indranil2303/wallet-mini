import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { CurrencySymbolPipe } from '../../core/pipes/currency-symbol-pipe';
import { WalletService } from '../../core/services/wallet.service';
import { CurrencyRecord, CurrencyService } from '../../core/services/currency.service';

@Component({
  selector: 'app-currency-setup',
  standalone: true,
  imports: [CommonModule, CurrencySymbolPipe],
  templateUrl: './currency-setup.html',
  styleUrls: ['./currency-setup.scss'],
})
export class CurrencySetupComponent implements OnInit {
  private router = inject(Router);
  private currencyService = inject(CurrencyService);
  private walletService = inject(WalletService);

  // 1. Converted to a Signal
  supportedCurrencies = signal<CurrencyRecord[]>([]);
  selectedCurrency = signal<string | null>(null);
  isSaving = signal(false);

  // 2. Await the data securely now that the user is actually authenticated
  async ngOnInit() {
    const currencies = await this.currencyService.getCurrencies();
    this.supportedCurrencies.set(currencies);
  }

  saveAndContinue() {
    const currency = this.selectedCurrency();
    if (!currency) return;

    this.isSaving.set(true);
    const payload = { currencyCode: currency };
    this.walletService.updateDefaultCurrency(payload).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.router.navigate(['/dashboard']);
      },
      error: (err) => {
        console.error('Failed to set default currency', err);
        this.isSaving.set(false);
      },
    });
  }
}
