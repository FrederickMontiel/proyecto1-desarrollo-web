package gt.umg.swmatrc.alertas.aplicacion;

import gt.umg.swmatrc.alertas.aplicacion.Dtos.FiltroReglas;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.GuardarReglaRequest;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.GuardarUmbralesRequest;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.ReglaDto;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.UmbralesDto;
import gt.umg.swmatrc.alertas.dominio.Enums.NivelAlerta;
import gt.umg.swmatrc.alertas.dominio.Enums.TipoFenomeno;
import gt.umg.swmatrc.alertas.dominio.Enums.TipoSensor;
import gt.umg.swmatrc.alertas.dominio.Umbrales;
import gt.umg.swmatrc.alertas.integracion.Integracion.ClienteAuditoria;
import gt.umg.swmatrc.alertas.persistencia.Entidades.ReglaAlerta;
import gt.umg.swmatrc.alertas.persistencia.Entidades.UmbralSensor;
import gt.umg.swmatrc.alertas.persistencia.Repositorios.RepositorioReglas;
import gt.umg.swmatrc.alertas.persistencia.Repositorios.RepositorioUmbrales;
import jakarta.persistence.criteria.Predicate;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import org.springframework.data.domain.Sort;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/**
 * Reglas configurables y umbrales por sensor ("reglas y umbrales", sección 10). Los
 * cambios se aplican solos en el siguiente ciclo de evaluación, a los pocos segundos.
 */
@Service
public class ServicioConfiguracion {

    private final RepositorioReglas reglas;
    private final RepositorioUmbrales umbrales;
    private final ClienteAuditoria auditoria;

    public ServicioConfiguracion(RepositorioReglas reglas, RepositorioUmbrales umbrales, ClienteAuditoria auditoria) {
        this.reglas = reglas;
        this.umbrales = umbrales;
        this.auditoria = auditoria;
    }

    // -------------------------------------------------------------------------------
    // Reglas
    // -------------------------------------------------------------------------------

    @Transactional(readOnly = true)
    public List<ReglaDto> listarReglas(FiltroReglas f) {
        return reglas.findAll((raiz, q, cb) -> {
            List<Predicate> c = new ArrayList<>();
            if (f.busqueda() != null && !f.busqueda().isBlank()) {
                String texto = "%" + f.busqueda().trim().toLowerCase() + "%";
                c.add(cb.or(cb.like(cb.lower(raiz.get("nombre")), texto), cb.like(cb.lower(raiz.get("mensaje")), texto)));
            }
            if (f.tipoSensor() != null) c.add(cb.equal(raiz.get("tipoSensor"), f.tipoSensor()));
            if (f.fenomeno() != null) c.add(cb.equal(raiz.get("fenomeno"), f.fenomeno()));
            if (f.nivel() != null) c.add(cb.equal(raiz.get("nivel"), f.nivel()));
            if (f.activa() != null) c.add(cb.equal(raiz.get("activa"), f.activa()));
            return cb.and(c.toArray(Predicate[]::new));
        }, Sort.by("tipoSensor", "nivel", "nombre")).stream().map(ReglaDto::desde).toList();
    }

    @Transactional(readOnly = true)
    public ReglaDto obtenerRegla(long id) {
        return ReglaDto.desde(cargarRegla(id));
    }

    @Transactional
    public ReglaDto crearRegla(GuardarReglaRequest r) {
        validar(r);
        if (reglas.existsByNombreIgnoreCase(r.nombre().trim()))
            throw new Excepciones.Conflicto("Ya existe una regla llamada '" + r.nombre().trim() + "'.");

        ReglaAlerta regla = new ReglaAlerta();
        aplicar(regla, r);
        reglas.save(regla);
        auditoria.registrar("ReglaCreada", "ReglaAlerta", regla.id, resumen(regla));
        return ReglaDto.desde(regla);
    }

    @Transactional
    public ReglaDto actualizarRegla(long id, GuardarReglaRequest r) {
        validar(r);
        ReglaAlerta regla = cargarRegla(id);
        if (reglas.existsByNombreIgnoreCaseAndIdNot(r.nombre().trim(), id))
            throw new Excepciones.Conflicto("Ya existe una regla llamada '" + r.nombre().trim() + "'.");

        aplicar(regla, r);
        reglas.saveAndFlush(regla);
        auditoria.registrar("ReglaModificada", "ReglaAlerta", id, resumen(regla));
        return ReglaDto.desde(regla);
    }

    @Transactional
    public ReglaDto cambiarEstadoRegla(long id, boolean activa) {
        ReglaAlerta regla = cargarRegla(id);
        if (regla.activa != activa) {
            regla.activa = activa;
            reglas.saveAndFlush(regla);
            auditoria.registrar(activa ? "ReglaActivada" : "ReglaDesactivada", "ReglaAlerta", id, Map.of("nombre", regla.nombre));
        }
        return ReglaDto.desde(regla);
    }

