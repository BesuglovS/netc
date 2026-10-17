using NetcServer.Watchdog;
using Xunit;

namespace NetcTests;

public class QuarantineFileTests
{
    private static string NewRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "netc_q_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public void MovesScriptInsideRoot()
    {
        string root = NewRoot();
        string file = Path.Combine(root, "evil.ps1");
        File.WriteAllText(file, "shutdown /h");
        string quarantine = Path.Combine(root, "quarantine");

        var res = MachinePersonalizationWorker.QuarantineFileCore(file, root, quarantine);

        Assert.True(res.Ok);
        Assert.False(File.Exists(file));
        Assert.Single(Directory.GetFiles(quarantine));
    }

    [Fact]
    public void RejectsPathOutsideRoot()
    {
        string root = NewRoot();
        string outside = Path.Combine(Path.GetTempPath(), "netc_evil_" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(outside, "x");

        var res = MachinePersonalizationWorker.QuarantineFileCore(outside, root, Path.Combine(root, "q"));

        Assert.False(res.Ok);
        Assert.True(File.Exists(outside));
        File.Delete(outside);
    }

    [Fact]
    public void RejectsTraversal()
    {
        string root = NewRoot();
        string traversal = Path.Combine(root, "..", "escape.ps1");
        var res = MachinePersonalizationWorker.QuarantineFileCore(traversal, root, Path.Combine(root, "q"));
        Assert.False(res.Ok);
    }

    [Fact]
    public void RejectsNonScriptExtension()
    {
        string root = NewRoot();
        string file = Path.Combine(root, "note.txt");
        File.WriteAllText(file, "x");
        var res = MachinePersonalizationWorker.QuarantineFileCore(file, root, Path.Combine(root, "q"));
        Assert.False(res.Ok);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void RejectsMissingFile()
    {
        string root = NewRoot();
        var res = MachinePersonalizationWorker.QuarantineFileCore(Path.Combine(root, "none.ps1"), root, Path.Combine(root, "q"));
        Assert.False(res.Ok);
    }
}
