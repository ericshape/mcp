// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine.Parsing;
using System.Net;
using Azure.Mcp.Core.Commands;
using Azure.Mcp.Core.Extensions;
using Azure.Mcp.Tools.Sql.Models;
using Azure.Mcp.Tools.Sql.Options;
using Azure.Mcp.Tools.Sql.Options.Database;
using Azure.Mcp.Tools.Sql.Services;
using Microsoft.Extensions.Logging;

namespace Azure.Mcp.Tools.Sql.Commands.Database;

public sealed class DatabaseRestoreCommand(ILogger<DatabaseRestoreCommand> logger)
    : BaseDatabaseCommand<DatabaseRestoreOptions>(logger)
{
    private const string CommandTitle = "Restore SQL Database";

    public override string Name => "restore";

    public override string Description =>
        """
        Restore an Azure SQL Database from a point-in-time backup. This command creates a new database by restoring from
        an existing database's backup history. Equivalent to 'az sql db restore'. Returns the restored database details.
        """;

    public override string Title => CommandTitle;

    public override ToolMetadata Metadata => new()
    {
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        ReadOnly = false,
        LocalRequired = false,
        Secret = false
    };

    protected override void RegisterOptions(Command command)
    {
        base.RegisterOptions(command);
        command.Options.Add(SqlOptionDefinitions.SourceServerOption);
        command.Options.Add(SqlOptionDefinitions.SourceDatabaseOption);
        command.Options.Add(SqlOptionDefinitions.SourceResourceGroupOption);
        command.Options.Add(SqlOptionDefinitions.SourceSubscriptionOption);
        command.Options.Add(SqlOptionDefinitions.RestorePointInTimeOption);
        command.Options.Add(SqlOptionDefinitions.ElasticPoolNameOption);
    }

    protected override DatabaseRestoreOptions BindOptions(ParseResult parseResult)
    {
        var options = base.BindOptions(parseResult);
        options.SourceServer = parseResult.GetValueOrDefault<string>(SqlOptionDefinitions.SourceServerOption);
        options.SourceDatabase = parseResult.GetValueOrDefault<string>(SqlOptionDefinitions.SourceDatabaseOption);
        options.SourceResourceGroup = parseResult.GetValueOrDefault<string>(SqlOptionDefinitions.SourceResourceGroupOption);
        options.SourceSubscription = parseResult.GetValueOrDefault<string>(SqlOptionDefinitions.SourceSubscriptionOption);
        var restorePoint = parseResult.GetValueOrDefault<DateTimeOffset>(SqlOptionDefinitions.RestorePointInTimeOption);
        options.RestorePointInTime = restorePoint;
        options.ElasticPoolName = parseResult.GetValueOrDefault<string>(SqlOptionDefinitions.ElasticPoolNameOption);
        return options;
    }

    public override ValidationResult Validate(CommandResult commandResult, CommandResponse? commandResponse = null)
    {
        var validation = base.Validate(commandResult, commandResponse);
        if (!validation.IsValid)
        {
            return validation;
        }

        if (commandResult.TryGetValue(SqlOptionDefinitions.RestorePointInTimeOption, out DateTimeOffset restorePoint)
            && restorePoint > DateTimeOffset.UtcNow)
        {
            const string message = "The --restore-point-in-time value cannot be in the future.";
            validation.IsValid = false;
            validation.ErrorMessage = message;
            if (commandResponse != null)
            {
                commandResponse.Status = HttpStatusCode.BadRequest;
                commandResponse.Message = message;
            }
        }

        return validation;
    }

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

            var sourceServer = string.IsNullOrWhiteSpace(options.SourceServer) ? options.Server! : options.SourceServer!;
            var sourceResourceGroup = string.IsNullOrWhiteSpace(options.SourceResourceGroup) ? options.ResourceGroup! : options.SourceResourceGroup!;
            var sourceSubscription = string.IsNullOrWhiteSpace(options.SourceSubscription) ? options.Subscription! : options.SourceSubscription!;

            var database = await sqlService.RestoreDatabaseAsync(
                options.Server!,
                options.Database!,
                options.ResourceGroup!,
                options.Subscription!,
                sourceServer,
                options.SourceDatabase!,
                sourceResourceGroup,
                sourceSubscription,
                options.RestorePointInTime!.Value,
                options.ElasticPoolName,
                options.RetryPolicy);

            context.Response.Results = ResponseResult.Create(new(database), SqlJsonContext.Default.DatabaseRestoreResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error restoring SQL database. TargetServer: {TargetServer}, TargetDatabase: {TargetDatabase}, TargetResourceGroup: {TargetResourceGroup}, SourceServer: {SourceServer}, SourceDatabase: {SourceDatabase}",
                options.Server, options.Database, options.ResourceGroup, options.SourceServer ?? options.Server, options.SourceDatabase);
            HandleException(context, ex);
        }

        return context.Response;
    }

    protected override string GetErrorMessage(Exception ex) => ex switch
    {
        RequestFailedException reqEx when reqEx.Status == 404 =>
            "Source SQL server or database not found. Verify the source server, database, resource group, and subscription.",
        RequestFailedException reqEx when reqEx.Status == 403 =>
            $"Authorization failed restoring the SQL database. Verify you have appropriate permissions. Details: {reqEx.Message}",
        RequestFailedException reqEx when reqEx.Status == 409 =>
            "A database with the target name already exists. Choose a different target database name or delete the existing database first.",
        RequestFailedException reqEx when reqEx.Status == 400 =>
            $"Invalid restore request. Check the restore point, source database, and other parameters. Details: {reqEx.Message}",
        RequestFailedException reqEx => reqEx.Message,
        _ => base.GetErrorMessage(ex)
    };

    internal record DatabaseRestoreResult(SqlDatabase Database);
}
