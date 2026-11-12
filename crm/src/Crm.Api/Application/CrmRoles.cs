namespace Crm.Api.Application;

public static class CrmRoles
{
    public const string EnterpriseAdmin = "enterprise-admin";
    public const string CrmAdmin = "crm-admin";
    public const string CrmSales = "crm-sales";
    public const string CrmSupport = "crm-support";

    public static readonly string[] Read = [EnterpriseAdmin, CrmAdmin, CrmSales, CrmSupport];
    public static readonly string[] Write = [EnterpriseAdmin, CrmAdmin, CrmSales];

    public static readonly string[] Admin = [EnterpriseAdmin, CrmAdmin];
    public const string AdminPolicy = "crm.admin";
    public const string ReadPolicy = "crm.read";
    public const string WritePolicy = "crm.write";
}
