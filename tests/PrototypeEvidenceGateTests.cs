using OnlineOs.AiOrchestrator.Pipeline;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class PrototypeEvidenceGateTests
{
    [Fact]
    public void CompletePairAndManifestPass()
    {
        using var fixture = new Fixture();
        fixture.WriteEvidence("O01");
        var result = PrototypeEvidenceGate.Check(fixture.Root, "M4", ["O01"]);
        Assert.True(result.Passed);
    }

    [Fact] public void MissingPrototypeFails() => AssertCode(false, "PROTOTYPE_EVIDENCE_MISSING", false, true, false);
    [Fact] public void MissingFlutterFails() => AssertCode(false, "FLUTTER_EVIDENCE_MISSING", true, false, false);
    [Fact] public void MissingManifestMappingFails() => AssertCode(false, "PROTOTYPE_MANIFEST_MAPPING_MISSING", true, true, false);
    [Fact] public void OneMissingStateFailsWholeSet() {
        using var f = new Fixture(); f.WriteEvidence("O01"); f.WriteEvidence("O07", manifest: false);
        var r = PrototypeEvidenceGate.Check(f.Root, "M4", ["O01", "O07"]);
        Assert.False(r.Passed); Assert.Contains("PROTOTYPE_MANIFEST_MAPPING_MISSING:O07", r.Missing);
    }
    [Fact] public void NonVisualTaskDoesNotNeedGate() => Assert.Empty(PrototypeEvidenceGate.Check(Path.GetTempPath(), "M4", []).Missing);

    private static void AssertCode(bool expected, string code, bool prototype, bool flutter, bool manifest)
    {
        using var f = new Fixture(); f.WriteEvidence("O01", prototype, flutter, manifest);
        var r = PrototypeEvidenceGate.Check(f.Root, "M4", ["O01"]);
        Assert.Equal(expected, r.Passed);
        Assert.Contains(r.Missing.Concat([r.Code]), item => item.StartsWith(code, StringComparison.Ordinal));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "evidence-gate-" + Guid.NewGuid().ToString("N"));
        public void WriteEvidence(string state, bool prototype = true, bool flutter = true, bool manifest = true)
        {
            var root = Path.Combine(Root, "docs", "qa", "m4", "visual-evidence");
            Directory.CreateDirectory(Path.Combine(root, "prototype")); Directory.CreateDirectory(Path.Combine(root, "flutter"));
            var slug = state.ToLowerInvariant();
            if (prototype) File.WriteAllBytes(Path.Combine(root, "prototype", "prototype-" + slug + ".png"), [1]);
            if (flutter) File.WriteAllBytes(Path.Combine(root, "flutter", "flutter-" + slug + ".png"), [1]);
            if (manifest) File.AppendAllText(Path.Combine(root, "MANIFEST.md"), $"{state} prototype-{slug}.png flutter-{slug}.png\n");
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
