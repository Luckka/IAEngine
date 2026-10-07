using IAEngine.Core.Recovery;

namespace IAEngine.Core.Tests;

public sealed class RecoveryContractTests
{
    [Theory]
    [InlineData(RecoveryReason.ValidationFailure)]
    [InlineData(RecoveryReason.ProviderFailure)]
    [InlineData(RecoveryReason.Timeout)]
    [InlineData(RecoveryReason.Cancellation)]
    [InlineData(RecoveryReason.Crash)]
    [InlineData(RecoveryReason.Interrupted)]
    public async Task RecoveryReasonsPersistAsDistinctAttempts(RecoveryReason reason)
    {
        var workspace = Directory.CreateTempSubdirectory("iaengine-recovery-reason-");
        try
        {
            var key = new ExecutionKey("project-a", "milestone-1", "task-1", "execution-1");
            var service = new ExecutionRecoveryService(new FileRecoveryStore(workspace.FullName));
            await service.StartAsync(key);
            var attempt = await service.StartAttemptAsync(key, reason);
            var result = await service.RecordAttemptAsync(key, attempt with { Status = RecoveryStatus.Failed, EndedAt = DateTimeOffset.UtcNow });

            Assert.Single(result.Attempts);
            Assert.Equal(reason, result.Attempts[0].Reason);
            Assert.Equal(RecoveryStatus.Failed, result.Status);
        }
        finally { workspace.Delete(true); }
    }

    [Fact]
    public async Task ExecutionKeyIsStableAndRestartReloadsAttempts()
    {
        var workspace = Directory.CreateTempSubdirectory("iaengine-recovery-");
        try
        {
            var key = new ExecutionKey("project-a", "milestone-1", "task-1", "execution-1");
            var service = new ExecutionRecoveryService(new FileRecoveryStore(workspace.FullName));
            await service.StartAsync(key);
            var attempt = await service.StartAttemptAsync(key, RecoveryReason.Crash);
            await service.RecordAttemptAsync(key, attempt with { Status = RecoveryStatus.Failed, EndedAt = DateTimeOffset.UtcNow, SanitizedError = "process interrupted" });

            var restarted = new ExecutionRecoveryService(new FileRecoveryStore(workspace.FullName));
            var loaded = await restarted.GetAsync(key);

            Assert.NotNull(loaded);
            Assert.Equal(key.Value, loaded!.ExecutionKey);
            Assert.Single(loaded.Attempts);
            Assert.Equal(RecoveryReason.Crash, loaded.Attempts[0].Reason);
        }
        finally { workspace.Delete(true); }
    }

    [Fact]
    public async Task ArtifactsAndCheckpointsAreIdempotent()
    {
        var workspace = Directory.CreateTempSubdirectory("iaengine-recovery-");
        try
        {
            var key = new ExecutionKey("project-a", "milestone-1", "task-1", "execution-1");
            var service = new ExecutionRecoveryService(new FileRecoveryStore(workspace.FullName));
            await service.StartAsync(key);
            var artifact = new RecoveryArtifactIdentity("artifact-1", "finding.json", "hash");
            var checkpoint = new RecoveryCheckpointIdentity("checkpoint-1", "commit-1");

            await service.RecordArtifactAsync(key, artifact);
            await service.RecordArtifactAsync(key, artifact);
            await service.RecordCheckpointAsync(key, checkpoint);
            var result = await service.RecordCheckpointAsync(key, checkpoint);

            Assert.Single(result.Artifacts);
            Assert.Single(result.Checkpoints);
        }
        finally { workspace.Delete(true); }
    }

    [Fact]
    public async Task HumanRequiredNeedsMatchingExplicitApproval()
    {
        var workspace = Directory.CreateTempSubdirectory("iaengine-recovery-");
        try
        {
            var key = new ExecutionKey("project-a", "milestone-1", "task-1", "execution-1");
            var service = new ExecutionRecoveryService(new FileRecoveryStore(workspace.FullName));
            await service.StartAsync(key);
            var attempt = await service.StartAttemptAsync(key, RecoveryReason.HumanRequired);
            await service.RecordAttemptAsync(key, attempt with { Status = RecoveryStatus.HumanRequired, ApprovalRequired = true });

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecoverAsync(key, RecoveryReason.HumanRequired));
            var approved = await service.ApproveHumanRequiredAsync(key, "approval-1", "reviewed locally");
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveHumanRequiredAsync(key, "approval-1", "same approval"));

            Assert.Equal(RecoveryStatus.Approved, approved.Status);
            Assert.Single(approved.Approvals);
        }
        finally { workspace.Delete(true); }
    }

    [Fact]
    public async Task ProjectsAndMilestonesAreIsolated()
    {
        var workspace = Directory.CreateTempSubdirectory("iaengine-recovery-");
        try
        {
            var service = new ExecutionRecoveryService(new FileRecoveryStore(workspace.FullName));
            var first = new ExecutionKey("project-a", "milestone-1", "task-1", "execution-1");
            var second = new ExecutionKey("project-b", "milestone-1", "task-1", "execution-1");
            await service.StartAsync(first);

            Assert.Null(await service.GetAsync(second));
        }
        finally { workspace.Delete(true); }
    }

    [Fact]
    public async Task WrongExecutionKeyAndSensitiveErrorAreRejectedOrSanitized()
    {
        var workspace = Directory.CreateTempSubdirectory("iaengine-recovery-");
        try
        {
            var service = new ExecutionRecoveryService(new FileRecoveryStore(workspace.FullName));
            var key = new ExecutionKey("project-a", "milestone-1", "task-1", "execution-1");
            var wrongKey = new ExecutionKey("project-a", "milestone-1", "task-1", "execution-2");
            await service.StartAsync(key);
            var attempt = await service.StartAttemptAsync(key, RecoveryReason.ProviderFailure);
            var saved = await service.RecordAttemptAsync(key, attempt with { Status = RecoveryStatus.Failed, SanitizedError = "token=secret-value" });

            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ApproveHumanRequiredAsync(wrongKey, "approval", "wrong execution"));
            Assert.Equal(1, saved.SchemaVersion);
            Assert.DoesNotContain("secret-value", saved.Attempts[0].SanitizedError, StringComparison.Ordinal);
        }
        finally { workspace.Delete(true); }
    }
}
