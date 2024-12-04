namespace OnForkHub.Scripts.Git;

public sealed class GitFlowConfiguration(ILogger logger, IProcessRunner processRunner)
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));

    public async Task EnsureCleanWorkingTreeAsync()
    {
        try
        {
            _logger.Log(ELogLevel.Info, "Checking for unstaged changes...");
            var statusOutput = await _processRunner.RunAsync("git", "status --porcelain");

            if (!string.IsNullOrWhiteSpace(statusOutput))
            {
                const string errorMessage = "Working tree contains unstaged changes. Please commit or stash changes before proceeding.";
                _logger.Log(ELogLevel.Error, errorMessage);
                throw new GitOperationException(errorMessage);
            }

            _logger.Log(ELogLevel.Info, "Working tree is clean.");
        }
        catch (Exception ex) when (ex is not GitOperationException)
        {
            _logger.Log(ELogLevel.Error, "Failed to verify clean working tree:");
            _logger.Log(ELogLevel.Error, ex.Message);
            throw new GitOperationException("Failed to verify working tree state.", ex);
        }
    }

    public async Task EnsureGitFlowConfiguredAsync()
    {
        try
        {
            _logger.Log(ELogLevel.Info, "Checking Git Flow configuration...");

            if (!Directory.Exists(Path.Combine(Environment.CurrentDirectory, ".git")))
            {
                await _processRunner.RunAsync("git", "init");
                _logger.Log(ELogLevel.Info, "Git repository initialized");
            }

            try
            {
                await _processRunner.RunAsync("git", "flow version");
            }
            catch
            {
                _logger.Log(ELogLevel.Info, "Git Flow not configured, initializing...");
                await InitializeGitFlow();
            }

            await EnsureRequiredBranchesExistAsync();

            _logger.Log(ELogLevel.Info, "Git Flow configuration completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.Log(ELogLevel.Error, "An error occurred while configuring Git Flow:");
            _logger.Log(ELogLevel.Error, ex.Message);
            throw;
        }
    }

    public async Task<bool> VerifyGitInstallationAsync()
    {
        try
        {
            _logger.Log(ELogLevel.Info, "Checking Git installation...");
            var gitVersion = await _processRunner.RunAsync("git", "--version");
            _logger.Log(ELogLevel.Info, $"Git Version: {gitVersion.Trim()}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Log(ELogLevel.Error, "Failed to verify Git installation:");
            _logger.Log(ELogLevel.Error, ex.Message);
            return false;
        }
    }

    private static bool IsFeatureBranch(string branchName)
    {
        return branchName.StartsWith("feature/", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ConfigureGitFlow()
    {
        var configs = new Dictionary<string, string>
        {
            { "gitflow.branch.master", "main" },
            { "gitflow.branch.develop", "dev" },
            { "gitflow.prefix.feature", "feature/" },
            { "gitflow.prefix.bugfix", "bugfix/" },
            { "gitflow.prefix.release", "release/" },
            { "gitflow.prefix.hotfix", "hotfix/" },
            { "gitflow.prefix.support", "support/" },
            { "gitflow.prefix.versiontag", "v" },
            { "gitflow.feature.start.fetch", "true" },
            { "gitflow.feature.finish.fetch", "true" },
            { "gitflow.feature.finish", "false" },
            { "gitflow.feature.no-ff", "true" },
            { "gitflow.feature.no-merge", "true" },
            { "gitflow.feature.keepbranch", "true" },
            { "gitflow.path.hooks", ".husky" },
        };

        foreach (var config in configs)
        {
            try
            {
                await _processRunner.RunAsync("git", $"config --local {config.Key} {config.Value}");
                _logger.Log(ELogLevel.Debug, $"Set {config.Key} to {config.Value}");
            }
            catch (Exception ex)
            {
                _logger.Log(ELogLevel.Warning, $"Failed to set {config.Key}: {ex.Message}");
            }
        }

        await SetGitFlowVersion();
    }

    private async Task CreateBranch(string branchName)
    {
        try
        {
            _logger.Log(ELogLevel.Info, $"Creating {branchName} branch...");
            await _processRunner.RunAsync("git", $"branch {branchName}");

            try
            {
                await _processRunner.RunAsync("git", $"push -u origin {branchName}");
                _logger.Log(ELogLevel.Info, $"Pushed {branchName} branch to remote.");
            }
            catch
            {
                _logger.Log(ELogLevel.Warning, $"Could not push {branchName} branch to remote. This is normal for new repositories.");
            }
        }
        catch (Exception ex)
        {
            _logger.Log(ELogLevel.Error, $"Error creating {branchName} branch: {ex.Message}");
            throw;
        }
    }

    private async Task EnsureRequiredBranchesExistAsync()
    {
        var currentBranch = (await _processRunner.RunAsync("git", "rev-parse --abbrev-ref HEAD")).Trim();
        var branches = (await _processRunner.RunAsync("git", "branch"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(b => b.Trim('*', ' '))
            .ToList();

        if (!branches.Contains("main") && !IsFeatureBranch(currentBranch))
        {
            await CreateBranch("main");
        }

        if (!branches.Contains("dev") && !IsFeatureBranch(currentBranch))
        {
            await CreateBranch("dev");
        }
    }

    private async Task InitializeGitFlow()
    {
        try
        {
            _logger.Log(ELogLevel.Info, "Initializing Git Flow...");

            var processInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "flow init",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(processInfo) ?? throw new GitOperationException("Failed to start Git Flow init process");

            using (var writer = process.StandardInput)
            {
                await writer.WriteLineAsync("main");
                await writer.WriteLineAsync("dev");
                await writer.WriteLineAsync("feature/");
                await writer.WriteLineAsync("bugfix/");
                await writer.WriteLineAsync("release/");
                await writer.WriteLineAsync("hotfix/");
                await writer.WriteLineAsync("support/");
                await writer.WriteLineAsync("v");
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new GitOperationException($"Git Flow init failed: {error}");
            }

            _logger.Log(ELogLevel.Info, "Git Flow initialization completed.");

            await ConfigureGitFlowOptions();
        }
        catch (Exception ex)
        {
            _logger.Log(ELogLevel.Error, "Failed to initialize Git Flow:");
            _logger.Log(ELogLevel.Error, ex.Message);
            throw;
        }
    }

    private async Task ConfigureGitFlowOptions()
    {
        var options = new Dictionary<string, string>
        {
            { "gitflow.feature.finish", "false" },
            { "gitflow.feature.no-ff", "true" },
            { "gitflow.feature.no-merge", "true" },
            { "gitflow.feature.keepbranch", "true" },
            { "gitflow.feature.start.fetch", "true" },
            { "gitflow.feature.finish.fetch", "true" },
        };

        foreach (var option in options)
        {
            await _processRunner.RunAsync("git", $"config --local {option.Key} {option.Value}");
        }
    }

    private async Task<bool> IsGitFlowConfigured()
    {
        try
        {
            var result = await _processRunner.RunAsync("git", "config --local gitflow.initialized");
            return !string.IsNullOrWhiteSpace(result);
        }
        catch
        {
            return false;
        }
    }

    private async Task SetGitFlowVersion()
    {
        try
        {
            await _processRunner.RunAsync("git", "config --local gitflow.initialized true");
            await _processRunner.RunAsync("git", "config --local gitflow.version 1.12.3");
            _logger.Log(ELogLevel.Debug, "Git Flow version set successfully.");
        }
        catch (Exception ex)
        {
            _logger.Log(ELogLevel.Warning, $"Git Flow version setting warning: {ex.Message}");
        }
    }
}
