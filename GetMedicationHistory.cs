// GetMedicationHistory.cs
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
    public class GetMedicationHistory
    {
        private readonly ILogger _logger;

        public GetMedicationHistory(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<GetMedicationHistory>();
        }

        [Function("GetMedicationHistory")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            _logger.LogInformation("GetMedicationHistory function processed a request.");

            // Obtener el userId del querystring
            string userId = req.Query["userId"] ?? string.Empty;
            string medicationId = req.Query["medicationId"] ?? string.Empty;
            string daysStr = req.Query["days"] ?? "30"; // Por defecto, 30 días
            
            if (string.IsNullOrEmpty(userId))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Por favor proporciona el ID del usuario.");
                return badRequestResponse;
            }
            
            // Convertir días a entero
            if (!int.TryParse(daysStr, out int days))
            {
                days = 30; // Valor por defecto
            }

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

                var historyItems = await GetHistoryFromDatabase(connectionString, userId, medicationId, days);
                
                await response.WriteAsJsonAsync(historyItems);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al obtener historial de medicamentos: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error: {ex.Message}");
                return errorResponse;
            }

            return response;
        }

        private async Task<List<MedicationHistoryModel>> GetHistoryFromDatabase(
            string connectionString, 
            string userId, 
            string medicationId, 
            int days)
        {
            var historyItems = new List<MedicationHistoryModel>();
            var cutoffDate = DateTime.UtcNow.AddDays(-days);

            await using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                
                // Construir la consulta SQL
                string query = @"
                    SELECT
                        h.HistoryId,
                        h.MedicationId,
                        h.UserId,
                        h.ScheduledTime,
                        h.ActualTakenTime,
                        h.WasTaken,
                        h.ReasonNotTaken,
                        h.TimeTakenAfterReminder,
                        h.CreatedAt,
                        c.Location,
                        c.Activity,
                        c.Mood,
                        c.AdherenceStreak,
                        c.TimeDeviation,
                        c.DayOfWeek,
                        c.IsWeekend,
                        c.IsHoliday,
                        c.WasTravelDay
                    FROM dbo.MedicationHistory h
                    LEFT JOIN dbo.MedicationContext c ON h.HistoryId = c.HistoryId
                    WHERE h.UserId = @UserId
                    AND h.ScheduledTime >= @CutoffDate";
                
                // Añadir filtro por medicationId si se proporciona
                if (!string.IsNullOrEmpty(medicationId))
                {
                    query += " AND h.MedicationId = @MedicationId";
                }
                
                query += " ORDER BY h.ScheduledTime DESC";

                await using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", userId);
                    command.Parameters.AddWithValue("@CutoffDate", cutoffDate);
                    
                    if (!string.IsNullOrEmpty(medicationId))
                    {
                        command.Parameters.AddWithValue("@MedicationId", Guid.Parse(medicationId));
                    }
                    
                    await using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var historyItem = new MedicationHistoryModel
                            {
                                Id = reader.GetGuid(0).ToString(),
                                MedicationId = reader.GetGuid(1).ToString(),
                                UserId = reader.GetString(2),
                                ScheduledDateTime = reader.GetDateTime(3).ToString("o"),
                                ActualTakenTime = !reader.IsDBNull(4) ? reader.GetDateTime(4).ToString("o") : null,
                                WasTaken = reader.GetBoolean(5),
                                ReasonNotTaken = !reader.IsDBNull(6) ? reader.GetString(6) : null,
                                DeviationMinutes = !reader.IsDBNull(7) ? reader.GetInt32(7) : null,
                                CreatedAt = reader.GetDateTime(8).ToString("o")
                            };
                            
                            // Añadir contexto si está disponible
                            if (!reader.IsDBNull(9)) // Si hay información de contexto
                            {
                                historyItem.Context = new MedicationContextModel
                                {
                                    Location = !reader.IsDBNull(9) ? reader.GetString(9) : null,
                                    Activity = !reader.IsDBNull(10) ? reader.GetString(10) : null,
                                    Mood = !reader.IsDBNull(11) ? reader.GetString(11) : null,
                                    AdherenceStreak = reader.GetInt32(12),
                                    TimeDeviation = !reader.IsDBNull(13) ? reader.GetInt32(13) : null,
                                    DayOfWeek = reader.GetInt32(14),
                                    IsWeekend = reader.GetBoolean(15),
                                    IsHoliday = reader.GetBoolean(16),
                                    WasTravelDay = reader.GetBoolean(17)
                                };
                            }
                            
                            historyItems.Add(historyItem);
                        }
                    }
                }
            }
            
            return historyItems;
        }
    }

    public class MedicationHistoryModel
    {
        public string Id { get; set; } = string.Empty;
        public string MedicationId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string ScheduledDateTime { get; set; } = string.Empty;
        public string? ActualTakenTime { get; set; }
        public bool WasTaken { get; set; }
        public string? ReasonNotTaken { get; set; }
        public MedicationContextModel? Context { get; set; }
        public int? DeviationMinutes { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
    }

    public class MedicationContextModel
    {
        public string? Location { get; set; }
        public string? Activity { get; set; }
        public string? Mood { get; set; }
        public int AdherenceStreak { get; set; }
        public int? TimeDeviation { get; set; }
        public int DayOfWeek { get; set; }
        public bool IsWeekend { get; set; }
        public bool IsHoliday { get; set; }
        public bool WasTravelDay { get; set; }
    }
}