// GetMedicaments.cs actualizado
using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace PastIA.Function
{
    public class GetMedicaments
    {
        private readonly ILogger _logger;

        public GetMedicaments(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<GetMedicaments>();
        }

        [Function("GetMedicaments")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            _logger.LogInformation("GetMedicaments function processed a request.");

            // Obtener el userId del querystring con operador de null-coalescing
            string userId = req.Query["userId"] ?? "user_demo_123";

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");

            try
            {
                // Obtener la cadena de conexión desde las variables de entorno
                string? connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
                
                if (string.IsNullOrEmpty(connectionString))
                {
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    await errorResponse.WriteStringAsync("Error: No se encontró la cadena de conexión.");
                    return errorResponse;
                }

                var medications = await GetMedicationsFromDatabase(connectionString, userId);
                
                await response.WriteAsJsonAsync(medications);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al obtener medicamentos: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error: {ex.Message}");
                return errorResponse;
            }

            return response;
        }

        private async Task<List<MedicationModel>> GetMedicationsFromDatabase(string connectionString, string userId)
        {
            var medications = new List<MedicationModel>();

            await using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                
                // Consulta SQL adaptada a la nueva estructura
                string query = @"
                    SELECT 
                        m.MedicationId as Id,
                        m.UserId, 
                        m.Name, 
                        m.Dosage, 
                        m.TimeHour, 
                        m.TimeMinute, 
                        m.DaysOfWeek, 
                        m.Instructions, 
                        m.Importance, 
                        m.SideEffects, 
                        m.Category, 
                        m.TreatmentDuration, 
                        m.ReminderStrategy,
                        m.CreatedAt,
                        m.UpdatedAt
                    FROM dbo.Medications m
                    WHERE m.UserId = @UserId AND m.IsActive = 1
                    ORDER BY m.TimeHour, m.TimeMinute";

                await using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", userId);
                    
                    await using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var medication = new MedicationModel
                            {
                                Id = reader.GetGuid(0).ToString(),
                                UserId = reader.GetString(1),
                                Name = reader.GetString(2),
                                Dosage = reader.GetString(3),
                                TimeHour = reader.GetInt32(4),
                                TimeMinute = reader.GetInt32(5),
                                DaysOfWeek = reader.GetString(6),
                                Instructions = !reader.IsDBNull(7) ? reader.GetString(7) : string.Empty,
                                Importance = !reader.IsDBNull(8) ? reader.GetInt32(8) : 3,
                                SideEffects = !reader.IsDBNull(9) ? reader.GetString(9) : null,
                                Category = !reader.IsDBNull(10) ? reader.GetString(10) : null,
                                TreatmentDuration = !reader.IsDBNull(11) ? reader.GetInt32(11) : null,
                                ReminderStrategy = !reader.IsDBNull(12) ? reader.GetString(12) : "standard",
                                CreatedAt = !reader.IsDBNull(13) ? reader.GetDateTime(13).ToString("o") : DateTime.UtcNow.ToString("o"),
                                UpdatedAt = !reader.IsDBNull(14) ? reader.GetDateTime(14).ToString("o") : null
                            };
                            
                            medications.Add(medication);
                        }
                    }
                }
            }
            
            return medications;
        }
    }

    public class MedicationModel
    {
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Dosage { get; set; } = string.Empty;
        public int TimeHour { get; set; }
        public int TimeMinute { get; set; }
        public string DaysOfWeek { get; set; } = string.Empty; // Formato "L,M,X,J,V,S,D"
        public string Instructions { get; set; } = string.Empty;
        public int Importance { get; set; } = 3;
        public string? SideEffects { get; set; }
        public string? Category { get; set; }
        public int? TreatmentDuration { get; set; }
        public string ReminderStrategy { get; set; } = "standard";
        public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
        public string? UpdatedAt { get; set; }
    }
}