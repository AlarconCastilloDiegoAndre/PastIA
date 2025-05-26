using System;

namespace PastIA.Function
{
    public static class ConnectionHelper
    {
        public static string GetConnectionString()
        {
            // Método 1: Intentar usar SqlConnectionString directa
            string? connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
            
            if (!string.IsNullOrEmpty(connectionString))
            {
                return connectionString;
            }
            
            // Método 2: Construir desde variables separadas
            var server = Environment.GetEnvironmentVariable("SERVER");
            var database = Environment.GetEnvironmentVariable("DATABASE");
            var userId = Environment.GetEnvironmentVariable("USER_ID");
            var password = Environment.GetEnvironmentVariable("PASSWORD");
            
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(database) || 
                string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(password))
            {
                throw new Exception($"Faltan variables de entorno para la conexión a la base de datos. SERVER: {!string.IsNullOrEmpty(server)}, DATABASE: {!string.IsNullOrEmpty(database)}, USER_ID: {!string.IsNullOrEmpty(userId)}, PASSWORD: {!string.IsNullOrEmpty(password)}");
            }
            
            return $"Server=tcp:{server},1433;Initial Catalog={database};Persist Security Info=False;User ID={userId};Password={password};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";
        }
    }
}