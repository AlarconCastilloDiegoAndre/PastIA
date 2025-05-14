using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace pastillero_api
{
    public static class GetMedications
    {
        [FunctionName("GetMedications")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "medications")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Procesando solicitud HTTP GET para obtener medicamentos");

            // Obtener el ID de usuario desde los parámetros de consulta
            string userId = req.Query["userId"];

            if (string.IsNullOrEmpty(userId))
            {
                log.LogWarning("Solicitud recibida sin ID de usuario");
                return new BadRequestObjectResult("Por favor proporciona un ID de usuario válido");
            }

            string connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
            log.LogInformation($"Intentando conectar a la base de datos con la cadena de conexión: {connectionString != null}");
            
            var medications = new List<object>();

            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    log.LogInformation("Conexión a la base de datos abierta con éxito");
                    
                    string query = "SELECT MedicationId, Name, Dosage, Instructions, TimeHour, TimeMinute, " +
                                  "Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday, " +
                                  "ImageUrl, PillImageUrl, IsActive " +
                                  "FROM Medications WHERE UserId = @UserId";
                    
                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);
                        
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                // Crear objeto que coincida con la estructura de tu tabla
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
                
                log.LogInformation($"Se encontraron {medications.Count} medicamentos para el usuario {userId}");
                return new OkObjectResult(medications);
            }
            catch (Exception ex)
            {
                log.LogError($"Error al obtener medicamentos: {ex.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}