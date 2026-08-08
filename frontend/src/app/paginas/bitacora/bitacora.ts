import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { Pagina, RegistroBitacora } from '../../core/modelos/modelos';
import { Api } from '../../core/servicios/api';

/**
 * Pista de auditoría. Registro de solo lectura de cada acción ejecutada por un usuario:
 * quién, qué, cuándo y desde qué dirección.
 */
@Component({
  selector: 'app-bitacora',
  imports: [DatePipe, FormsModule],
  templateUrl: './bitacora.html',
  styleUrl: './bitacora.scss',
})
export class Bitacora implements OnInit {
  private readonly api = inject(Api);

  protected readonly datos = signal<Pagina<RegistroBitacora> | null>(null);
  protected readonly cargando = signal(false);
  protected readonly pagina = signal(1);
  protected readonly filtro = signal('');

  async ngOnInit(): Promise<void> {
    await this.cargar();
  }

  protected async cargar(): Promise<void> {
    this.cargando.set(true);

    try {
      const resultado = await firstValueFrom(
        this.api.bitacora({
          pagina: this.pagina(),
          tamano: 30,
          accion: this.filtro().trim() || undefined,
        }),
      );

      this.datos.set(resultado);
    } finally {
      this.cargando.set(false);
    }
  }

  protected async buscar(): Promise<void> {
    this.pagina.set(1);
    await this.cargar();
  }

  protected async irA(pagina: number): Promise<void> {
    this.pagina.set(pagina);
    await this.cargar();
  }

  /**
   * El detalle se guarda como JSON. Se muestra en forma de pares legibles en vez del
   * texto crudo, que en una tabla resulta ilegible.
   */
  protected detalleLegible(registro: RegistroBitacora): string {
    if (!registro.detalle) return '—';

    try {
      const objeto = JSON.parse(registro.detalle) as Record<string, unknown>;
      return Object.entries(objeto)
        .map(([clave, valor]) => `${clave}: ${valor}`)
        .join(' · ');
    } catch {
      return registro.detalle;
    }
  }
}
