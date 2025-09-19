// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Options;
using Azure.Mcp.Core.Services.Azure.Subscription;
using Azure.Mcp.Core.Services.Azure.Tenant;
using Azure.Mcp.Tools.Sql.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Azure.Mcp.Tools.Sql.UnitTests.Services;

/// <summary>
/// Unit tests for SqlService subscription resolution functionality.
/// These tests verify that the subscription resolution bug fix works correctly.
/// </summary>
public class SqlServiceTests
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ITenantService _tenantService;
    private readonly ILogger<SqlService> _logger;
    private readonly SqlService _sqlService;

    public SqlServiceTests()
    {
        _subscriptionService = Substitute.For<ISubscriptionService>();
        _tenantService = Substitute.For<ITenantService>();
        _logger = Substitute.For<ILogger<SqlService>>();
        
        _sqlService = new SqlService(_subscriptionService, _tenantService, _logger);
    }

    /// <summary>
    /// This test demonstrates that the bug fix works correctly by ensuring
    /// that subscription names are resolved through the subscription service
    /// before being passed to ARM client operations.
    /// </summary>
    [Fact]
    public async Task SubscriptionResolution_Verification()
    {
        // This test verifies that:
        // 1. The SQL service properly calls ISubscriptionService.GetSubscription()
        // 2. Both subscription names and GUIDs are handled through the same resolution path
        // 3. The service no longer directly passes subscription strings to CreateResourceIdentifier()

        var testCases = new[]
        {
            "My Development Subscription",  // Display name
            "12345678-1234-1234-1234-123456789012"  // GUID
        };

        foreach (var subscription in testCases)
        {
            // Reset the mock for each test case
            _subscriptionService.ClearReceivedCalls();
            
            // Arrange
            _subscriptionService.GetSubscription(subscription, null, Arg.Any<RetryPolicyOptions?>())
                .ThrowsAsync(new InvalidOperationException($"Resolution verified for: {subscription}"));

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _sqlService.GetDatabaseAsync("server", "db", "rg", subscription, null, CancellationToken.None));

            Assert.Contains($"Resolution verified for: {subscription}", exception.Message);
            
            // Verify subscription service was called exactly once for this subscription
            await _subscriptionService.Received(1).GetSubscription(subscription, null, Arg.Any<RetryPolicyOptions?>());
        }
    }
}