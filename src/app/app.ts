import { Component, inject, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ThemeService } from './theme/theme.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly title = signal('azure-admin');

  /** Applies persisted theme on login/register routes (shell mounts its own instance later). */
  private readonly _theme = inject(ThemeService);
}
