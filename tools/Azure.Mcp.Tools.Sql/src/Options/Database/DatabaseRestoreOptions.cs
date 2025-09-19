// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.Sql.Options.Database;

public sealed class DatabaseRestoreOptions : BaseDatabaseOptions
{
    [JsonPropertyName(SqlOptionDefinitions.SourceServerName)]
    public string? SourceServer { get; set; }

    [JsonPropertyName(SqlOptionDefinitions.SourceDatabaseName)]
    public string? SourceDatabase { get; set; }

    [JsonPropertyName(SqlOptionDefinitions.SourceResourceGroupName)]
    public string? SourceResourceGroup { get; set; }

    [JsonPropertyName(SqlOptionDefinitions.SourceSubscription)]
    public string? SourceSubscription { get; set; }

    [JsonPropertyName(SqlOptionDefinitions.RestorePointInTime)]
    public DateTimeOffset? RestorePointInTime { get; set; }

    [JsonPropertyName(SqlOptionDefinitions.ElasticPoolName)]
    public string? ElasticPoolName { get; set; }
}
