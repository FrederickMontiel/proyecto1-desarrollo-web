package gt.umg.swmatrc.alertas.web;

import gt.umg.swmatrc.alertas.integracion.Integracion.Propiedades;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collection;
import java.util.List;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.http.HttpMethod;
import org.springframework.http.HttpStatus;
import org.springframework.security.authorization.AuthorityAuthorizationManager;
import org.springframework.security.authorization.AuthorizationManager;
import org.springframework.security.authorization.AuthorizationManagers;
import org.springframework.security.config.annotation.method.configuration.EnableMethodSecurity;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.config.http.SessionCreationPolicy;
import org.springframework.security.core.GrantedAuthority;
import org.springframework.security.core.authority.SimpleGrantedAuthority;
import org.springframework.security.oauth2.core.DelegatingOAuth2TokenValidator;
import org.springframework.security.oauth2.core.OAuth2Error;
import org.springframework.security.oauth2.core.OAuth2TokenValidator;
import org.springframework.security.oauth2.core.OAuth2TokenValidatorResult;
import org.springframework.security.oauth2.jose.jws.SignatureAlgorithm;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.security.oauth2.jwt.JwtDecoder;
import org.springframework.security.oauth2.jwt.JwtValidators;
import org.springframework.security.oauth2.jwt.NimbusJwtDecoder;
import org.springframework.security.oauth2.server.resource.authentication.JwtAuthenticationConverter;
import org.springframework.security.web.SecurityFilterChain;
import org.springframework.security.web.access.intercept.RequestAuthorizationContext;

/**
 * Valida los JWT RS256 del Auth Service con su JWKS: firma, emisor, vencimiento y audiencia.
 *
 * <p>Los permisos se comprueban en esta cadena por método y ruta; los {@code @PreAuthorize}
 * de los controladores quedan como segunda barrera.
 *
 * <p>Del token se derivan autoridades: {@code AUD_WEB} o {@code AUD_INTERNA} según la
 * audiencia, y {@code PERM_<permiso>} por cada permiso. Las rutas {@code /api} exigen token de
 * usuario; las {@code /internal}, token de servicio. Un tipo de token nunca sirve donde se
 * espera el otro.
 */
@Configuration
@EnableMethodSecurity
public class Seguridad {

    public static final String AUD_WEB = "AUD_WEB";
    public static final String AUD_INTERNA = "AUD_INTERNA";

    @Bean
    JwtDecoder decodificador(Propiedades propiedades) {
        var jwt = propiedades.jwt();
        NimbusJwtDecoder decodificador = NimbusJwtDecoder.withJwkSetUri(jwt.jwks())
                .jwsAlgorithm(SignatureAlgorithm.RS256)
                .build();

        OAuth2TokenValidator<Jwt> audiencia = token -> {
            List<String> aud = token.getAudience();
            return aud != null && (aud.contains(jwt.audiencia()) || aud.contains(jwt.audienciaInterna()))
                    ? OAuth2TokenValidatorResult.success()
                    : OAuth2TokenValidatorResult.failure(new OAuth2Error("invalid_token", "Audiencia no válida.", null));
        };
        decodificador.setJwtValidator(new DelegatingOAuth2TokenValidator<>(
                JwtValidators.createDefaultWithIssuer(jwt.emisor()), audiencia));
        return decodificador;
    }

    @Bean
    SecurityFilterChain cadena(HttpSecurity http, Propiedades propiedades) throws Exception {
        var conversor = new JwtAuthenticationConverter();
        conversor.setPrincipalClaimName("sub");
        conversor.setJwtGrantedAuthoritiesConverter(token -> autoridades(token, propiedades.jwt()));

        var soloServicios = ambas(AUD_INTERNA, "PERM_interno");
        // Los permisos de escritura se comprueban aquí, antes de leer el cuerpo: si no, un
        // usuario sin permiso que manda datos inválidos recibiría 400 en lugar de 403.
        var atender = ambas(AUD_WEB, "PERM_alertas:atender");
        var configurar = ambas(AUD_WEB, "PERM_reglas:editar");
        var leer = ambas(AUD_WEB, "PERM_alertas:leer");

        http
                .csrf(c -> c.disable())
                .sessionManagement(s -> s.sessionCreationPolicy(SessionCreationPolicy.STATELESS))
                .authorizeHttpRequests(a -> a
                        .requestMatchers("/health/**").permitAll()
                        .requestMatchers("/internal/**").access(soloServicios)
                        .requestMatchers(HttpMethod.POST, "/api/alerts/*/attend", "/api/alerts/*/acknowledge",
                                "/api/alerts/*/close").access(atender)
                        .requestMatchers(HttpMethod.POST, "/api/alerts/rules").access(configurar)
                        .requestMatchers(HttpMethod.PUT, "/api/alerts/rules/**", "/api/alerts/thresholds/**").access(configurar)
                        .requestMatchers(HttpMethod.PATCH, "/api/alerts/rules/**").access(configurar)
                        .requestMatchers(HttpMethod.DELETE, "/api/alerts/rules/**", "/api/alerts/thresholds/**").access(configurar)
                        .requestMatchers(HttpMethod.GET, "/api/**").access(leer)
                        .anyRequest().denyAll())
                .oauth2ResourceServer(o -> o
                        .jwt(j -> j.jwtAuthenticationConverter(conversor))
                        .authenticationEntryPoint((req, res, e) -> problema(req, res, HttpStatus.UNAUTHORIZED,
                                "No autenticado", "El token no es válido o ya venció."))
                        .accessDeniedHandler((req, res, e) -> problema(req, res, HttpStatus.FORBIDDEN,
                                "Acceso denegado", "No tiene permisos para realizar esta operación.")))
                .exceptionHandling(e -> e
                        .authenticationEntryPoint((req, res, ex) -> problema(req, res, HttpStatus.UNAUTHORIZED,
                                "No autenticado", "Falta el token de acceso."))
                        .accessDeniedHandler((req, res, ex) -> problema(req, res, HttpStatus.FORBIDDEN,
                                "Acceso denegado", "No tiene permisos para realizar esta operación.")));
        return http.build();
    }

    private static AuthorizationManager<RequestAuthorizationContext> ambas(String a, String b) {
        return AuthorizationManagers.allOf(
                AuthorityAuthorizationManager.hasAuthority(a), AuthorityAuthorizationManager.hasAuthority(b));
    }

    private static Collection<GrantedAuthority> autoridades(Jwt token, Propiedades.Jwt jwt) {
        List<GrantedAuthority> lista = new ArrayList<>();
        List<String> aud = token.getAudience() == null ? List.of() : token.getAudience();
        if (aud.contains(jwt.audiencia())) lista.add(new SimpleGrantedAuthority(AUD_WEB));
        if (aud.contains(jwt.audienciaInterna())) lista.add(new SimpleGrantedAuthority(AUD_INTERNA));
        List<String> permisos = token.getClaimAsStringList("permissions");
        if (permisos != null) permisos.forEach(p -> lista.add(new SimpleGrantedAuthority("PERM_" + p)));
        return lista;
    }

    private static void problema(HttpServletRequest req, HttpServletResponse res, HttpStatus estado,
                                 String titulo, String detalle) throws IOException {
        res.setStatus(estado.value());
        res.setContentType("application/problem+json");
        res.setCharacterEncoding(StandardCharsets.UTF_8.name());
        res.getWriter().write("""
                {"type":"https://httpstatuses.io/%d","title":"%s","status":%d,"detail":"%s","instance":"%s"}"""
                .formatted(estado.value(), titulo, estado.value(), detalle, req.getRequestURI().replace("\"", "")));
    }
}
