// AnalyzeMedicationPatterns.cs
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
    public class AnalyzeMedicationPatterns
    {
        private readonly ILogger _logger;

        public AnalyzeMedicationPatterns(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<AnalyzeMedicationPatterns>();
        }

        [Function("AnalyzeMedicationPatterns")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            _logger.LogInformation("AnalyzeMedicationPatterns function processed a request.");

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

                // Obtener datos históricos del medicamento
                var historyData = await GetMedicationHistoryData(connectionString, userId, medicationId);
                
                // Analizar patrones
                var patterns = AnalyzePatterns(historyData);
                
                // Devolver los patrones detectados
                await response.WriteAsJsonAsync(patterns);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al analizar patrones: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error: {ex.Message}");
                return errorResponse;
            }

            return response;
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
                        h.TimeTakenAfterReminder,
                        c.DayOfWeek,
                        c.IsWeekend,
                        c.Location,
                        c.Activity,
                        c.Mood,
                        c.IsHoliday,
                        c.WasTravelDay
                    FROM dbo.MedicationHistory h
                    LEFT JOIN dbo.MedicationContext c ON h.HistoryId = c.HistoryId
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
                                historyItem["timeTakenAfterReminder"] = reader.GetInt32(3);
                            
                            if (!reader.IsDBNull(4))
                                historyItem["dayOfWeek"] = reader.GetInt32(4);
                            
                            if (!reader.IsDBNull(5))
                                historyItem["isWeekend"] = reader.GetBoolean(5);
                            
                            if (!reader.IsDBNull(6))
                                historyItem["location"] = reader.GetString(6);
                            
                            if (!reader.IsDBNull(7))
                                historyItem["activity"] = reader.GetString(7);
                            
                            if (!reader.IsDBNull(8))
                                historyItem["mood"] = reader.GetString(8);
                            
                            if (!reader.IsDBNull(9))
                                historyItem["isHoliday"] = reader.GetBoolean(9);
                            
                            if (!reader.IsDBNull(10))
                                historyItem["wasTravelDay"] = reader.GetBoolean(10);
                            
                            historyData.Add(historyItem);
                        }
                    }
                }
            }
            
            return historyData;
        }
        
        private Dictionary<string, object> AnalyzePatterns(List<Dictionary<string, object>> historyData)
        {
            // Inicializar resultado
            var result = new Dictionary<string, object>
            {
                ["hasPatterns"] = false,
                ["patterns"] = new List<Dictionary<string, object>>(),
                ["adherence"] = 0
            };
            
            // Si no hay suficientes datos, retornar
            if (historyData.Count < 5)
            {
                return result;
            }
            
            // Calcular adherencia global
            int totalItems = historyData.Count;
            int takenItems = 0;
            foreach (var item in historyData)
            {
                if ((bool)item["wasTaken"])
                {
                    takenItems++;
                }
            }
            
            double adherencePercentage = (double)takenItems / totalItems * 100;
            result["adherence"] = Math.Round(adherencePercentage);
            
            // Lista para almacenar patrones detectados
            var patterns = new List<Dictionary<string, object>>();
            
            // Analizar patrones por día de la semana
            var dayGroups = new Dictionary<int, List<Dictionary<string, object>>>();
            foreach (var item in historyData)
            {
                if (item.ContainsKey("dayOfWeek"))
                {
                    int day = (int)item["dayOfWeek"];
                    if (!dayGroups.ContainsKey(day))
                    {
                        dayGroups[day] = new List<Dictionary<string, object>>();
                    }
                    dayGroups[day].Add(item);
                }
            }
            
            // Calcular adherencia por día
            var dayAdherences = new Dictionary<int, double>();
            foreach (var entry in dayGroups)
            {
                int day = entry.Key;
                var items = entry.Value;
                
                int dayTakenCount = 0;
                foreach (var item in items)
                {
                    if ((bool)item["wasTaken"])
                    {
                        dayTakenCount++;
                    }
                }
                
                dayAdherences[day] = (double)dayTakenCount / items.Count * 100;
            }
            
            // Detectar días con baja adherencia
            foreach (var entry in dayAdherences)
            {
                int day = entry.Key;
                double dayAdherence = entry.Value;
                
                if (dayAdherence < adherencePercentage - 20) // 20% menos que el promedio
                {
                    patterns.Add(new Dictionary<string, object>
                    {
                        ["tipo"] = "adherencia",
                        ["descripcion"] = $"Los {GetDayName(day)} tienes una adherencia {Math.Round(dayAdherence)}% menor al promedio",
                        ["sugerencia"] = $"Configura recordatorios adicionales para los {GetDayName(day)}",
                        ["accion"] = "recordar",
                        ["confianza"] = 85 + (int)Math.Round(adherencePercentage - dayAdherence)
                    });
                }
            }
            
            // Analizar patrones de tiempo
            var takenWithDeviation = new List<int>();
            foreach (var item in historyData)
            {
                if ((bool)item["wasTaken"] && item.ContainsKey("timeTakenAfterReminder"))
                {
                    takenWithDeviation.Add((int)item["timeTakenAfterReminder"]);
                }
            }
            
            if (takenWithDeviation.Count > 0)
            {
                // Calcular desviación promedio
                int totalDeviation = 0;
                foreach (var deviation in takenWithDeviation)
                {
                    totalDeviation += deviation;
                }
                
                double avgDeviation = (double)totalDeviation / takenWithDeviation.Count;
                
                // Si hay una desviación consistente, sugerir ajuste
                if (Math.Abs(avgDeviation) >= 10) // 10 minutos o más
                {
                    string direction = avgDeviation > 0 ? "después" : "antes";
                    patterns.Add(new Dictionary<string, object>
                    {
                        ["tipo"] = "tiempo",
                        ["descripcion"] = $"Sueles tomar este medicamento {Math.Abs(Math.Round(avgDeviation))} minutos {direction} de lo programado",
                        ["sugerencia"] = "Reajustar el horario para reflejar tu patrón real",
                        ["accion"] = "ajustar",
                        ["confianza"] = 80 + Math.Min((int)Math.Abs(Math.Round(avgDeviation)), 20)
                    });
                }
            }
            
            // Analizar patrones contextuales (ubicación)
            var locationCounts = new Dictionary<string, int>();
            var locationTaken = new Dictionary<string, int>();
            
            foreach (var item in historyData)
            {
                if (item.ContainsKey("location") && item["location"] != null)
                {
                    string location = (string)item["location"];
                    
                    if (!locationCounts.ContainsKey(location))
                    {
                        locationCounts[location] = 0;
                        locationTaken[location] = 0;
                    }
                    
                    locationCounts[location]++;
                    
                    if ((bool)item["wasTaken"])
                    {
                        locationTaken[location]++;
                    }
                }
            }
            
            // Encontrar la ubicación más común
            string? mostCommonLocation = null;
            int maxCount = 0;
            
            foreach (var entry in locationCounts)
            {
                if (entry.Value > maxCount)
                {
                    maxCount = entry.Value;
                    mostCommonLocation = entry.Key;
                }
            }
            
            // Verificar adherencia por ubicación
            if (mostCommonLocation != null && locationCounts.Count > 1)
            {
                double commonLocationAdherence = (double)locationTaken[mostCommonLocation] / locationCounts[mostCommonLocation];
                
                // Calcular adherencia en otras ubicaciones
                int otherLocationsCount = 0;
                int otherLocationsTaken = 0;
                
                foreach (var entry in locationCounts)
                {
                    if (entry.Key != mostCommonLocation)
                    {
                        otherLocationsCount += entry.Value;
                        otherLocationsTaken += locationTaken[entry.Key];
                    }
                }
                
                if (otherLocationsCount > 0)
                {
                    double otherLocationsAdherence = (double)otherLocationsTaken / otherLocationsCount;
                    
                    if (commonLocationAdherence - otherLocationsAdherence > 0.2) // 20% de diferencia
                    {
                        patterns.Add(new Dictionary<string, object>
                        {
                            ["tipo"] = "contexto",
                            ["descripcion"] = $"Mayor probabilidad de omisión cuando no estás en {mostCommonLocation}",
                            ["sugerencia"] = $"Preparar dosis para llevar cuando salgas de {mostCommonLocation}",
                            ["accion"] = "informar",
                            ["confianza"] = 75 + (int)Math.Round((commonLocationAdherence - otherLocationsAdherence) * 100)
                        });
                    }
                }
            }
            
            // Actualizar resultado
            result["hasPatterns"] = patterns.Count > 0;
            result["patterns"] = patterns;
            
            return result;
        }
        
        private string GetDayName(int day)
        {
            switch (day)
            {
                case 1: return "lunes";
                case 2: return "martes";
                case 3: return "miércoles";
                case 4: return "jueves";
                case 5: return "viernes";
                case 6: return "sábado";
                case 7: return "domingo";
                default: return "día";
            }
        }
    }
}