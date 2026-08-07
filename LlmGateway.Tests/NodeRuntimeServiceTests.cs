using LlmGateway.Desktop.Services;
using Xunit;

namespace LlmGateway.Tests;

public sealed class NodeRuntimeServiceTests
{
    [Fact]
    public void GetManagedNodePath_UsesWindowsX64RuntimeLayout()
    {
        var paths = new AppPaths("C:\\Users\\tester", "C:\\LlmGateway");

        var managedNodePath = new NodeRuntimeService(paths).GetManagedNodePath();

        Assert.EndsWith(Path.Combine("runtime", "win-x64", "node.exe"), managedNodePath);
    }
}