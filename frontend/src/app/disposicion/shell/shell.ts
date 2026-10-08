import { Component, OnInit, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Autenticacion } from '../../core/servicios/autenticacion';
import { EstadoMonitoreo } from '../../core/servicios/estado-monitoreo';
import { Notificaciones } from '../../core/servicios/notificaciones';
import { Avisos } from '../avisos/avisos';

interface EntradaMenu {
  ruta: string;
  etiqueta: string;
  icono: string;
  soloAdministrador?: boolean;
}

/**
 * Armazón de la aplicación: barra superior con el estado del canal, navegación lateral
 * y salida del enrutador. Es también el punto donde se abre la conexión en tiempo real,
 * porque solo se llega aquí con sesión iniciada.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Avisos],
  templateUrl: './shell.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './shell.scss',
})
export class Shell implements OnInit {
  protected readonly auth = inject(Autenticacion);
  protected readonly estado = inject(EstadoMonitoreo);
  protected readonly notificaciones = inject(Notificaciones);

  protected readonly menuAbierto = signal(false);
  protected readonly tema = signal<'oscuro' | 'claro'>(this.temaGuardado());

  protected readonly menu: EntradaMenu[] = [
    { ruta: '/dashboard', etiqueta: 'Monitoreo', icono: '📊' },
    { ruta: '/alertas', etiqueta: 'Alertas', icono: '🚨' },
    { ruta: '/historial', etiqueta: 'Historial', icono: '🗂️' },
    { ruta: '/comunidades', etiqueta: 'Comunidades', icono: '🏘️' },
    { ruta: '/sensores', etiqueta: 'Sensores', icono: '🛰️' },
    { ruta: '/lecturas', etiqueta: 'Lecturas', icono: '📈' },
    { ruta: '/reglas', etiqueta: 'Reglas de alerta', icono: '⚙️' },
    { ruta: '/usuarios', etiqueta: 'Usuarios', icono: '👥', soloAdministrador: true },
    { ruta: '/bitacora', etiqueta: 'Bitácora', icono: '📝', soloAdministrador: true },
  ];

  async ngOnInit(): Promise<void> {
    this.aplicarTema(this.tema());

    await this.estado.iniciar();
    await this.estado.cargarComunidades();
  }

  protected get entradasVisibles(): EntradaMenu[] {
    return this.menu.filter((e) => !e.soloAdministrador || this.auth.puedeAdministrar());
  }

  protected async cambiarComunidad(evento: Event): Promise<void> {
    const id = Number((evento.target as HTMLSelectElement).value);
    const comunidad = this.estado.comunidades().find((c) => c.id === id);

    if (comunidad) await this.estado.seleccionarComunidad(comunidad);
  }

  protected alternarTema(): void {
    const nuevo = this.tema() === 'oscuro' ? 'claro' : 'oscuro';
    this.tema.set(nuevo);
    this.aplicarTema(nuevo);
    localStorage.setItem('swmatrc.tema', nuevo);
  }

  protected async cerrarSesion(): Promise<void> {
    // Primero se cierra el WebSocket: dejarlo abierto mantendría vivo un canal
    // autenticado con un token que el usuario acaba de abandonar.
    await this.estado.detener();
    await this.auth.salir();
  }

  protected etiquetaConexion(): string {
    switch (this.estado.estadoConexion()) {
      case 'conectado':
        return 'En vivo';
      case 'conectando':
        return 'Conectando…';
      case 'reconectando':
        return 'Reconectando…';
      default:
        return 'Sin conexión';
    }
  }

  private aplicarTema(tema: 'oscuro' | 'claro'): void {
    document.documentElement.dataset['tema'] = tema;
  }

  private temaGuardado(): 'oscuro' | 'claro' {
    return localStorage.getItem('swmatrc.tema') === 'claro' ? 'claro' : 'oscuro';
  }
}
