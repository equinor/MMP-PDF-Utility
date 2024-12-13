using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SelectPdf;
 
namespace MMP_Digiteam_Azure_functions
{
    public class SplitPdfs
    {
        private readonly ILogger<SplitPdfs> _logger;

        public SplitPdfs(ILogger<SplitPdfs> logger)
        {
            _logger = logger;
        }

        [Function("SplitPdfs")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
        {
            _logger.LogInformation("C# HTTP trigger function to split PDFs started.");

            // Deserialize the request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            SplitPdfRequest data;

            try
            {
                data = JsonSerializer.Deserialize<SplitPdfRequest>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error deserializing request body: {ex.Message}");
                return new BadRequestObjectResult("Invalid JSON in request body.");
            }

            // Validate input
            if (string.IsNullOrEmpty(data.SourceFilePath) &&
                (string.IsNullOrEmpty(data.SourceContainer) || string.IsNullOrEmpty(data.FileName)))
            {
                return new BadRequestObjectResult("Provide either local file paths (sourceFilePath and destinationDirectoryPath) or storage details (sourceContainer, destinationContainer, and fileName).");
            }

            try
            {
                MemoryStream pdfStream;

                if (!string.IsNullOrEmpty(data.SourceFilePath))
                {
                    // Local File Mode
                    if (!File.Exists(data.SourceFilePath))
                    {
                        return new NotFoundObjectResult($"Source file not found: {data.SourceFilePath}");
                    }

                    if (!Directory.Exists(data.DestinationDirectoryPath))
                    {
                        Directory.CreateDirectory(data.DestinationDirectoryPath);
                    }

                    pdfStream = new MemoryStream(await File.ReadAllBytesAsync(data.SourceFilePath));
                }
                else
                {
                    // Storage Mode
                    string storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
                    var blobServiceClient = new BlobServiceClient(storageConnectionString);
                    var sourceBlobContainerClient = blobServiceClient.GetBlobContainerClient(data.SourceContainer);
                    var blobClient = sourceBlobContainerClient.GetBlobClient(data.FileName);

                    if (!await blobClient.ExistsAsync())
                    {
                        return new NotFoundObjectResult($"Blob {data.FileName} not found in container {data.SourceContainer}.");
                    }

                    var downloadResponse = await blobClient.DownloadContentAsync();
                    pdfStream = new MemoryStream(downloadResponse.Value.Content.ToArray());
                }

                PdfDocument doc1 = new SelectPdf.PdfDocument(pdfStream);

                for (int i = 0; i < doc1.Pages.Count; i++)
                {
                    var singlePageDoc = new PdfDocument();
                    singlePageDoc.AddPage(doc1.Pages[i]);

                    string newFileName = $"{Path.GetFileNameWithoutExtension(data.FileName ?? data.SourceFilePath)}_Page_{i + 1}.pdf";

                    if (!string.IsNullOrEmpty(data.SourceFilePath))
                    {
                        // Save locally
                        string localPath = Path.Combine(data.DestinationDirectoryPath, newFileName);
                        singlePageDoc.Save(localPath);
                    }
                    else
                    {
                        // Save to storage
                        string storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
                        var blobServiceClient = new BlobServiceClient(storageConnectionString);
                        var destinationBlobContainerClient = blobServiceClient.GetBlobContainerClient(data.DestinationContainer);
                        await destinationBlobContainerClient.CreateIfNotExistsAsync();

                        var destinationBlobClient = destinationBlobContainerClient.GetBlobClient(newFileName);
                        var memoryStream = new MemoryStream();
                        singlePageDoc.Save(memoryStream);
                        memoryStream.Position = 0;

                        await destinationBlobClient.UploadAsync(memoryStream, overwrite: true);
                    }

                    singlePageDoc.Close();
                }

                doc1.Close();

                _logger.LogInformation("PDF splitting completed successfully.");
                return new OkObjectResult("PDF splitting completed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error occurred: {ex.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }

    public class SplitPdfRequest
    {
        public string? SourceFilePath { get; set; }
        public string? DestinationDirectoryPath { get; set; }
        public string? SourceContainer { get; set; }
        public string? DestinationContainer { get; set; }
        public string? FileName { get; set; }
    }
}
