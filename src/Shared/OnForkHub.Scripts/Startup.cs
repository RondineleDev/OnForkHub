namespace OnForkHub.Scripts;

public class Startup(ILogger logger, GitFlowConfiguration gitFlow, GitFlowPullRequestConfiguration prConfig, ICliHandler cliHandler)
{
    public async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Contains("-h"))
            {
                cliHandler.ShowHelp();
                return 0;
            }

            if (!await gitFlow.VerifyGitInstallationAsync())
            {
                logger.Log(ELogLevel.Error, "Git not installed");
                return 1;
            }

            await gitFlow.EnsureGitFlowConfiguredAsync();

            if (await cliHandler.HandlePackageCommand(args))
            {
                return 0;
            }

            if (args.Contains("-p"))
            {
                await gitFlow.EnsureCleanWorkingTreeAsync();
                await prConfig.CreatePullRequestForGitFlowFinishAsync();
                return 0;
            }

            if (args.Length > 0)
            {
                logger.Log(ELogLevel.Error, "Unknown command. Use -h for help.");
                return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            logger.Log(ELogLevel.Error, ex.Message);
            logger.Log(ELogLevel.Debug, ex.StackTrace ?? string.Empty);
            return 1;
        }
    }
}
