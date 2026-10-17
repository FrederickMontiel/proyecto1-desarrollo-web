package gt.umg.swmatrc.alertas;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.boot.context.properties.ConfigurationPropertiesScan;
import org.springframework.data.jpa.repository.config.EnableJpaRepositories;
import org.springframework.scheduling.annotation.EnableScheduling;

/**
 * Alert Service de SWMATRC: reglas y umbrales, evaluación de riesgo, alertas e historial de
 * fenómenos climáticos (Inundación, Sequía, Tormenta, Helada, Incendio forestal).
 */
@SpringBootApplication
@ConfigurationPropertiesScan
@EnableScheduling
// Los repositorios están declarados como interfaces anidadas en Repositorios.
@EnableJpaRepositories(considerNestedRepositories = true)
public class AlertServiceApplication {

    public static void main(String[] args) {
        SpringApplication.run(AlertServiceApplication.class, args);
    }
}
