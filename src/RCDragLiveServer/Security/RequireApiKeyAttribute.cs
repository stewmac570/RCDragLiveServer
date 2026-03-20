using Microsoft.AspNetCore.Mvc;

namespace RCDragLiveServer.Security;

public sealed class RequireApiKeyAttribute() : TypeFilterAttribute(typeof(ApiKeyAuthorizationFilter));
