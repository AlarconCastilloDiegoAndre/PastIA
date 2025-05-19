// SuggestOptimalTime.cs
using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace PastIA.Function
{
    public class SuggestOptimalTime
    {
        private readonly ILogger _logger;

        public SuggestOptimalTime(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<SuggestOptimalTime>();
        }

        [Function("SuggestOptimalTime")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            _logger.LogInformation("SuggestOptimalTime function processed a request.");

            // Obtener parámetros del querystring
            string userId = req.Query["userId"] ?? string.Empty;
            string medicationId = req.Query["medicationId"] ?? string.Empty;
            
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(medicationId))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Se requiere tanto userId como medicationId.");
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

                // Obtener información sobre el medicamento
                var medication = await GetMedicationInfo(connectionString, userId, medicationId);
                
                if (medication == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("Medicamento no encontrado");
                    return notFoundResponse;
                }
                
                // Obtener historial de tomas
                var historyData = await GetMedicationHistoryData(connectionString, userId, medicationId);
                
                // Calcular hora óptima
                var optimalTimeResult = CalculateOptimalTime(historyData, medication);
                
                // Devolver el resultado
                await response.WriteAsJsonAsync(optimalTimeResult);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al sugerir hora óptima: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error: {ex.Message}");
                return errorResponse;
            }

            return response;
        }
        
        private async Task<Dictionary<string, object>?> GetMedicationInfo(
            string connectionString,
            string userId,
            string medicationId)
        {
            await using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                
                string query = @"
                    SELECT 
                        TimeHour,
                        TimeMinute,
                        ReminderStrategy
                    FROM dbo.Medications
                    WHERE MedicationId = @MedicationId AND UserId = @UserId AND IsActive = 1";
                
                await using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@MedicationId", Guid.Parse(medicationId));
                    command.Parameters.AddWithValue("@UserId", userId);
                    
                    await using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new Dictionary<string, object>
                            {
                                ["timeHour"] = reader.GetInt32(0),
                                ["timeMinute"] = reader.GetInt32(1),
                                ["reminderStrategy"] = reader.GetString(2)
                            };
                        }
                    }
                }
            }
            
            return null;
        }
        
        private async Task<List<Dictionary<string, object>>> GetMedicationHistoryData(
            string connectionString,
            string userId,
            string medicationId)
        {
            var historyData = new List<Dictionary<string, object>>();
            
            await using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                
                string query = @"
                    SELECT
                        h.ScheduledTime,
                        h.ActualTakenTime,
                        h.WasTaken,
                        h.TimeTakenAfterReminder
                    FROM dbo.MedicationHistory h
                    WHERE h.UserId = @UserId AND h.MedicationId = @MedicationId
                    ORDER BY h.ScheduledTime DESC";
                
                await using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", userId);
                    command.Parameters.AddWithValue("@MedicationId", Guid.Parse(medicationId));
                    
                    await using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var historyItem = new Dictionary<string, object>
                            {
                                ["scheduledTime"] = reader.GetDateTime(0),
                                ["wasTaken"] = reader.GetBoolean(2)
                            };
                            
                            if (!reader.IsDBNull(1))
                                historyItem["actualTakenTime"] = reader.GetDateTime(1);
                            
                            if (!reader.IsDBNull(3))
                                historyItem["deviationMinutes"] = reader.GetInt32(3);
                            
                            historyData.Add(historyItem);
                        }
                    }
                }
            }
            
            return historyData;
        }
        
        private Dictionary<string, object> CalculateOptimalTime(
            List<Dictionary<string, object>> historyData,
            Dictionary<string, object> medication)
        {
            // Extraer la hora actual
            int currentHour = (int)medication["timeHour"];
            int currentMinute = (int)medication["timeMinute"];
            string reminderStrategy = (string)medication["reminderStrategy"];
            
            // Si no hay suficientes datos, mantener la hora actual
            if (historyData.Count < 5)
            {
                return new Dictionary<string, object>
                {
                    ["suggestedHour"] = currentHour,
                    ["suggestedMinute"] = currentMinute,
                    ["explanation"] = "Necesitamos más datos de adherencia para optimizar. Por ahora, mantendremos la hora actual.",
                    ["confidence"] = 50,
                    ["isSignificantChange"] = false,
                };
            }
            
            // Calcular adherencia actual
            double currentAdherence = CalculateAdherence(historyData);
            
            // Analizar patrones en la hora de toma real
            var takenWithActualTime = new List<Dictionary<string, object>>();
            foreach (var item in historyData)
            {
                if ((bool)item["wasTaken"] && item.ContainsKey("actualTakenTime"))
                {
                    takenWithActualTime.Add(item);
                }
            }
            
            // Si no hay datos de hora real, devolver la hora actual
            if (takenWithActualTime.Count == 0)
            {
                return new Dictionary<string, object>
                {
                    ["suggestedHour"] = currentHour,
                    ["suggestedMinute"] = currentMinute,
                    ["explanation"] = "No hay suficientes datos sobre tiempos de toma reales para optimizar.",
                    ["confidence"] = 50,
                    ["isSignificantChange"] = false,
                };
            }
            
            // Calcular desviación promedio
            int totalDeviationMinutes = 0;
            foreach (var item in historyData)
            {
                if (item.ContainsKey("deviationMinutes"))
                {
                    totalDeviationMinutes += (int)item["deviationMinutes"];
                }
            }
            
            double avgDeviationMinutes = (double)totalDeviationMinutes / takenWithActualTime.Count;
            
            // Si la desviación promedio es significativa, ajustar la hora
            if (Math.Abs(avgDeviationMinutes) >= 10)
            {
                // Calcular nueva hora sugerida
                int totalMinutes = currentHour * 60 + currentMinute + (int)avgDeviationMinutes;
                
                // Ajustar para mantener dentro de un día (0-23:59)
                totalMinutes = (totalMinutes + 1440) % 1440; // 1440 = 24 * 60
                
                int suggestedHour = totalMinutes / 60;
                int suggestedMinute = totalMinutes % 60;
                
                string direction = avgDeviationMinutes > 0 ? "después" : "antes";
                int absDeviation = (int)Math.Abs(Math.Round(avgDeviationMinutes));
                
                return new Dictionary<string, object>
                {
                    ["suggestedHour"] = suggestedHour,
                    ["suggestedMinute"] = suggestedMinute,
                    ["explanation"] = $"Sueles tomar este medicamento {absDeviation} minutos {direction} de lo programado. Ajustar la hora puede mejorar tu adherencia.",
                    ["confidence"] = 80 + Math.Min(absDeviation, 20),
                    ["isSignificantChange"] = true,
                    ["deviationMinutes"] = (int)Math.Round(avgDeviationMinutes)
                };
            }
            
            // Si no hay una desviación significativa, mantener la hora actual
            return new Dictionary<string, object>
            {
                ["suggestedHour"] = currentHour,
                ["suggestedMinute"] = currentMinute,
                ["explanation"] = "Tu hora actual parece ser óptima para tu adherencia.",
                ["confidence"] = 75,
                ["isSignificantChange"] = false,
            };
        }
        
        private double CalculateAdherence(List<Dictionary<string, object>> historyData)
        {
            if (historyData.Count == 0)
                return 0;
            
            int takenCount = 0;
            foreach (var item in historyData)
            {
                if ((bool)item["wasTaken"])
                {
                    takenCount++;
                }
            }
            
            return (double)takenCount / historyData.Count;
        }
    }
}