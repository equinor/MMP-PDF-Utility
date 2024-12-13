using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SelectPdf;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MMP_Digiteam_Azure_functions
{
    public class SplitPDFBlobTriggered
    {
        private readonly ILogger<SplitPDFBlobTriggered> _logger;

        public SplitPDFBlobTriggered(ILogger<SplitPDFBlobTriggered> logger)
        {
            _logger = logger;
        }

        [Function(nameof(SplitPDFBlobTriggered))]
        public async Task Run([BlobTrigger("samples-workitems/{name}", Connection = "")] Stream stream, string name)
        {
            try
            {
                PdfDocument doc1 = new PdfDocument(stream);

                for (int i = 0; i < doc1.Pages.Count; i++)
                {
                    var singlePageDoc = new PdfDocument();
                    singlePageDoc.AddPage(doc1.Pages[i]);

                    string newFileName = $"{Path.GetFileNameWithoutExtension(name)}_Page_{i + 1}.pdf";

                    // Save to storage
                    string storageConnectionStringDestination = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
                    string storageConnectionStringSource = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
                    var blobServiceClient = new BlobServiceClient(storageConnectionStringSource);
                    var destinationBlobContainerClient = blobServiceClient.GetBlobContainerClient(storageConnectionStringDestination);
                    await destinationBlobContainerClient.CreateIfNotExistsAsync();

                    var destinationBlobClient = destinationBlobContainerClient.GetBlobClient(newFileName);
                    var memoryStream = new MemoryStream();
                    singlePageDoc.Save(memoryStream);
                    memoryStream.Position = 0;

                    await destinationBlobClient.UploadAsync(memoryStream, overwrite: true);

                    singlePageDoc.Close();
                }

                doc1.Close();

                _logger.LogInformation("PDF splitting completed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error occurred: {ex.Message}");
            }
        }
    }
}
