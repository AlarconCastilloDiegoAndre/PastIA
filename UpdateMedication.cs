using System;
using Microsoft.Data.SqlClient; // Cambiado de System.Data.SqlClient
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
            _logger.LogInformation("C# HTTP trigger function processed a request to update medication.");

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
                    
                    string query = @"UPDATE Medications 
                                   SET Name = @Name, 
                                       Dosage = @Dosage, 
                                       Frequency = @Frequency
                                   WHERE Id = @Id AND UserId = @UserId";
                    
                    await using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@Id", medication.Id);
                        command.Parameters.AddWithValue("@Name", medication.Name);
                        command.Parameters.AddWithValue("@Dosage", medication.Dosage);
                        command.Parameters.AddWithValue("@Frequency", medication.Frequency);
                        command.Parameters.AddWithValue("@UserId", medication.UserId);
                        
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected == 0)
                        {
                            var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                            await notFoundResponse.WriteStringAsync("Medicamento no encontrado");
                            return notFoundResponse;
                        }
                    }
                }
                
                await response.WriteStringAsync("Medicamento actualizado correctamente");
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