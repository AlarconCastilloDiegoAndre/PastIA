using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace pastillero_api
{
    public class GetMedications
    {
        private readonly ILogger<GetMedications> _logger;
        private readonly string _connectionString;

        public GetMedications(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<GetMedications>();
            _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString") ?? string.Empty;
        }

        [Function("GetMedications")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "medications")] HttpRequestData req)
        {
            _logger.LogInformation("Procesando solicitud HTTP GET para obtener medicamentos");

            try
            {
                // Validar el ID de usuario
                string userId = req.Query["userId"] ?? string.Empty;
                
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("Solicitud recibida sin ID de usuario");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                        "Por favor proporciona un ID de usuario válido");
                }

                // Validar la cadena de conexión
                if (string.IsNullOrEmpty(_connectionString))
                {
                    _logger.LogError("Cadena de conexión no configurada");
                    return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                        "Error de configuración del servidor");
                }

                _logger.LogInformation("Intentando conectar a la base de datos");
                
                // Obtener los medicamentos
                var medications = await GetMedicationsForUser(userId);
                
                _logger.LogInformation($"Se encontraron {medications.Count} medicamentos para el usuario {userId}");
                
                // Crear respuesta exitosa
                var successResponse = req.CreateResponse(HttpStatusCode.OK);
                await successResponse.WriteAsJsonAsync(medications);
                return successResponse;
            }
            catch (SqlException sqlEx)
            {
                _logger.LogError($"Error de SQL al obtener medicamentos: {sqlEx.Message}");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                    $"Error de base de datos: {sqlEx.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al obtener medicamentos: {ex.Message}");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                    $"Error interno: {ex.Message}");
            }
        }

        private async Task<List<object>> GetMedicationsForUser(string userId)
        {
            List<object> medications = new();
            
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                _logger.LogInformation("Conexión a la base de datos abierta con éxito");
                
                string query = @"SELECT MedicationId, Name, Dosage, Instructions, TimeHour, TimeMinute, 
                              Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday, 
                              ImageUrl, PillImageUrl, IsActive 
                              FROM Medications WHERE UserId = @UserId";
                
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", userId);
                    
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            // Crear objeto que coincida con la estructura de la tabla
                            var medication = new
                            {
                                Id = reader["MedicationId"].ToString(),
                                Name = reader["Name"].ToString(),
                                Dosage = reader["Dosage"].ToString(),
                                Instructions = reader["Instructions"].ToString(),
                                Time = $"{reader["TimeHour"]}:{reader["TimeMinute"]}",
                                DaysOfWeek = new List<bool> {
                                    Convert.ToBoolean(reader["Monday"]),
                                    Convert.ToBoolean(reader["Tuesday"]),
                                    Convert.ToBoolean(reader["Wednesday"]),
                                    Convert.ToBoolean(reader["Thursday"]),
                                    Convert.ToBoolean(reader["Friday"]),
                                    Convert.ToBoolean(reader["Saturday"]),
                                    Convert.ToBoolean(reader["Sunday"])
                                },
                                ImageUrl = reader["ImageUrl"] != DBNull.Value ? reader["ImageUrl"].ToString() : null,
                                PillImageUrl = reader["PillImageUrl"] != DBNull.Value ? reader["PillImageUrl"].ToString() : null,
                                IsActive = Convert.ToBoolean(reader["IsActive"])
                            };
                            
                            medications.Add(medication);
                        }
                    }
                }
            }
            
            return medications;
        }

        private async Task<HttpResponseData> CreateErrorResponse(
            HttpRequestData req, 
            HttpStatusCode statusCode, 
            string message)
        {
            var response = req.CreateResponse(statusCode);
            await response.WriteStringAsync(message);
            return response;
        }
    }
}