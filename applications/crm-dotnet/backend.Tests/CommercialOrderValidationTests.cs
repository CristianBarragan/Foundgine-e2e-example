using System.Text.Json;
using Foundgine.Crm.Api;
using Xunit;

namespace Foundgine.Crm.Semantic.Tests;

public sealed class CommercialOrderValidationTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static CreateCommercialOrder Order(string currency = "nzd", params CommercialOrderLineInput[] lines) =>
        new(Company, currency,
            lines.Length == 0 ? new[] { new CommercialOrderLineInput(" SKU-1 ", 2m, " EA ") } : lines);

    [Fact]
    public void Normalize_trims_and_uppercases()
    {
        var n = CommercialOrderValidation.Normalize(Order());
        Assert.Equal("NZD", n.Currency);
        Assert.Equal("SKU-1", n.Lines[0].ProductId);
        Assert.Equal("EA", n.Lines[0].UnitOfMeasure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("NZ")]
    [InlineData("N2D")]
    [InlineData("NZDD")]
    public void Normalize_rejects_bad_currency(string currency) =>
        Assert.Throws<CrmValidationException>(() => CommercialOrderValidation.Normalize(Order(currency)));

    [Fact]
    public void Normalize_rejects_empty_company_and_empty_lines()
    {
        Assert.Throws<CrmValidationException>(() =>
            CommercialOrderValidation.Normalize(new(Guid.Empty, "NZD", [new("A", 1, "EA")])));
        Assert.Throws<CrmValidationException>(() =>
            CommercialOrderValidation.Normalize(new(Company, "NZD", [])));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(1000001d)]
    public void Normalize_rejects_out_of_range_quantity(double quantity) =>
        Assert.Throws<CrmValidationException>(() =>
            CommercialOrderValidation.Normalize(Order("NZD", new("A", (decimal)quantity, "EA"))));

    [Fact]
    public void Normalize_rejects_more_than_three_decimal_places() =>
        Assert.Throws<CrmValidationException>(() =>
            CommercialOrderValidation.Normalize(Order("NZD", new("A", 1.0001m, "EA"))));

    [Fact]
    public void Normalize_rejects_blank_product_and_too_many_lines()
    {
        Assert.Throws<CrmValidationException>(() =>
            CommercialOrderValidation.Normalize(Order("NZD", new("  ", 1, "EA"))));
        var many = Enumerable.Range(0, CommercialOrderValidation.MaxLines + 1)
            .Select(i => new CommercialOrderLineInput($"P{i}", 1, "EA")).ToArray();
        Assert.Throws<CrmValidationException>(() => CommercialOrderValidation.Normalize(Order("NZD", many)));
    }

    [Fact]
    public void Fingerprint_ignores_line_order_and_whitespace_but_not_content()
    {
        var a = CommercialOrderValidation.Normalize(Order("NZD", new("A", 1, "EA"), new("B", 2.5m, "KG")));
        var b = CommercialOrderValidation.Normalize(Order("nzd", new(" B ", 2.50m, "KG"), new("A", 1.0m, "EA")));
        var c = CommercialOrderValidation.Normalize(Order("NZD", new("A", 1, "EA"), new("B", 3m, "KG")));

        Assert.Equal(CommercialOrderValidation.Fingerprint(a), CommercialOrderValidation.Fingerprint(b));
        Assert.NotEqual(CommercialOrderValidation.Fingerprint(a), CommercialOrderValidation.Fingerprint(c));
        Assert.Equal(64, CommercialOrderValidation.Fingerprint(a).Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("has space")]
    public void Idempotency_key_is_required_and_printable(string? key) =>
        Assert.Throws<CrmValidationException>(() => CommercialOrderValidation.NormalizeIdempotencyKey(key));

    [Fact]
    public void Outbox_payload_matches_the_orchestrator_workflow_input_shape()
    {
        var order = CommercialOrderValidation.Normalize(Order("NZD", new("SKU-9", 4m, "EA")));
        var id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var json = CommercialOrderValidation.SerializePayload(
            CommercialOrderValidation.BuildPayload("tenant-a", id, "CO-1", order));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        // Keys must equal CommercialOrderWorkflowInput under JsonSerializerDefaults.Web (the dispatcher's reader).
        Assert.Equal(
            new[] { "currency", "crmCommercialOrderId", "customerId", "lines", "orderNumber", "tenantId" },
            root.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Equal("tenant-a", root.GetProperty("tenantId").GetString());
        Assert.Equal(id.ToString(), root.GetProperty("crmCommercialOrderId").GetString());
        Assert.Equal(Company.ToString(), root.GetProperty("customerId").GetString());
        var line = root.GetProperty("lines")[0];
        Assert.Equal("SKU-9", line.GetProperty("productId").GetString());
        Assert.Equal(4m, line.GetProperty("quantity").GetDecimal());
        Assert.Equal("EA", line.GetProperty("unitOfMeasure").GetString());
    }

    [Fact]
    public void Callback_must_match_route_order_and_carry_an_idempotency_key()
    {
        var id = Guid.NewGuid();
        var ok = CommercialOrderValidation.ValidateCallback(id,
            new("tenant-a", id.ToString(), " res-1 ", "  done ", "tenant-a:" + id + ":confirm-crm"));
        Assert.Equal("res-1", ok.ExternalId);
        Assert.Equal("done", ok.Reason);

        Assert.Throws<CrmValidationException>(() => CommercialOrderValidation.ValidateCallback(id,
            new("tenant-a", Guid.NewGuid().ToString(), "x", "r", "k")));
        Assert.Throws<CrmValidationException>(() => CommercialOrderValidation.ValidateCallback(id,
            new("tenant-a", id.ToString(), "x", "r", "")));
        Assert.Throws<CrmValidationException>(() => CommercialOrderValidation.ValidateCallback(id,
            new("", id.ToString(), "x", "r", "k")));
    }
}
