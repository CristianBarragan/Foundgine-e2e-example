using System.Text.Json;
using Crm.Api.Integration;
using Xunit;

namespace Crm.Api.Tests;

public sealed class CustomerToCashContractTests
{
    [Fact]
    public void CustomerToCashStart_UsesTheVersionedCamelCaseContractAndPreservesDecimalValues()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "tenantId": "acme",
          "quoteId": 7,
          "opportunityId": 3,
          "crmCustomerId": 42,
          "correlationId": "corr-quote-7",
          "approvalRequired": false,
          "quote": {
            "schemaVersion": 1,
            "tenantId": "acme",
            "quoteId": 7,
            "opportunityId": 3,
            "crmCustomerId": 42,
            "erpCustomerId": "ERP-9001",
            "correlationId": "corr-quote-7",
            "total": 200.00,
            "lines": [
              { "sku": "SKU-1", "description": "Widget", "quantity": 2, "unitPrice": 100.00 }
            ]
          }
        }
        """;

        var contract = JsonSerializer.Deserialize<CustomerToCashStart>(json);

        Assert.NotNull(contract);
        Assert.Equal(1, contract.SchemaVersion);
        Assert.Equal("acme", contract.TenantId);
        Assert.False(contract.ApprovalRequired);
        Assert.Equal(7, contract.Quote.QuoteId);
        Assert.Equal(200.00m, contract.Quote.Total);
        Assert.Equal(100.00m, contract.Quote.Lines[0].UnitPrice);

        var roundTrip = JsonSerializer.Deserialize<CustomerToCashStart>(JsonSerializer.Serialize(contract));
        Assert.NotNull(roundTrip);
        Assert.Equal(contract.TenantId, roundTrip.TenantId);
        Assert.Equal(contract.Quote.Total, roundTrip.Quote.Total);
        Assert.Equal(contract.Quote.Lines[0].Sku, roundTrip.Quote.Lines[0].Sku);
    }

    [Fact]
    public void ParentAndJavaChildHaveDistinctDeterministicWorkflowIds()
    {
        Assert.Equal("CustomerToCash:acme:7", CustomerToCashNames.WorkflowId("acme", 7));
        Assert.Equal("QuoteToOrder:acme:7", CustomerToCashNames.JavaWorkflowId("acme", 7));
    }
}
