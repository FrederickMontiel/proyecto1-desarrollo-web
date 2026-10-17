package gt.umg.swmatrc.alertas.persistencia;

import gt.umg.swmatrc.alertas.persistencia.Entidades.Alerta;
import gt.umg.swmatrc.alertas.persistencia.Entidades.EventoHistorial;
import gt.umg.swmatrc.alertas.persistencia.Entidades.ReglaAlerta;
import gt.umg.swmatrc.alertas.persistencia.Entidades.UmbralSensor;
import java.util.Collection;
import java.util.List;
import java.util.Optional;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.JpaSpecificationExecutor;

/** Repositorios de Spring Data. Los filtros dinámicos de los listados usan especificaciones. */
public final class Repositorios {

    private Repositorios() {
    }

    public interface RepositorioReglas extends JpaRepository<ReglaAlerta, Long>, JpaSpecificationExecutor<ReglaAlerta> {

        List<ReglaAlerta> findByActivaTrue();

        boolean existsByNombreIgnoreCaseAndIdNot(String nombre, Long id);

        boolean existsByNombreIgnoreCase(String nombre);
    }

    public interface RepositorioUmbrales extends JpaRepository<UmbralSensor, Integer> {
    }

    public interface RepositorioAlertas extends JpaRepository<Alerta, Long>, JpaSpecificationExecutor<Alerta> {

        List<Alerta> findByComunidadIdAndFechaCierreIsNull(Integer comunidadId);

        List<Alerta> findByComunidadIdInAndFechaCierreIsNull(Collection<Integer> comunidadIds);

        List<Alerta> findByFechaCierreIsNull();
    }

    public interface RepositorioEventos extends JpaRepository<EventoHistorial, Long>, JpaSpecificationExecutor<EventoHistorial> {

        List<EventoHistorial> findByAlertaIdInAndFechaFinIsNull(Collection<Long> alertaIds);

        Optional<EventoHistorial> findFirstByAlertaIdOrderByFechaInicioDesc(Long alertaId);

        List<EventoHistorial> findByFechaFinIsNull();
    }
}
