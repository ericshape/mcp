// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.Mcp.Core.Commands;
using Azure.Mcp.Tools.Sql.Models;
using Azure.Mcp.Tools.Sql.Options.Database;
using Azure.Mcp.Tools.Sql.Services;
using Microsoft.Extensions.Logging;

namespace Azure.Mcp.Tools.Sql.Commands.Database;

public sealed class DatabaseTdeShowCommand(ILogger<DatabaseTdeShowCommand> logger)
    : BaseDatabaseCommand<DatabaseTdeShowOptions>(logger)
{
    private const string CommandTitle = "Show SQL Database TDE Status";

    public override string Name => "show";

    public override string Description =>
        """
        Get the Transparent Data Encryption (TDE) configuration for an Azure SQL Database. TDE provides 
        encryption-at-rest to protect data and log files. This command retrieves the current TDE status 
        (Enabled/Disabled) for the specified database. Equivalent to 'az sql db tde show'.
        
        Required options:
        - subscription: Azure subscription ID
        - resource-group: Resource group name containing the SQL server
        - server: Azure SQL Server name
        - database: Database name to retrieve TDE status for
        """;

    public override string Title => CommandTitle;

    public override ToolMetadata Metadata => new()
    {
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        ReadOnly = true,
        LocalRequired = false,
        Secret = false
    };

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, ParseResult parseResult)
    {
        if (!Validate(parseResult.CommandResult, context.Response).IsValid)
        {
            return context.Response;
        }

        var options = BindOptions(parseResult);

        try
        {
            var sqlService = context.GetService<ISqlService>();

            var tde = await sqlService.GetDatabaseTransparentDataEncryptionAsync(
                options.Server!,
                options.Database!,
                options.ResourceGroup!,
                options.Subscription!,
                options.RetryPolicy);

            context.Response.Results = ResponseResult.Create(new(tde), SqlJsonContext.Default.DatabaseTdeShowResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error retrieving TDE configuration. Server: {Server}, Database: {Database}, ResourceGroup: {ResourceGroup}, Options: {@Options}",
                options.Server, options.Database, options.ResourceGroup, options);
            HandleException(context, ex);
        }

        return context.Response;
    }

    protected override string GetErrorMessage(Exception ex) => ex switch
    {
        KeyNotFoundException =>
            "TDE configuration not found. Verify the database name, server name, resource group, and that you have access.",
        RequestFailedException reqEx when reqEx.Status == (int)HttpStatusCode.NotFound =>
            "Database, server, or TDE configuration not found. Verify the database name, server name, resource group, and that you have access.",
        RequestFailedException reqEx when reqEx.Status == (int)HttpStatusCode.Forbidden =>
            $"Authorization failed accessing TDE configuration. Verify you have appropriate permissions. Details: {reqEx.Message}",
        RequestFailedException reqEx => reqEx.Message,
        ArgumentException argEx => $"Invalid parameter: {argEx.Message}",
        _ => base.GetErrorMessage(ex)
    };

    protected override HttpStatusCode GetStatusCode(Exception ex) => ex switch
    {
        KeyNotFoundException => HttpStatusCode.NotFound,
        RequestFailedException reqEx => (HttpStatusCode)reqEx.Status,
        ArgumentException => HttpStatusCode.BadRequest,
        _ => base.GetStatusCode(ex)
    };

    internal record DatabaseTdeShowResult(SqlTransparentDataEncryption TransparentDataEncryption);
}
