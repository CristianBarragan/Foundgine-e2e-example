namespace Crm.Api.Application;

public sealed class CrmValidationException(string message) : Exception(message);
public sealed class CrmConflictException(string message) : Exception(message);
public sealed class CrmNotFoundException(string message) : Exception(message);
public sealed class CrmForbiddenException(string message) : Exception(message);
