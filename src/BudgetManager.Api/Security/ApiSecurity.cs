namespace BudgetManager.Api.Security;

public static class ApiRoles
{
    public const string EnterpriseAdmin = "EnterpriseAdmin";
    public const string Operator = "Operator";
    public const string Auditor = "Auditor";
}

public static class ApiPolicies
{
    public const string EnterpriseAdmin = "EnterpriseAdmin";
    public const string Operator = "OperatorOrAdmin";
    public const string Auditor = "AuditorOrAbove";
}

public static class ApiRateLimits
{
    public const string AdministrativeWrites = "AdministrativeWrites";
}
