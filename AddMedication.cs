// AddMedication.cs actualizado
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
    public class AddMedication
    {
        private readonly ILogger _logger;

        public AddMedication(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<AddMedication>();
        }

        [Function("AddMedication")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
        {
            _logger.LogInformation("AddMedication function processed a request.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation($"Request body: {requestBody}");
            
            var medication = JsonSerializer.Deserialize<MedicationModel>(requestBody, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (medication == null)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("El cuerpo de la solicitud no es válido.");
                return badRequestResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            
            try
            {
                string connectionString = ConnectionHelper.GetConnectionString();
                _logger.LogInformation("Connection string obtenida exitosamente");

                // Generar un ID si no se proporciona
                Guid medicationId;
                if (string.IsNullOrEmpty(medication.Id) || !Guid.TryParse(medication.Id, out medicationId))
                {
                    medicationId = Guid.NewGuid();
                }

                await using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    _logger.LogInformation("Conexión a base de datos abierta exitosamente");
                    
                    string query = @"
                        INSERT INTO dbo.Medications (
                            MedicationId, UserId, Name, Dosage, TimeHour, TimeMinute, 
                            DaysOfWeek, Instructions, IsActive, CreatedAt, 
                            Importance, SideEffects, Category, TreatmentDuration, ReminderStrategy
                        ) VALUES (
                            @MedicationId, @UserId, @Name, @Dosage, @TimeHour, @TimeMinute, 
                            @DaysOfWeek, @Instructions, 1, GETUTCDATE(),
                            @Importance, @SideEffects, @Category, @TreatmentDuration, @ReminderStrategy
                        );";
                    
                    await using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@MedicationId", medicationId);
                        command.Parameters.AddWithValue("@UserId", medication.UserId);
                        command.Parameters.AddWithValue("@Name", medication.Name);
                        command.Parameters.AddWithValue("@Dosage", medication.Dosage);
                        command.Parameters.AddWithValue("@TimeHour", medication.TimeHour);
                        command.Parameters.AddWithValue("@TimeMinute", medication.TimeMinute);
                        command.Parameters.AddWithValue("@DaysOfWeek", medication.DaysOfWeek);
                        
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

                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation($"Medicamento insertado exitosamente con ID: {medicationId}");
                    }
                }
                
                // Devolver el medicamento con su ID
                medication.Id = medicationId.ToString();
                await response.WriteAsJsonAsync(medication);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al agregar medicamento: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                var errorData = new
                {
                    error = ex.Message,
                    stackTrace = ex.StackTrace,
                    suggestions = new[]
                    {
                        "Verificar que la tabla dbo.Medications exista en la base de datos",
                        "Verificar los permisos del usuario de base de datos",
                        "Revisar la configuración de las variables de entorno"
                    }
                };
                await errorResponse.WriteAsJsonAsync(errorData);
                return errorResponse;
            }
            
            return response;
        }
    }
}