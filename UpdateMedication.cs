// UpdateMedication.cs actualizado
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
    public class UpdateMedication
    {
        private readonly ILogger _logger;

        public UpdateMedication(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<UpdateMedication>();
        }

        [Function("UpdateMedication")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "put")] HttpRequestData req)
        {
            _logger.LogInformation("UpdateMedication function processed a request.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var medication = JsonSerializer.Deserialize<MedicationModel>(requestBody, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (medication == null || string.IsNullOrEmpty(medication.Id) || string.IsNullOrEmpty(medication.UserId))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Se requiere ID de medicamento y ID de usuario.");
                return badRequestResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            
            try
            {
                string? connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
                
                if (string.IsNullOrEmpty(connectionString))
                {
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    await errorResponse.WriteStringAsync("Error: No se encontró la cadena de conexión.");
                    return errorResponse;
                }

                await using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    string query = @"
                        UPDATE dbo.Medications 
                        SET Name = @Name, 
                            Dosage = @Dosage, 
                            TimeHour = @TimeHour,
                            TimeMinute = @TimeMinute,
                            DaysOfWeek = @DaysOfWeek,
                            Instructions = @Instructions,
                            Importance = @Importance,
                            SideEffects = @SideEffects,
                            Category = @Category,
                            TreatmentDuration = @TreatmentDuration,
                            ReminderStrategy = @ReminderStrategy,
                            UpdatedAt = GETUTCDATE()
                        WHERE MedicationId = @MedicationId 
                        AND UserId = @UserId";
                    
                    await using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@MedicationId", Guid.Parse(medication.Id));
                        command.Parameters.AddWithValue("@Name", medication.Name);
                        command.Parameters.AddWithValue("@Dosage", medication.Dosage);
                        command.Parameters.AddWithValue("@TimeHour", medication.TimeHour);
                        command.Parameters.AddWithValue("@TimeMinute", medication.TimeMinute);
                        command.Parameters.AddWithValue("@DaysOfWeek", medication.DaysOfWeek);
                        command.Parameters.AddWithValue("@UserId", medication.UserId);
                        
                        if (string.IsNullOrEmpty(medication.Instructions))
                            command.Parameters.AddWithValue("@Instructions", DBNull.Value);
                        else
                            command.Parameters.AddWithValue("@Instructions", medication.Instructions);
                        
                        command.Parameters.AddWithValue("@Importance", medication.Importance);
                        
                        if (string.IsNullOrEmpty(medication.SideEffects))
                            command.Parameters.AddWithValue("@SideEffects", DBNull.Value);
                        else
                            command.Parameters.AddWithValue("@SideEffects", medication.SideEffects);
                        
                        if (string.IsNullOrEmpty(medication.Category))
                            command.Parameters.AddWithValue("@Category", DBNull.Value);
                        else
                            command.Parameters.AddWithValue("@Category", medication.Category);
                        
                        if (medication.TreatmentDuration.HasValue)
                            command.Parameters.AddWithValue("@TreatmentDuration", medication.TreatmentDuration.Value);
                        else
                            command.Parameters.AddWithValue("@TreatmentDuration", DBNull.Value);
                        
                        command.Parameters.AddWithValue("@ReminderStrategy", medication.ReminderStrategy);
                        
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected == 0)
                        {
                            var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                            await notFoundResponse.WriteStringAsync("Medicamento no encontrado");
                            return notFoundResponse;
                        }
                    }
                }
                
                await response.WriteAsJsonAsync(new { success = true, message = "Medicamento actualizado correctamente" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al actualizar medicamento: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error: {ex.Message}");
                return errorResponse;
            }
            
            return response;
        }
    }
}