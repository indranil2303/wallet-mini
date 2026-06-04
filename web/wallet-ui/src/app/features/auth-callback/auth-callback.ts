import { Component, inject, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';

@Component({
  selector: 'app-auth-callback',
  imports: [],
  templateUrl: './auth-callback.html',
  styleUrl: './auth-callback.scss',
})
export class AuthCallbackComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private authService = inject(AuthService);
  
  ngOnInit() {
    this.route.queryParams.subscribe(params => {
      const accessToken = params['access_token'];
      if (accessToken) {
        this.authService.setSession(accessToken);
        this.router.navigate(['/'], { replaceUrl: true });
      } 
      else {
        this.router.navigate(['/login'], { replaceUrl: true });
      }
    });
  }
}
