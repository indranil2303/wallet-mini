using Microsoft.Extensions.ML;
using System.IO.Compression;
using wallet.fraud_detection.contracts;

namespace wallet.fraud_detection.extensions;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFraudDetectionConfiguration(this IServiceCollection services,
        KafkaConfig kafkaConfig,
        string mlModelPath)
    {
        var resolvedModelPath = ResolveMlModelPath(mlModelPath);

        // Register the ML.NET Prediction Engine
        services.AddPredictionEnginePool<FraudModelInput, FraudPrediction>()
            .FromFile(modelName: "FraudDetection", filePath: resolvedModelPath, watchForChanges: true);

        services.AddSingleton(kafkaConfig);
        
        return services;
    }

    private static string ResolveMlModelPath(string mlModelPath)
    {
        if (string.IsNullOrWhiteSpace(mlModelPath))
        {
            throw new ArgumentException("ML model path must be configured.", nameof(mlModelPath));
        }

        if (Directory.Exists(mlModelPath))
        {
            var modelFiles = Directory.GetFiles(mlModelPath, "*.mlnet", SearchOption.AllDirectories);
            if (modelFiles.Length == 0)
            {
                var archives = Directory.GetFiles(mlModelPath, "*.zip", SearchOption.AllDirectories);
                if (archives.Length == 1)
                {
                    return archives[0];
                }

                throw new InvalidOperationException($"No ML.NET model file was found under directory '{mlModelPath}'.");
            }

            if (modelFiles.Length > 1)
            {
                throw new InvalidOperationException($"Multiple .mlnet model files were found under '{mlModelPath}'. Please point MLModelPath to a single model file or archive.");
            }

            return modelFiles[0];
        }

        if (!File.Exists(mlModelPath))
        {
            throw new FileNotFoundException($"The configured ML model file was not found: '{mlModelPath}'.", mlModelPath);
        }

        var extension = Path.GetExtension(mlModelPath);
        if (string.Equals(extension, ".mlnet", StringComparison.OrdinalIgnoreCase))
        {
            return mlModelPath;
        }

        if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(mlModelPath);
            var hasModelRoot = archive.Entries.Any(e => e.FullName.StartsWith("DataLoaderModel/", StringComparison.OrdinalIgnoreCase)
                || e.FullName.StartsWith("TransformerChain/", StringComparison.OrdinalIgnoreCase));
            if (hasModelRoot)
            {
                return mlModelPath;
            }

            var innerModelEntry = archive.Entries.FirstOrDefault(e => string.Equals(Path.GetExtension(e.Name), ".mlnet", StringComparison.OrdinalIgnoreCase));
            if (innerModelEntry is not null)
            {
                var tempFolder = Path.Combine(Path.GetTempPath(), "wallet-fraud-detection-model");
                Directory.CreateDirectory(tempFolder);
                var extractedModelPath = Path.Combine(tempFolder, innerModelEntry.Name);
                innerModelEntry.ExtractToFile(extractedModelPath, overwrite: true);
                return extractedModelPath;
            }

            throw new InvalidOperationException($"Zip archive '{mlModelPath}' does not contain a valid ML.NET model. Expected a .mlnet file or a model archive with DataLoaderModel/TransformerChain.");
        }

        throw new InvalidOperationException($"Unsupported model file extension '{extension}'. Expected .zip or .mlnet.");
    }
}