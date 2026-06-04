import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  OnDestroy,
  OnInit,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../core/services/auth.service';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { NotificationService, WalletNotification } from '../../core/services/notification.service';
import { CurrencySymbolPipe } from "../../core/pipes/currency-symbol-pipe";

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatIconModule,
    MatButtonModule,
    CurrencySymbolPipe
],
  templateUrl: './mobile-shell.html',
  styleUrls: ['./mobile-shell.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MobileShellComponent implements OnInit, OnDestroy {
  // Use modern inject() to remove constructor boilerplate
  private authService = inject(AuthService);
  private notificationService = inject(NotificationService);
  readonly recentToasts = signal<WalletNotification[]>([]);

  constructor() {
    // Listen for new notifications and add them to our transient toast list
    effect(
      () => {
        const latestNotes = this.notificationService.notifications();
        if (latestNotes.length > 0) {
          const newest = latestNotes[0]; // Grab the latest incoming

          // Prevent duplicates if effect triggers multiple times for the same data
          const isDuplicate = untracked(() =>
            this.recentToasts().find((t) => t.requestId === newest.requestId),
          );

          if (!isDuplicate) {
            this.recentToasts.update((current) => [...current, newest]);

            // Auto dismiss after 4 seconds
            setTimeout(() => {
              this.recentToasts.update((current) =>
                current.filter((t) => t.requestId !== newest.requestId),
              );
            }, 10000);
          }
        }
      },
      { allowSignalWrites: true },
    );
  }

  ngOnInit(): void {
    const token = localStorage.getItem('access_token');
    if (token) {
      this.notificationService.connect(token);
    }
  }

  ngOnDestroy() {
    this.notificationService.disconnect();
  }

  isConnected(): boolean {
    return this.notificationService.connectionState() === 'connected';
  }

  isAuthenticated(): boolean {
    return this.authService.isAuthenticated();
  }

  logout(): void {
    localStorage.clear();
    sessionStorage.clear();
    this.notificationService.disconnect();
    this.authService.logout();
  }
}