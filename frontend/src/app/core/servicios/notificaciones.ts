import { Injectable, signal } from '@angular/core';
import { Alerta, NivelAlerta } from '../modelos/modelos';

export interface Aviso {
  id: number;
  titulo: string;
  mensaje: string;
  nivel: NivelAlerta | 'Info';
  fecha: Date;
}

/** Tono de aviso por nivel: a mayor severidad, más agudo, más repeticiones y más volumen. */
const TONOS: Record<NivelAlerta, { frecuencia: number; repeticiones: number; volumen: number }> = {
  Verde: { frecuencia: 520, repeticiones: 1, volumen: 0.05 },
  Amarillo: { frecuencia: 660, repeticiones: 2, volumen: 0.09 },
  Naranja: { frecuencia: 830, repeticiones: 3, volumen: 0.13 },
  Rojo: { frecuencia: 1040, repeticiones: 5, volumen: 0.18 },
};

/**
 * Avisos visuales y sonoros de la aplicación.
 *
 * El sonido se sintetiza con la Web Audio API en vez de reproducir un archivo: no hay
 * ningún recurso que descargar, el aviso suena igual sin conexión —clave en una comunidad
 * rural— y la urgencia se codifica en el propio tono.
 */
@Injectable({ providedIn: 'root' })
export class Notificaciones {
  private contexto: AudioContext | null = null;
  private secuencia = 0;

  readonly avisos = signal<Aviso[]>([]);
  readonly sonidoActivo = signal(this.leerPreferenciaSonido());

  /**
   * Los navegadores bloquean el audio hasta que hay un gesto del usuario. Se invoca al
   * iniciar sesión para dejar el contexto listo antes de la primera alerta.
   */
  habilitarAudio(): void {
    this.contexto ??= new AudioContext();
    void this.contexto.resume();
  }

  alternarSonido(): void {
    const activo = !this.sonidoActivo();
    this.sonidoActivo.set(activo);
    localStorage.setItem('swmatrc.sonido', String(activo));

    if (activo) this.habilitarAudio();
  }

  /** Aviso completo —visual y sonoro— para una alerta recién emitida. */
  notificarAlerta(alerta: Alerta): void {
    this.mostrar({
      titulo: `${alerta.nivelNombre}: ${alerta.fenomenoNombre}`,
      mensaje: alerta.mensaje,
      nivel: alerta.nivel,
    });

    this.reproducir(alerta.nivel);
  }

  informar(titulo: string, mensaje: string): void {
    this.mostrar({ titulo, mensaje, nivel: 'Info' });
  }

  error(mensaje: string): void {
    this.mostrar({ titulo: 'Error', mensaje, nivel: 'Rojo' });
  }

  descartar(id: number): void {
    this.avisos.update((lista) => lista.filter((a) => a.id !== id));
  }

  private mostrar(datos: Pick<Aviso, 'titulo' | 'mensaje' | 'nivel'>): void {
    const aviso: Aviso = { ...datos, id: ++this.secuencia, fecha: new Date() };

    this.avisos.update((lista) => [aviso, ...lista].slice(0, 5));

    // Las emergencias permanecen hasta que alguien las cierra; el resto se retira solo.
    if (aviso.nivel !== 'Rojo') {
      setTimeout(() => this.descartar(aviso.id), 8000);
    }
  }

  /** Sintetiza una secuencia de pitidos cuya urgencia depende del nivel. */
  private reproducir(nivel: NivelAlerta): void {
    if (!this.sonidoActivo() || nivel === 'Verde') return;

    try {
      this.contexto ??= new AudioContext();
      const contexto = this.contexto;

      // Si el contexto sigue suspendido no hubo gesto del usuario todavía: se intenta
      // reanudar y el aviso visual cubre el hueco.
      if (contexto.state === 'suspended') void contexto.resume();

      const tono = TONOS[nivel];
      const duracion = 0.16;
      const separacion = 0.22;

      for (let i = 0; i < tono.repeticiones; i++) {
        const inicio = contexto.currentTime + i * separacion;

        const oscilador = contexto.createOscillator();
        const ganancia = contexto.createGain();

        oscilador.type = 'square';
        oscilador.frequency.setValueAtTime(tono.frecuencia, inicio);

        // Envolvente con ataque y caída suaves: un corte brusco produce un chasquido.
        ganancia.gain.setValueAtTime(0, inicio);
        ganancia.gain.linearRampToValueAtTime(tono.volumen, inicio + 0.02);
        ganancia.gain.exponentialRampToValueAtTime(0.0001, inicio + duracion);

        oscilador.connect(ganancia).connect(contexto.destination);
        oscilador.start(inicio);
        oscilador.stop(inicio + duracion);
      }
    } catch {
      // Que el audio no esté disponible no puede impedir que la alerta se vea.
    }
  }

  private leerPreferenciaSonido(): boolean {
    return localStorage.getItem('swmatrc.sonido') !== 'false';
  }
}
