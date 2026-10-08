using Aiko.Application.Contracts;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// A data directory with no model in it: enough to ask the provider for one and check what it says.
/// </summary>
internal sealed class WorkspaceFixture : IDisposable
{
    private WorkspaceFixture(string directory, AikoDataPaths paths, EmbeddingModel model)
    {
        Directory = directory;
        Paths = paths;
        Model = model;
    }

    public string Directory { get; }

    public AikoDataPaths Paths { get; }

    /// <summary>
    /// The registered model, described the way the catalog describes one: a graph and a tokenizer, each with
    /// its own checksum. Their values do not matter here - nothing is downloaded - but the names do, because
    /// the provider finds the files by the names the registry gives them.
    /// </summary>
    public EmbeddingModel Model { get; }

    public static WorkspaceFixture Create()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"aiko-workspace-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(directory);

        var model = new EmbeddingModel(
            "test-model",
            "apache-2.0",
            "models-v1",
            [
                new EmbeddingModelFile(
                    "model_quantized.onnx",
                    118_308_126,
                    "0000000000000000000000000000000000000000000000000000000000000000",
                    "https://example.invalid/model_quantized.onnx"),
                new EmbeddingModelFile(
                    "tokenizer.json",
                    17_082_913,
                    "0000000000000000000000000000000000000000000000000000000000000000",
                    "https://example.invalid/tokenizer.json")
            ]);

        return new WorkspaceFixture(directory, new AikoDataPaths(Path.Combine(directory, "aiko.db")), model);
    }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not a failing spec.
        }
    }
}
