using System.Net;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Api
{
    public class Ticket : ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public bool isdrawn { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }

    public class ApiFunction
    {
        private readonly ILogger _logger;
        private readonly string _connectionString;
        private readonly string _magicword;

        public ApiFunction(ILoggerFactory loggerFactory, IConfiguration configuration)
        {
            _logger = loggerFactory.CreateLogger<ApiFunction>();
            _connectionString = configuration["connectionString"]!;
            _magicword = configuration["magicword"]!;
        }

        private TableClient GetTableClient(string table)
        {
            return new TableClient(_connectionString, table);
        }

        [Function("resetdeelnemers")]
        public async Task<HttpResponseData> ResetDeelnemers([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            if (req.Query["magicword"] != _magicword)
            {
                var gifBytes = File.ReadAllBytes("you-didnt-say-the-magic-word-ah-ah.gif");
                response.Headers.Add("Content-Type", "image/gif");
                response.Body.Write(gifBytes, 0, gifBytes.Length);
                return response;
            }

            _logger.LogInformation("HTTP trigger function processed a request.");

            var tableClient = GetTableClient("participants");
            var entities = tableClient.QueryAsync<TableEntity>();

            await foreach (var entity in entities)
            {
                await tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey);
            }

            await response.Body.WriteAsync(Encoding.UTF8.GetBytes("Deelnemers zijn gereset"));
            return response;
        }

        [Function("draw")]
        public async Task<string> Ticket([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            _logger.LogInformation("HTTP trigger function processed a request.");

            var ticketTableClient = GetTableClient("goldenticket");

            var partitionKey = "ticket";
            var rowKey = "drawn";
            var goldenTicket = await ticketTableClient.GetEntityAsync<Ticket>(partitionKey, rowKey);

            if (goldenTicket.Value.isdrawn)
            {
                return "";
            }

            var deelnemerTableClient = GetTableClient("participants");

            var deelnemers = await GetDeelnemers(deelnemerTableClient);

            var random = new Random();
            var winner = deelnemers[random.Next(deelnemers.Count)];

            goldenTicket.Value.isdrawn = true;

            await ticketTableClient.UpdateEntityAsync(goldenTicket.Value, goldenTicket.Value.ETag);

            return winner;
        }

        [Function("deelnemers")]
        public async Task<string> Deelnemer([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("HTTP trigger function processed a request.");

                var tableClient = GetTableClient("participants");

                if (req.Method == "GET")
                {
                    List<string> deelnemers = await GetDeelnemers(tableClient);

                    return JsonSerializer.Serialize(deelnemers);
                }

                if (req.Method == "POST")
                {
                    var deelnemer = new TableEntity
                    {
                        PartitionKey = "deelnemer",
                        RowKey = await new StreamReader(req.Body).ReadToEndAsync()
                    };

                    await tableClient.AddEntityAsync(deelnemer);
                    return HttpStatusCode.OK.ToString();
                }
            }
            catch (RequestFailedException ex)
            {
                if (ex.ErrorCode == "EntityAlreadyExists")
                {
                    return HttpStatusCode.Conflict.ToString();
                }
            }
            catch (Exception ex)
            {
                return HttpStatusCode.InternalServerError.ToString();
            }

            return HttpStatusCode.Forbidden.ToString();
        }

        private static async Task<List<string>> GetDeelnemers(TableClient tableClient)
        {
            var results = new List<string>();
            await foreach (var deelnemer in tableClient.QueryAsync<TableEntity>())
            {
                results.Add(deelnemer.RowKey);
            }

            return results;
        }
    }
}