    /** Las alertas que generó conservan el nombre de la regla: el historial no pierde el rastro. */
    @Transactional
    public void eliminarRegla(long id) {
        ReglaAlerta regla = cargarRegla(id);
        reglas.delete(regla);
        auditoria.registrar("ReglaEliminada", "ReglaAlerta", id, Map.of("nombre", regla.nombre));
    }

    private static void validar(GuardarReglaRequest r) {
        if (r.valorMinimo() == null && r.valorMaximo() == null)
            throw new Excepciones.Validacion("Indique al menos un valor mínimo o un valor máximo.");
        if (r.valorMinimo() != null && r.valorMaximo() != null && r.valorMinimo().compareTo(r.valorMaximo()) > 0)
            throw new Excepciones.Validacion("El valor mínimo no puede ser mayor que el valor máximo.");
        if (r.nivel() == NivelAlerta.Verde)
            throw new Excepciones.Validacion("Una regla de alerta debe tener nivel Amarillo, Naranja o Rojo.");
        if (r.fenomeno() == TipoFenomeno.Ninguno)
            throw new Excepciones.Validacion("Seleccione el fenómeno al que corresponde la regla.");
    }

    private static void aplicar(ReglaAlerta regla, GuardarReglaRequest r) {
        regla.nombre = r.nombre().trim();
        regla.tipoSensor = r.tipoSensor();
        regla.valorMinimo = r.valorMinimo();
        regla.valorMaximo = r.valorMaximo();
        regla.nivel = r.nivel();
        regla.fenomeno = r.fenomeno();
        regla.mensaje = r.mensaje().trim();
        regla.activa = r.activa() == null || r.activa();
    }

    private static Map<String, Object> resumen(ReglaAlerta r) {
        Map<String, Object> d = new LinkedHashMap<>();
        d.put("nombre", r.nombre);
        d.put("tipoSensor", r.tipoSensor.name());
        d.put("nivel", r.nivel.name());
        d.put("fenomeno", r.fenomeno.name());
        d.put("valorMinimo", r.valorMinimo);
        d.put("valorMaximo", r.valorMaximo);
        return d;
    }

    private ReglaAlerta cargarRegla(long id) {
        return reglas.findById(id).orElseThrow(() -> new Excepciones.NoEncontrado("la regla", id));
    }

    // -------------------------------------------------------------------------------
    // Umbrales por sensor
    // -------------------------------------------------------------------------------

    /**
     * Umbrales vigentes de un sensor. Si nadie los ha calibrado se devuelve la plantilla del
     * tipo, que es lo que la evaluación está usando; para eso hace falta conocer el tipo.
     */
    @Transactional(readOnly = true)
    public UmbralesDto umbralesDe(int sensorId, TipoSensor tipo) {
        return umbrales.findById(sensorId)
                .map(u -> UmbralesDto.de(sensorId, u.tipoSensor, u.aUmbrales(), true, u.fechaModificacion))
                .orElseGet(() -> {
                    if (tipo == null)
                        throw new Excepciones.NoEncontrado("El sensor " + sensorId
                                + " no tiene umbrales personalizados. Indique ?tipo= para ver la plantilla que usa.");
                    return UmbralesDto.de(sensorId, tipo, Umbrales.plantilla(tipo), false, null);
                });
    }

    @Transactional(readOnly = true)
    public List<UmbralesDto> umbralesPersonalizados() {
        return umbrales.findAll(Sort.by("sensorId")).stream()
                .map(u -> UmbralesDto.de(u.sensorId, u.tipoSensor, u.aUmbrales(), true, u.fechaModificacion))
                .toList();
    }

    @Transactional
    public UmbralesDto guardarUmbrales(int sensorId, GuardarUmbralesRequest r) {
        Umbrales nuevos = r.aUmbrales();
        String error = nuevos.validar();
        if (error != null) throw new Excepciones.Validacion(error);

        UmbralSensor fila = umbrales.findById(sensorId).orElseGet(() -> {
            UmbralSensor u = new UmbralSensor();
            u.sensorId = sensorId;
            return u;
        });
        fila.tipoSensor = r.tipoSensor();
        fila.aplicar(nuevos);
        umbrales.save(fila);

        Map<String, Object> detalle = new LinkedHashMap<>();
        detalle.put("tipoSensor", r.tipoSensor().name());
        detalle.put("umbrales", nuevos);
        auditoria.registrar("UmbralesActualizados", "Sensor", sensorId, detalle);
        return UmbralesDto.de(sensorId, fila.tipoSensor, nuevos, true, fila.fechaModificacion);
    }

    /** Vuelve a la plantilla del tipo. */
    @Transactional
    public void restablecerUmbrales(int sensorId) {
        if (!umbrales.existsById(sensorId)) return;
        umbrales.deleteById(sensorId);
        auditoria.registrar("UmbralesRestablecidos", "Sensor", sensorId, Map.of());
    }

    public Map<TipoSensor, Umbrales> plantillas() {
        return Umbrales.plantillas();
    }
}
