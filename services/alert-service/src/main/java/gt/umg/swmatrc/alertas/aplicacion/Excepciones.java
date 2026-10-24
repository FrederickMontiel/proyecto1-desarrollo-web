package gt.umg.swmatrc.alertas.aplicacion;

/** Errores esperados de negocio. El manejador web los traduce a ProblemDetails. */
public final class Excepciones {

    private Excepciones() {
    }

    /** Se responde 404. */
    public static class NoEncontrado extends RuntimeException {
        public NoEncontrado(String entidad, Object id) {
            super("No se encontró " + entidad + " con identificador '" + id + "'.");
        }

        public NoEncontrado(String mensaje) {
            super(mensaje);
        }
    }

    /** Los datos no cumplen las reglas de negocio. Se responde 400. */
    public static class Validacion extends RuntimeException {
        public Validacion(String mensaje) {
            super(mensaje);
        }
    }

    /** Choca con el estado actual (nombre duplicado, alerta ya cerrada). Se responde 409. */
    public static class Conflicto extends RuntimeException {
        public Conflicto(String mensaje) {
            super(mensaje);
        }
    }
}
