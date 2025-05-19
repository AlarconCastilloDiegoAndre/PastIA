// SaveMedicationHistory.cs
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
    public class SaveMedicationHistory
    {
        private readonly ILogger _logger;

        public SaveMedicationHistory(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<SaveMedicationHistory>();
        }

        [Function("SaveMedicationHistory")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
        {
            _logger.LogInformation("SaveMedicationHistory function processed a request.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var historyEntry = JsonSerializer.Deserialize<MedicationHistoryModel>(requestBody, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (historyEntry == null || string.IsNullOrEmpty(historyEntry.MedicationId) || string.IsNullOrEmpty(historyEntry.UserId))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Se requiere ID de medicamento e ID de usuario.");
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

                // Generar un ID si no se proporciona
                Guid historyId;
                if (string.IsNullOrEmpty(historyEntry.Id) || !Guid.TryParse(historyEntry.Id, out historyId))
                {
                    historyId = Guid.NewGuid();
                    historyEntry.Id = historyId.ToString();
                }

                await using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Comenzar una transacción
                    SqlTransaction transaction = connection.BeginTransaction();
                    
                    try
                    {
                        // 1. Insertar en MedicationHistory
                        string historyQuery = @"
                            INSERT INTO dbo.MedicationHistory (
                                HistoryId, MedicationId, UserId, ScheduledTime, 
                                ActualTakenTime, WasTaken, ReasonNotTaken, 
                                TimeTakenAfterReminder, CreatedAt
                            ) VALUES (
                                @HistoryId, @MedicationId, @UserId, @ScheduledTime, 
                                @ActualTakenTime, @WasTaken, @ReasonNotTaken, 
                                @TimeTakenAfterReminder, GETUTCDATE()
                            );";
                        
                        await using (SqlCommand command = new SqlCommand(historyQuery, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@HistoryId", historyId);
                            command.Parameters.AddWithValue("@MedicationId", Guid.Parse(historyEntry.MedicationId));
                            command.Parameters.AddWithValue("@UserId", historyEntry.UserId);
                            command.Parameters.AddWithValue("@ScheduledTime", DateTime.Parse(historyEntry.ScheduledDateTime));
                            
                            if (string.IsNullOrEmpty(historyEntry.ActualTakenTime))
                                command.Parameters.AddWithValue("@ActualTakenTime", DBNull.Value);
                            else
                                command.Parameters.AddWithValue("@ActualTakenTime", DateTime.Parse(historyEntry.ActualTakenTime));
                            
                            command.Parameters.AddWithValue("@WasTaken", historyEntry.WasTaken);
                            
                            if (string.IsNullOrEmpty(historyEntry.ReasonNotTaken))
                                command.Parameters.AddWithValue("@ReasonNotTaken", DBNull.Value);
                            else
                                command.Parameters.AddWithValue("@ReasonNotTaken", historyEntry.ReasonNotTaken);
                            
                            if (historyEntry.DeviationMinutes.HasValue)
                                command.Parameters.AddWithValue("@TimeTakenAfterReminder", historyEntry.DeviationMinutes.Value);
                            else
                                command.Parameters.AddWithValue("@TimeTakenAfterReminder", DBNull.Value);

                            await command.ExecuteNonQueryAsync();
                        }
                        
                        // 2. Insertar en MedicationContext si hay datos de contexto
                        if (historyEntry.Context != null)
                        {
                            string contextQuery = @"
                                INSERT INTO dbo.MedicationContext (
                                    ContextId, HistoryId, Location, Activity, Mood, 
                                    AdherenceStreak, TimeDeviation, DayOfWeek, 
                                    IsWeekend, IsHoliday, WasTravelDay, CreatedAt
                                ) VALUES (
                                    NEWID(), @HistoryId, @Location, @Activity, @Mood, 
                                    @AdherenceStreak, @TimeDeviation, @DayOfWeek, 
                                    @IsWeekend, @IsHoliday, @WasTravelDay, GETUTCDATE()
                                );";
                            
                            await using (SqlCommand command = new SqlCommand(contextQuery, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@HistoryId", historyId);
                                
                                if (string.IsNullOrEmpty(historyEntry.Context.Location))
                                    command.Parameters.AddWithValue("@Location", DBNull.Value);
                                else
                                    command.Parameters.AddWithValue("@Location", historyEntry.Context.Location);
                                
                                if (string.IsNullOrEmpty(historyEntry.Context.Activity))
                                    command.Parameters.AddWithValue("@Activity", DBNull.Value);
                                else
                                    command.Parameters.AddWithValue("@Activity", historyEntry.Context.Activity);
                                
                                if (string.IsNullOrEmpty(historyEntry.Context.Mood))
                                    command.Parameters.AddWithValue("@Mood", DBNull.Value);
                                else
                                    command.Parameters.AddWithValue("@Mood", historyEntry.Context.Mood);
                                
                                command.Parameters.AddWithValue("@AdherenceStreak", historyEntry.Context.AdherenceStreak);
                                
                                if (historyEntry.Context.TimeDeviation.HasValue)
                                    command.Parameters.AddWithValue("@TimeDeviation", historyEntry.Context.TimeDeviation.Value);
                                else
                                    command.Parameters.AddWithValue("@TimeDeviation", DBNull.Value);
                                
                                command.Parameters.AddWithValue("@DayOfWeek", historyEntry.Context.DayOfWeek);
                                command.Parameters.AddWithValue("@IsWeekend", historyEntry.Context.IsWeekend);
                                command.Parameters.AddWithValue("@IsHoliday", historyEntry.Context.IsHoliday);
                                command.Parameters.AddWithValue("@WasTravelDay", historyEntry.Context.WasTravelDay);

                                await command.ExecuteNonQueryAsync();
                            }
                        }
                        
                        // 3. Actualizar estadísticas del medicamento (optional)
                        string updateMedicationQuery = @"
                            UPDATE dbo.Medications 
                            SET LastTakenAt = CASE WHEN @WasTaken = 1 THEN GETUTCDATE() ELSE LastTakenAt END,
                                UpdatedAt = GETUTCDATE()
                            WHERE MedicationId = @MedicationId AND UserId = @UserId;";
                        
                        await using (SqlCommand command = new SqlCommand(updateMedicationQuery, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@MedicationId", Guid.Parse(historyEntry.MedicationId));
                            command.Parameters.AddWithValue("@UserId", historyEntry.UserId);
                            command.Parameters.AddWithValue("@WasTaken", historyEntry.WasTaken);

                            await command.ExecuteNonQueryAsync();
                        }
                        
                        // 4. Confirmar transacción
                        transaction.Commit();
                    }
                    catch (Exception)
                    {
                        // Revertir transacción en caso de error
                        transaction.Rollback();
                        throw;
                    }
                }
                
                // Respuesta exitosa
                await response.WriteAsJsonAsync(new { 
                    success = true, 
                    message = "Registro de historial guardado correctamente",
                    historyId = historyEntry.Id
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al guardar historial de medicamento: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error: {ex.Message}");
                return errorResponse;
            }
            
            return response;
        }
    }
}