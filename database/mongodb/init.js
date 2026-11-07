// --------------------------------------------------------------------------------------
// Primer arranque de MongoDB: una base y un usuario por microservicio (Report y Audit).
// Cada usuario solo tiene readWrite sobre su base.
//
// La imagen oficial ejecuta este script únicamente cuando el volumen está vacío.
// --------------------------------------------------------------------------------------

function crearBase(base, usuario, password) {
  db.getSiblingDB(base).createUser({
    user: usuario,
    pwd: password,
    roles: [{ role: 'readWrite', db: base }],
  });
  print(`Base ${base} creada para el usuario ${usuario}.`);
}

crearBase('report_db', 'report_svc', process.env.REPORT_DB_PASSWORD);
crearBase('audit_db', 'audit_svc', process.env.AUDIT_DB_PASSWORD);
