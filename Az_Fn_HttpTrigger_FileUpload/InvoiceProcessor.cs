using System.IO;
using System.Threading.Tasks;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs;

namespace Az_Fn_HttpTrigger_FileUpload
{
    public class InvoiceProcessor
    {
        private readonly ILogger<InvoiceProcessor> _logger;
        private readonly string _blobConnectionString;
        private readonly string _sqlbConnectionString;
        private readonly DocumentAnalysisClient _documentAnalysisClient;
        private readonly BlobServiceClient _blobServiceClient;
        public InvoiceProcessor(ILogger<InvoiceProcessor> logger, IConfiguration configuration, DocumentAnalysisClient documentAnalysisClient, BlobServiceClient blobServiceClient)
        {
            _logger = logger;
            _blobConnectionString = configuration["Values:AzureWebJobsStorage"] ?? throw new ArgumentNullException("AzureWebJobsStorage configuration is missing.");
            _documentAnalysisClient = documentAnalysisClient;
            _sqlbConnectionString = configuration["Values:DatabaseConnectionString"] ?? throw new ArgumentNullException("AzureSqlConnectionString configuration is missing.");
            _blobServiceClient = blobServiceClient;
        }

        [Function(nameof(InvoiceProcessor))]
        public async Task Run([BlobTrigger("invoices/{name}", Connection = "AzureWebJobsStorage")] Stream stream, string name)
        {
            using var blobStreamReader = new StreamReader(stream);
            var content = await blobStreamReader.ReadToEndAsync();

            await ProcessInvoiceAsync(stream, name);
            _logger.LogInformation($"C# Blob trigger function Processed blob\n Name: {name} \n Data: {content}");
        }



        /// <summary>
        /// 
        /// </summary>
        /// <param name="blobStream"></param>
        /// <param name="fileName"></param>
        /// <returns></returns>
        private  async Task ProcessInvoiceAsync(Stream blobStream, string name)
        {
            try
            {
                // Reset the stream position to the beginning
                blobStream.Position = 0;

                AnalyzeDocumentOperation operation = await _documentAnalysisClient.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-invoice", blobStream);
                AnalyzeResult result = operation.Value;

                foreach (var document in result.Documents)
                {
                    string invoiceId = document.Fields["InvoiceId"].Value.AsString();
                    string vendorName = document.Fields["VendorName"].Value.AsString();
                    string customerName = document.Fields["CustomerName"].Value.AsString();
                    DateTime invoiceDate = document.Fields["InvoiceDate"].Value.AsDate().DateTime;
                    double totalAmount = document.Fields["InvoiceTotal"].Value.AsCurrency().Amount;

                   var res = SaveToDB(invoiceId, vendorName, customerName, invoiceDate, totalAmount);
                    if (res)
                    {
                        _logger.LogInformation("Invoice saved to database successfully.");
                        // Save the Blob in the processed container
                       await SaveBlobToProcessedContainer(blobStream, name);
                    }
                    else
                    {
                        _logger.LogError("Failed to save invoice to database.");
                    }
                }
            }
            catch (Exception ex)
            {

                throw ex;
            }
        }
        /// <summary>
        /// Save Invoice to Database
        /// </summary>
        /// <param name="invoiceId"></param>
        /// <param name="vendorName"></param>
        /// <param name="customerName"></param>
        /// <param name="invoiceDate"></param>
        /// <param name="totalAmount"></param>
        /// <returns></returns>

        private  bool SaveToDB(string invoiceId, string vendorName, string customerName, DateTime invoiceDate, double totalAmount)
        {
            bool isSuccessful = false;
            try
            {
                using var connection = new SqlConnection(_sqlbConnectionString);
                string query = "INSERT INTO Invoices (InvoiceId, VendorName, CustomerName, InvoiceDate, TotalAmount) VALUES (@InvoiceId, @VendorName, @CustomerName, @InvoiceDate, @TotalAmount)";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@InvoiceId", invoiceId);
                command.Parameters.AddWithValue("@VendorName", vendorName);
                command.Parameters.AddWithValue("@CustomerName", customerName);
                command.Parameters.AddWithValue("@InvoiceDate", invoiceDate);
                command.Parameters.AddWithValue("@TotalAmount", totalAmount);

                connection.Open();
                command.ExecuteNonQuery();
                isSuccessful = true;
            }
            catch (Exception ex)
            {
                isSuccessful = false;
                throw ex;
            }
           return isSuccessful;
        }

        /// <summary>
        /// Save the Processed Invoice Blob to Processed Container
        /// </summary>
        /// <param name="blobStream"></param>
        /// <param name="blobName"></param>
        /// <returns></returns>

        private async Task SaveBlobToProcessedContainer(Stream blobStream, string blobName)
        {
            try
            {
                var sourceContainerClient = _blobServiceClient.GetBlobContainerClient("invoices");
                var destinationContainerClient = _blobServiceClient.GetBlobContainerClient("processed-invoices");

                await destinationContainerClient.CreateIfNotExistsAsync();

                var sourceBlobClient = sourceContainerClient.GetBlobClient(blobName);
                var destinationBlobClient = destinationContainerClient.GetBlobClient(blobName);

                await destinationBlobClient.StartCopyFromUriAsync(sourceBlobClient.Uri);
                _logger.LogInformation($"Blob {blobName} copied to processed container successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving blob to processed container: {ex.Message}");
                throw;
            }
        }
    }
}
