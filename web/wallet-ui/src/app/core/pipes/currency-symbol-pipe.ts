import { Pipe, PipeTransform, inject } from '@angular/core';
import { CurrencyService } from '../services/currency.service'; // Adjust path if necessary

@Pipe({
  name: 'currencySymbol',
  standalone: true,
  pure: true, // Keeping it pure ensures high performance during change detection
})
export class CurrencySymbolPipe implements PipeTransform {
  // Inject the service to centralize all currency logic
  private currencyService = inject(CurrencyService);

  transform(currencyCode: string | null | undefined): string {
    if (!currencyCode) return '';

    // Delegate to the service which holds the backend cache and logic
    return this.currencyService.getSymbol(currencyCode);
  }
}