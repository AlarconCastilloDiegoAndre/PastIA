// RegisterUser.cs
using System;
using Microsoft.Data.SqlClient;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace PastIA.Function
{
    public class RegisterUser
    {
        private readonly ILogger _logger;

        public RegisterUser(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<RegisterUser>();
        }

        [Function("RegisterUser")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
        {
            _logger.LogInformation("RegisterUser function processed a request.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation($"Request body: {requestBody}");
            
            var userData = JsonSerializer.Deserialize<UserModel>(requestBody, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (userData == null)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("El cuerpo de la solicitud no es válido.");
                return badRequestResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            
            try
            {
                // Obtener cadena de conexión
                string connectionString = GetConnectionString();
                _logger.LogInformation($"Connection string obtenida: {MaskConnectionString(connectionString)}");
                
                string userId;
                
                // Si se proporciona un ID, usarlo, de lo contrario generar uno nuevo
                if (!string.IsNullOrEmpty(userData.Id))
                {
                    userId = userData.Id;
                    _logger.LogInformation($"Usando ID proporcionado: {userId}");
                }
                else
                {
                    // Obtenemos el último ID para generar uno nuevo en secuencia
                    userId = await GetNextUserIdAsync(connectionString);
                    _logger.LogInformation($"ID generado: {userId}");
                }

                await using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    _logger.LogInformation("Conexión a base de datos abierta exitosamente");
                    
                    // Primero verificar si el usuario ya existe
                    string checkQuery = "SELECT COUNT(*) FROM dbo.Users WHERE UserId = @UserId";
                    await using (SqlCommand checkCommand = new SqlCommand(checkQuery, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@UserId", userId);
                        int userCount = (int)(await checkCommand.ExecuteScalarAsync() ?? 0);
                        
                        _logger.LogInformation($"Usuario existe: {userCount > 0}");
                        
                        // Si el usuario ya existe, actualizar información
                        if (userCount > 0)
                        {
                            string updateQuery = @"
                                UPDATE dbo.Users
                                SET UserName = @UserName,
                                    Email = @Email,
                                    UpdatedAt = GETUTCDATE(),
                                    LastLogin = GETUTCDATE()
                                WHERE UserId = @UserId";
                            
                            await using (SqlCommand updateCommand = new SqlCommand(updateQuery, connection))
                            {
                                updateCommand.Parameters.AddWithValue("@UserId", userId);
                                updateCommand.Parameters.AddWithValue("@UserName", userData.Name ?? "Usuario");
                                
                                if (string.IsNullOrEmpty(userData.Email))
                                    updateCommand.Parameters.AddWithValue("@Email", DBNull.Value);
                                else
                                    updateCommand.Parameters.AddWithValue("@Email", userData.Email);
                                
                                await updateCommand.ExecuteNonQueryAsync();
                                _logger.LogInformation("Usuario actualizado exitosamente");
                            }
                        }
                        // Si no existe, crear nuevo usuario
                        else
                        {
                            string insertQuery = @"
                                INSERT INTO dbo.Users (UserId, UserName, Email, CreatedAt, LastLogin)
                                VALUES (@UserId, @UserName, @Email, GETUTCDATE(), GETUTCDATE())";
                            
                            await using (SqlCommand insertCommand = new SqlCommand(insertQuery, connection))
                            {
                                insertCommand.Parameters.AddWithValue("@UserId", userId);
                                insertCommand.Parameters.AddWithValue("@UserName", userData.Name ?? "Usuario");
                                
                                if (string.IsNullOrEmpty(userData.Email))
                                    insertCommand.Parameters.AddWithValue("@Email", DBNull.Value);
                                else
                                    insertCommand.Parameters.AddWithValue("@Email", userData.Email);
                                
                                await insertCommand.ExecuteNonQueryAsync();
                                _logger.LogInformation("Usuario creado exitosamente");
                            }
                        }
                    }
                }
                
                // Devolver los datos del usuario con el ID
                var responseData = new
                {
                    id = userId,
                    name = userData.Name ?? "Usuario",
                    email = userData.Email
                };
                
                await response.WriteAsJsonAsync(responseData);
                _logger.LogInformation($"Respuesta enviada: {JsonSerializer.Serialize(responseData)}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al registrar usuario: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                var errorData = new
                {
                    error = ex.Message,
                    stackTrace = ex.StackTrace,
                    suggestions = new[]
                    {
                        "Verificar que la tabla dbo.Users exista en la base de datos",
                        "Verificar los permisos del usuario de base de datos",
                        "Revisar la configuración de las variables de entorno"
                    }
                };
                await errorResponse.WriteAsJsonAsync(errorData);
                return errorResponse;
            }
            
            return response;
        }
        
        private string GetConnectionString()
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
        
        private async Task<string> GetNextUserIdAsync(string connectionString)
        {
            await using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                
                string query = "SELECT MAX(UserId) FROM dbo.Users WHERE UserId LIKE 'USR%'";
                await using (SqlCommand command = new SqlCommand(query, connection))
                {
                    object? result = await command.ExecuteScalarAsync();
                    
                    if (result == null || result == DBNull.Value)
                    {
                        return "USR001"; // Primer usuario
                    }
                    
                    string? lastId = result.ToString();
                    if (lastId != null && lastId.Length >= 6 && int.TryParse(lastId.Substring(3), out int number))
                    {
                        int nextNumber = number + 1;
                        return $"USR{nextNumber:D3}"; // Formato USR001, USR002, etc.
                    }
                    
                    return "USR001"; // Si hay algún error, empezamos desde USR001
                }
            }
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

    public class UserModel
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
    }
}