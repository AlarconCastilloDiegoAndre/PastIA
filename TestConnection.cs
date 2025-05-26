using System;
using Microsoft.Data.SqlClient;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace PastIA.Function
{
    public class TestConnection
    {
        private readonly ILogger _logger;

        public TestConnection(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<TestConnection>();
        }

        [Function("TestConnection")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req)
        {
            _logger.LogInformation("TestConnection function processed a request.");

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            
            try
            {
                // Método 1: Usar SqlConnectionString si existe
                string? connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
                
                // Método 2: Si no existe, construir desde variables separadas
                if (string.IsNullOrEmpty(connectionString))
                {
                    connectionString = GetConnectionStringFromParts();
                }
                
                if (string.IsNullOrEmpty(connectionString))
                {
                    throw new Exception("No se pudo obtener cadena de conexión de ninguna fuente");
                }
                
                _logger.LogInformation($"Intentando conectar con: {MaskConnectionString(connectionString)}");
                
                await using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    string query = "SELECT 1 as TestValue, GETDATE() as ServerTime, DB_NAME() as DatabaseName, USER_NAME() as CurrentUser";
                    
                    await using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        await using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                var result = new
                                {
                                    success = true,
                                    message = "Conexión exitosa a la base de datos",
                                    testValue = reader.GetInt32("TestValue"),
                                    serverTime = reader.GetDateTime("ServerTime"),
                                    databaseName = reader.GetString("DatabaseName"),
                                    currentUser = reader.GetString("CurrentUser"),
                                    connectionMethod = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SqlConnectionString")) ? "Variables separadas" : "SqlConnectionString",
                                    maskedConnectionString = MaskConnectionString(connectionString)
                                };
                                
                                await response.WriteAsJsonAsync(result);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error en conexión: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                
                var errorResult = new
                {
                    success = false,
                    message = $"Error de conexión: {ex.Message}",
                    stackTrace = ex.StackTrace,
                    variables = new
                    {
                        sqlConnectionString = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SqlConnectionString")) ? "Configurada" : "No configurada",
                        server = Environment.GetEnvironmentVariable("SERVER"),
                        database = Environment.GetEnvironmentVariable("DATABASE"),
                        userId = Environment.GetEnvironmentVariable("USER_ID"),
                        hasPassword = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PASSWORD")),
                        passwordLength = Environment.GetEnvironmentVariable("PASSWORD")?.Length ?? 0
                    },
                    suggestions = new[]
                    {
                        "Verificar que las variables de entorno estén configuradas correctamente",
                        "Verificar que Azure SQL permita conexiones desde Azure Functions",
                        "Revisar la configuración de firewall de Azure SQL",
                        "Verificar que las credenciales sean correctas"
                    }
                };
                
                response = req.CreateResponse(HttpStatusCode.InternalServerError);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await response.WriteAsJsonAsync(errorResult);
            }
            
            return response;
        }
        
        private string GetConnectionStringFromParts()
        {
            var server = Environment.GetEnvironmentVariable("SERVER");
            var database = Environment.GetEnvironmentVariable("DATABASE");
            var userId = Environment.GetEnvironmentVariable("USER_ID");
            var password = Environment.GetEnvironmentVariable("PASSWORD");
            
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(database) || 
                string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(password))
            {
                throw new Exception($"Faltan variables de entorno. SERVER: {!string.IsNullOrEmpty(server)}, DATABASE: {!string.IsNullOrEmpty(database)}, USER_ID: {!string.IsNullOrEmpty(userId)}, PASSWORD: {!string.IsNullOrEmpty(password)}");
            }
            
            return $"Server=tcp:{server},1433;Initial Catalog={database};Persist Security Info=False;User ID={userId};Password={password};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";
        }
        
        private string MaskConnectionString(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return "NULL";
            
            // Enmascarar la contraseña en la cadena de conexión
            var parts = connectionString.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Trim().StartsWith("Password=", StringComparison.OrdinalIgnoreCase))
                {
                    parts[i] = "Password=***MASKED***";
                }
            }
            return string.Join(";", parts);
        }
    }
}