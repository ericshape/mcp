// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine;
using System.Net;
using Azure.Mcp.Core.Models.Command;
using Azure.Mcp.Core.Options;
using Azure.Mcp.Tools.Sql.Commands.Database;
using Azure.Mcp.Tools.Sql.Models;
using Azure.Mcp.Tools.Sql.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Azure.Mcp.Tools.Sql.UnitTests.Database;

public class DatabaseRestoreCommandTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISqlService _sqlService;
    private readonly ILogger<DatabaseRestoreCommand> _logger;
    private readonly DatabaseRestoreCommand _command;
    private readonly CommandContext _context;
    private readonly Command _commandDefinition;

    public DatabaseRestoreCommandTests()
    {
        _sqlService = Substitute.For<ISqlService>();
        _logger = Substitute.For<ILogger<DatabaseRestoreCommand>>();

        var collection = new ServiceCollection();
        collection.AddSingleton(_sqlService);
        _serviceProvider = collection.BuildServiceProvider();

        _command = new(_logger);
        _context = new(_serviceProvider);
        _commandDefinition = _command.GetCommand();
    }

    [Fact]
    public void Constructor_InitializesCommandMetadata()
    {
        var command = _command.GetCommand();
        Assert.Equal("restore", command.Name);
        Assert.Contains("Restore an Azure SQL Database", command.Description);
    }

    [Fact]
    public async Task ExecuteAsync_WithAllParameters_RestoresDatabase()
    {
        var restorePoint = DateTimeOffset.UtcNow.AddHours(-2);
        var database = new SqlDatabase(
            Name: "restored-db",
            Id: "/subscriptions/targetSub/resourceGroups/targetRg/providers/Microsoft.Sql/servers/targetServer/databases/restored-db",
            Type: "Microsoft.Sql/servers/databases",
            Location: "East US",
            Sku: new DatabaseSku("S0", "Standard", 10, null, null),
            Status: "Online",
            Collation: "SQL_Latin1_General_CP1_CI_AS",
            CreationDate: DateTimeOffset.UtcNow.AddYears(-1),
            MaxSizeBytes: 2147483648,
            ServiceLevelObjective: "S0",
            Edition: "Standard",
            ElasticPoolName: null,
            EarliestRestoreDate: DateTimeOffset.UtcNow.AddDays(-7),
            ReadScale: "Disabled",
            ZoneRedundant: false);

        _sqlService.RestoreDatabaseAsync(
                Arg.Is("targetServer"),
                Arg.Is("restored-db"),
                Arg.Is("targetRg"),
                Arg.Is("targetSub"),
                Arg.Is("sourceServer"),
                Arg.Is("sourceDb"),
                Arg.Is("sourceRg"),
                Arg.Is("sourceSub"),
                Arg.Is(restorePoint),
                Arg.Is("pool1"),
                Arg.Any<RetryPolicyOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(database);

        var args = _commandDefinition.Parse([
            "--subscription", "targetSub",
            "--resource-group", "targetRg",
            "--server", "targetServer",
            "--database", "restored-db",
            "--source-server", "sourceServer",
            "--source-database", "sourceDb",
            "--source-resource-group", "sourceRg",
            "--source-subscription", "sourceSub",
            "--restore-point-in-time", restorePoint.ToString("O"),
            "--elastic-pool-name", "pool1"
        ]);

        var response = await _command.ExecuteAsync(_context, args);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("Success", response.Message);
        await _sqlService.Received(1).RestoreDatabaseAsync(
            "targetServer",
            "restored-db",
            "targetRg",
            "targetSub",
            "sourceServer",
            "sourceDb",
            "sourceRg",
            "sourceSub",
            restorePoint,
            "pool1",
            Arg.Any<RetryPolicyOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_DefaultsSourceToTarget_WhenSourceOptionsMissing()
    {
        var restorePoint = DateTimeOffset.UtcNow.AddHours(-6);
        _sqlService.RestoreDatabaseAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string?>(),
                Arg.Any<RetryPolicyOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(new SqlDatabase(
                Name: "restored-db",
                Id: "id",
                Type: "Microsoft.Sql/servers/databases",
                Location: "East US",
                Sku: null,
                Status: "Online",
                Collation: null,
                CreationDate: DateTimeOffset.UtcNow,
                MaxSizeBytes: null,
                ServiceLevelObjective: null,
                Edition: null,
                ElasticPoolName: null,
                EarliestRestoreDate: null,
                ReadScale: null,
                ZoneRedundant: null));

        var args = _commandDefinition.Parse([
            "--subscription", "sub",
            "--resource-group", "rg",
            "--server", "server1",
            "--database", "restored-db",
            "--source-database", "source-db",
            "--restore-point-in-time", restorePoint.ToString("O")
        ]);

        await _command.ExecuteAsync(_context, args);

        await _sqlService.Received(1).RestoreDatabaseAsync(
            "server1",
            "restored-db",
            "rg",
            "sub",
            "server1",
            "source-db",
            "rg",
            "sub",
            restorePoint,
            null,
            Arg.Any<RetryPolicyOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_RestorePointInFuture_ReturnsValidationError()
    {
        var futurePoint = DateTimeOffset.UtcNow.AddHours(2);

        var args = _commandDefinition.Parse([
            "--subscription", "sub",
            "--resource-group", "rg",
            "--server", "server1",
            "--database", "restored-db",
            "--source-database", "source-db",
            "--restore-point-in-time", futurePoint.ToString("O")
        ]);

        var response = await _command.ExecuteAsync(_context, args);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Contains("cannot be in the future", response.Message);
        Assert.Empty(_sqlService.ReceivedCalls());
    }

    [Fact]
    public async Task ExecuteAsync_HandlesServiceErrors()
    {
        var restorePoint = DateTimeOffset.UtcNow.AddHours(-3);
        _sqlService.RestoreDatabaseAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string?>(),
                Arg.Any<RetryPolicyOptions?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("restore failed"));

        var args = _commandDefinition.Parse([
            "--subscription", "sub",
            "--resource-group", "rg",
            "--server", "server1",
            "--database", "restored-db",
            "--source-database", "source-db",
            "--restore-point-in-time", restorePoint.ToString("O")
        ]);

        var response = await _command.ExecuteAsync(_context, args);

        Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
        Assert.Contains("restore failed", response.Message);
        Assert.Contains("troubleshooting", response.Message);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesRequestFailedExceptions()
    {
        var restorePoint = DateTimeOffset.UtcNow.AddHours(-4);
        var exception = new RequestFailedException(409, "Conflict");
        _sqlService.RestoreDatabaseAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string?>(),
                Arg.Any<RetryPolicyOptions?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(exception);

        var args = _commandDefinition.Parse([
            "--subscription", "sub",
            "--resource-group", "rg",
            "--server", "server1",
            "--database", "restored-db",
            "--source-database", "source-db",
            "--restore-point-in-time", restorePoint.ToString("O")
        ]);

        var response = await _command.ExecuteAsync(_context, args);

        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.Contains("target name already exists", response.Message);
    }
}
