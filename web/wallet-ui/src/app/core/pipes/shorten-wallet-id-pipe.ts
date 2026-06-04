import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
  name: 'shortenWalletId',
  standalone: true,
})
export class ShortenWalletIdPipe implements PipeTransform {
  transform(id: string | null | undefined): string {
    if (!id) {
      return '';
    }

    // Safely handles strings shorter than 10 characters if they ever occur
    if (id.length <= 10) {
      return id;
    }

    return `${id.slice(0, 6)}...${id.slice(-4)}`;
  }
}