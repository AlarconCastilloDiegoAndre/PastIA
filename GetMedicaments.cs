using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient; // Cambiado de System.Data.SqlClient
using System.Net;
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
            [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            // Obtener el userId de la consulta con operador de null-coalescing
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
                
                string query = "SELECT * FROM Medications WHERE UserId = @UserId";
                await using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", userId);
                    
                    await using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var medication = new MedicationModel
                            {
                                Id = SafeGetString(reader, "Id"),
                                Name = SafeGetString(reader, "Name"),
                                Dosage = SafeGetString(reader, "Dosage"),
                                Frequency = SafeGetString(reader, "Frequency"),
                                UserId = SafeGetString(reader, "UserId")
                                // Agrega otras propiedades según tu esquema
                            };
                            
                            medications.Add(medication);
                        }
                    }
                }
            }
            
            return medications;
        }

        private string SafeGetString(SqlDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }
    }

    public class MedicationModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Dosage { get; set; } = string.Empty;
        public string Frequency { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        // Agrega otras propiedades según tu esquema
    }
}