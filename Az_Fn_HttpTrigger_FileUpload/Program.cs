using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureAppConfiguration(config =>
    {
        config.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true);
    })
    .ConfigureServices((context,services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        services.AddSingleton(x => new BlobServiceClient(context.Configuration["Values:AzureWebJobsStorage"]));
        services.AddSingleton(x=> new DocumentAnalysisClient(new Uri(context.Configuration["Values:AzureAIServiceEndpoint"]),
                    new AzureKeyCredential(context.Configuration["Values:AzureAIServiceKey"])));
    })
    .Build();

host.Run();
