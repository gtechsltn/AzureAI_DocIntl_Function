using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Az_Fn_HttpTrigger_FileUpload
{
    public class UploadBlob
    {
        private readonly ILogger<UploadBlob> _logger;
        private readonly BlobServiceClient _blobServiceClient;

        public UploadBlob(ILogger<UploadBlob> logger, BlobServiceClient blobServiceClient)
        {
            _logger = logger;
            _blobServiceClient = blobServiceClient;
        }

        [Function("upload")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            if (!req.HasFormContentType || !req.Form.Files.Any())
            {
                return new BadRequestObjectResult("No file uploaded.");
            }
            // 1. Read the File from the request    
            var file = req.Form.Files[0];
            if (!CheckFileExtension(file))
            {
                return new BadRequestObjectResult("Invalid file extension. Only PDF files are allowed.");
            }

            // 2. Get the Blob Container
            var containerClient = _blobServiceClient.GetBlobContainerClient("invoices");
            // 3. Create the Blob Container if it doesn't exist
            await containerClient.CreateIfNotExistsAsync();

            var blobClient = containerClient.GetBlobClient(file.FileName);

            using (var stream = file.OpenReadStream())
            {
                await blobClient.UploadAsync(stream, true);
            }

            return new OkObjectResult($"File {file.FileName} uploaded successfully.");
        }

        private bool CheckFileExtension(IFormFile file)
        {
            string[] extensions = new string[] { "pdf" };

            var fileNameExtension = file.FileName.Split(".")[1];
            if (string.IsNullOrEmpty(fileNameExtension) ||
                !extensions.Contains(fileNameExtension))
            {
                return false;
            }

            return true;
        }
    }
}
