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
            _logger.LogInformation("C# HTTP trigger function processed a request to add medication.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var medication = JsonSerializer.Deserialize<MedicationModel>(requestBody, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (medication == null)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("El cuerpo de la solicitud no es válido.");
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
                    
                    string query = @"INSERT INTO Medications (Id, Name, Dosage, Frequency, UserId) 
                                   VALUES (@Id, @Name, @Dosage, @Frequency, @UserId);";
                    
                    await using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        // Generar un ID si no se proporciona
                        string id = string.IsNullOrEmpty(medication.Id) ? Guid.NewGuid().ToString() : medication.Id;
                        
                        command.Parameters.AddWithValue("@Id", id);
                        command.Parameters.AddWithValue("@Name", medication.Name);
                        command.Parameters.AddWithValue("@Dosage", medication.Dosage);
                        command.Parameters.AddWithValue("@Frequency", medication.Frequency);
                        command.Parameters.AddWithValue("@UserId", medication.UserId);
                        
                        await command.ExecuteNonQueryAsync();
                    }
                }
                
                await response.WriteStringAsync("Medicamento agregado correctamente");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al agregar medicamento: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Error: {ex.Message}");
                return errorResponse;
            }
            
            return response;
        }
    }
}