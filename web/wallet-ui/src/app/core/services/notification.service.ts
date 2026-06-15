import { Injectable, signal, Inject, NgZone } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { API_BASE_URL } from './api-base-url.token';

const MAX_NOTIFICATIONS = 50;
const FLUSH_INTERVAL_MS = 120;

export interface WalletNotification {
  type: 'money_sent' | 'money_received' | 'failed_event';
  requestId: string;
  currency: string;
  amount: number;
  message: string;
  createdAtUtc: string;
}

@Injectable({
  providedIn: 'root',
})
export class NotificationService {
  readonly notifications = signal<WalletNotification[]>([]);
  readonly connectionState = signal<'disconnected' | 'connecting' | 'connected'>('disconnected');

  private hub?: signalR.HubConnection;
  private readonly queue: WalletNotification[] = [];
  private flushTimer?: ReturnType<typeof setInterval>;
  private isConnecting = false;

  constructor(
    @Inject(API_BASE_URL) private readonly baseUrl: string,
    private readonly ngZone: NgZone,
  ) {}

  async connect(token: string): Promise<void> {
    if (this.isConnecting || this.hub?.state === signalR.HubConnectionState.Connected) {
      return;
    }

    this.isConnecting = true;
    this.connectionState.set('connecting');

    this.hub = new signalR.HubConnectionBuilder()
      .withUrl(`${this.baseUrl}/hubs/notifications`, {
        accessTokenFactory: () => token,
        withCredentials: true,
        skipNegotiation: false,
        transport: signalR.HttpTransportType.WebSockets,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .build();

    this.hub.off('notification');

    this.hub.on('notification', (payload: WalletNotification) => {
      console.info(payload);
      this.queue.push(payload);
    });

    this.hub.onreconnecting(() => {
      this.connectionState.set('connecting');
    });

    this.hub.onreconnected(() => {
      this.connectionState.set('connected');
    });

    this.hub.onclose(() => {
      this.connectionState.set('disconnected');
    });

    this.startFlushWorker();

    try {
      await this.hub.start();
      this.connectionState.set('connected');
    } finally {
      this.isConnecting = false;
    }
  }

  async disconnect(): Promise<void> {
    this.stopFlushWorker();

    if (!this.hub) {
      return;
    }

    this.hub.off('notification');
    await this.hub.stop();

    this.connectionState.set('disconnected');
  }

  clear(): void {
    this.notifications.set([]);
  }

  private startFlushWorker(): void {
    if (this.flushTimer) {
      return;
    }

    // Run the timer entirely outside of Angular so it doesn't trigger
    // a global Change Detection cycle every 120ms.
    this.ngZone.runOutsideAngular(() => {
      this.flushTimer = setInterval(() => {
        if (this.queue.length === 0) {
          return;
        }

        const items = this.queue.splice(0, this.queue.length);

        // Only re-enter the Angular Zone when we actually have data to flush to the Signals
        this.ngZone.run(() => {
          this.notifications.update((current) => {
            const merged = [...items.reverse(), ...current];
            return merged.slice(0, MAX_NOTIFICATIONS);
          });
        });
      }, FLUSH_INTERVAL_MS);
    });
  }

  private stopFlushWorker(): void {
    if (!this.flushTimer) {
      return;
    }

    clearInterval(this.flushTimer);
    this.flushTimer = undefined;
  }
}