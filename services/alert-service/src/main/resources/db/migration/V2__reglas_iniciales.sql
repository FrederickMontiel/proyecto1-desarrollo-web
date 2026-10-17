-- --------------------------------------------------------------------------------------
-- Reglas de partida para las magnitudes que el motor integrado no cubre (reservorio y
-- humo), las mismas de la Fase 2. El administrador puede editarlas, desactivarlas o crear
-- otras. Si la migración de datos trae las reglas de la Fase 2, primero vacía esta tabla.
--
-- nivel: 1 Amarillo, 2 Naranja, 3 Rojo.  fenomeno: 1 Inundacion, 2 Sequia, 5 IncendioForestal.
-- --------------------------------------------------------------------------------------

INSERT INTO reglas_alerta (nombre, tipo_sensor, valor_minimo, valor_maximo, nivel, fenomeno, mensaje) VALUES
('Reservorio cerca del rebalse', 'NivelReservorio', 92, 97, 2, 1,
 'Alerta: el reservorio {sensor} está al {valor} {unidad}. Preparar el desfogue controlado.'),
('Reservorio en rebalse', 'NivelReservorio', 97, NULL, 3, 1,
 'EMERGENCIA: el reservorio {sensor} rebasa el {valor} {unidad}. Evacuar las zonas aguas abajo.'),
('Reservorio en nivel crítico', 'NivelReservorio', NULL, 20, 2, 2,
 'Alerta de desabastecimiento: el reservorio {sensor} bajó al {valor} {unidad}. Racionar el agua.'),
('Presencia de humo', 'Humo', 55, 150, 1, 5,
 'Precaución: {valor} {unidad} de partículas en {comunidad}. Verificar posibles quemas.'),
('Humo denso', 'Humo', 150, 250, 2, 5,
 'Alerta de incendio: {valor} {unidad} de partículas en {comunidad}. Avisar a la brigada.'),
('Incendio declarado', 'Humo', 250, NULL, 3, 5,
 'EMERGENCIA por incendio: {valor} {unidad} de partículas. Evacuar y llamar a bomberos.');
