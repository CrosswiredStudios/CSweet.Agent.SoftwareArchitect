using System.Text.Json;
using CSweet.Agent.SDK;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agents.SoftwareArchitect.Tests;

public sealed class ManifestTests
{
    [Fact]
    public async Task ManifestLoadsAndMatchesImplementation()
    {
        var root = RepositoryRoot();
        var path = Path.Combine(root, "csweet-plugin.json");
        var manifest = await AgentManifestLoader.LoadAsync(path, CancellationToken.None);
        var agent = new SoftwareArchitectAgent();

        Assert.Equal(agent.AgentId, manifest.Id);
        Assert.Equal(agent.Version, manifest.Version);
        Assert.Contains(SoftwareArchitectProfile.DesignCapability, manifest.Capabilities);
        Assert.Contains(SoftwareArchitectProfile.PublishCapability, manifest.Capabilities);
        Assert.Contains(AgentConfigurationCapabilities.Describe, manifest.Capabilities);
        Assert.Contains(AgentConfigurationCapabilities.Update, manifest.Capabilities);
        Assert.Equal("AlwaysOn", manifest.Runtime.DefaultActivationMode);
        Assert.Equal(1, manifest.Runtime.MaximumConcurrentJobs);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var configuration = document.RootElement.GetProperty("configuration").EnumerateArray().ToArray();
        Assert.Equal(
            SoftwareArchitectProfile.DefaultContextWindowTokens,
            configuration.Single(field => field.GetProperty("key").GetString() == "maxContextWindowTokens")
                .GetProperty("defaultValue").GetInt32());
        Assert.Equal(
            SoftwareArchitectProfile.DefaultOutputTokens,
            configuration.Single(field => field.GetProperty("key").GetString() == "maxOutputTokens")
                .GetProperty("defaultValue").GetInt32());
        Assert.Equal(
            SoftwareArchitectProfile.DefaultSprintLengthDays,
            configuration.Single(field => field.GetProperty("key").GetString() == "defaultSprintLengthDays")
                .GetProperty("defaultValue").GetInt32());
        Assert.True(File.Exists(Path.Combine(
            root,
            manifest.Runtime.ProjectPath!.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void ManifestRequestsOnlyReviewedArchitectureAuthority()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "csweet-plugin.json")));
        var root = document.RootElement;
        var required = root.GetProperty("requires")
            .EnumerateArray()
            .Select(x => x.GetProperty("name").GetString()!)
            .ToArray();

        Assert.Equal(
            [
                PersonalTodoCapabilities.Read,
                PersonalTodoCapabilities.Add,
                PersonalTodoCapabilities.Reorder,
                PersonalTodoCapabilities.Requeue,
                PersonalTodoCapabilities.Claim,
                PersonalTodoCapabilities.Complete,
                PersonalTodoCapabilities.Block,
                PersonalTodoCapabilities.Release,
                PlatformCapabilities.LlmChatStream,
                PlatformCapabilities.BusinessProfileRead,
                PlatformCapabilities.OrganizationSnapshotRead,
                PlatformCapabilities.TeamRosterRead,
                CommunicationCapabilities.ChatRead,
                CommunicationCapabilities.ChatCreate,
                CommunicationCapabilities.MessageSend,
                CommunicationCapabilities.CoordinationStart,
                CommunicationCapabilities.CoordinationRespond,
                CommunicationCapabilities.CoordinationRead,
                CommunicationCapabilities.CoordinationCancel,
                AgentLifecycleCapabilities.CompleteOnboarding,
                WorkBoardCapabilities.Read,
                WorkItemCapabilities.Read,
                WorkItemCapabilities.Create,
                WorkItemCapabilities.FinalizeDelivery,
                WorkItemCapabilities.Estimate,
                WorkItemCapabilities.Move,
                WorkSprintCapabilities.Read,
                WorkSprintCapabilities.Create,
                WorkSprintCapabilities.ManageScope,
                WorkSprintCapabilities.ReadReports,
                GitMergeCapabilities.Review,
                GitMergeCapabilities.Authorize,
                SourceControlCapabilities.ProvisionRepository
            ],
            required);
        Assert.Empty(root.GetProperty("credentials").EnumerateArray());
        Assert.Equal("None", root.GetProperty("webAccess").GetProperty("mode").GetString());
        Assert.Empty(root.GetProperty("webAccess").GetProperty("rules").EnumerateArray());
        Assert.Equal("None", root.GetProperty("runtime").GetProperty("workspaceAccess").GetString());
        Assert.Equal(
            [PersonalTodoEvents.Available, CommunicationEvents.MessageMentioned,
                AgentLifecycleEvents.Onboarded, SoftwareArchitectProfile.UserMessageReceivedEvent,
                AgentCoordinationEvents.TurnRequested],
            root.GetProperty("events").GetProperty("subscribes")
                .EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.DoesNotContain(GitWorkspaceCapabilities.Prepare, required);
        Assert.DoesNotContain(WorkItemCapabilities.Start, required);
        Assert.DoesNotContain(WorkItemCapabilities.Complete, required);
        Assert.DoesNotContain(WorkSprintCapabilities.Start, required);
        Assert.DoesNotContain(WorkSprintCapabilities.Complete, required);
    }

    [Fact]
    public void SourceContainsNoShellFileOrAmbientInfrastructureAccess()
    {
        var source = string.Join(
            "\n",
            Directory.EnumerateFiles(
                    Path.Combine(RepositoryRoot(), "src"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Select(File.ReadAllText));
        Assert.DoesNotContain("LocalShellExecutor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FileSystemAgentFileStore", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new HttpClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Diagnostics.Process", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonRpc", source, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "CSweet.Agents.SoftwareArchitect.slnx")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
