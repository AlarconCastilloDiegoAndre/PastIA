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
                string? connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
                
                if (string.IsNullOrEmpty(connectionString))
                {
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    await errorResponse.WriteStringAsync("Error: No se encontró la cadena de conexión.");
                    return errorResponse;
                }
                
                string userId;
                
                // Si se proporciona un ID, usarlo, de lo contrario generar uno nuevo
                if (!string.IsNullOrEmpty(userData.Id))
                {
                    userId = userData.Id;
                }
                else
                {
                    // Obtenemos el último ID para generar uno nuevo en secuencia
                    userId = await GetNextUserIdAsync(connectionString);
                }

                await using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Primero verificar si el usuario ya existe
                    string checkQuery = "SELECT COUNT(*) FROM dbo.Users WHERE UserId = @UserId";
                    await using (SqlCommand checkCommand = new SqlCommand(checkQuery, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@UserId", userId);
                        int userCount = (int)(await checkCommand.ExecuteScalarAsync() ?? 0);
                        
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
                            }
                        }
                    }
                }
                
                // Devolver los datos del usuario con el ID
                userData.Id = userId;
                await response.WriteAsJsonAsync(userData);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al registrar usuario: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error: {ex.Message}");
                return errorResponse;
            }
            
            return response;
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
    }

    public class UserModel
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
    }
}