// DeleteMedication.cs actualizado
using System;
using Microsoft.Data.SqlClient;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace PastIA.Function
{
    public class DeleteMedication
    {
        private readonly ILogger _logger;

        public DeleteMedication(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<DeleteMedication>();
        }

        [Function("DeleteMedication")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "delete")] HttpRequestData req)
        {
            _logger.LogInformation("DeleteMedication function processed a request.");

            string id = req.Query["id"] ?? string.Empty;
            string userId = req.Query["userId"] ?? string.Empty;
            
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(userId))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Por favor proporciona el ID del medicamento y el ID del usuario");
                return badRequestResponse;
            }
            
            // Validar que el ID del medicamento sea un GUID válido
            if (!Guid.TryParse(id, out Guid medicationId))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("El ID del medicamento no es válido");
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
                    
                    // En lugar de eliminar el registro, actualizamos IsActive a 0 (soft delete)
                    string query = @"
                        UPDATE dbo.Medications 
                        SET IsActive = 0, UpdatedAt = GETUTCDATE() 
                        WHERE MedicationId = @MedicationId AND UserId = @UserId";
                    
                    await using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@MedicationId", medicationId);
                        command.Parameters.AddWithValue("@UserId", userId);
                        
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected == 0)
                        {
                            var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                            await notFoundResponse.WriteStringAsync("Medicamento no encontrado");
                            return notFoundResponse;
                        }
                    }
                }
                
                await response.WriteAsJsonAsync(new { success = true, message = "Medicamento eliminado correctamente" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al eliminar medicamento: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error: {ex.Message}");
                return errorResponse;
            }
            
            return response;
        }
    }
}