import { ChangeDetectionStrategy, Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-auth',
  standalone: true,
  imports: [CommonModule, MatButtonModule],
  templateUrl: './auth.html',
  styleUrls: ['./auth.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuthComponent implements OnInit {
  private router = inject(Router);
  private authService = inject(AuthService);

  ngOnInit() {
    const accessToken = localStorage.getItem('access_token');
    if (accessToken) {
        this.authService.setSession(accessToken);
        this.router.navigate(['/'], { replaceUrl: true });
    } 
    else {
        console.log('No token found..');
    }    
  }

  login(){
    this.authService.login();
  }
}
